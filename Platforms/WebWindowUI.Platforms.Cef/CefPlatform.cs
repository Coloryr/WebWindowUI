using System.Collections.Concurrent;
using System.Text;
using WebWindowUI.Core;
using WebWindowUI.Core.Protocol;
using WebWindowUI.Natives.Windows;
using Xilium.CefGlue;
using Xilium.CefGlue.BrowserProcess;

namespace WebWindowUI.Platforms.Cef;

/// <summary>
/// CEF 平台实现（Windows：CefGlue 托管包装 + 裸 Win32 子窗口 + 启动自动下载运行时）。
/// 与 WebWindowUI.Platforms.Windows 互斥：入口在 UseCEF=true 时改引本包（WWUIPlatform 必为 Windows）。
///
/// 消息循环：单线程模式（multi_threaded_message_loop=false）→ CEF UI 线程 == 主线程。
/// RunMessageLoop 用 CefRuntime.RunMessageLoop()（CEF 内部完整消息环：泵 Windows 消息 + CEF 任务，
/// 隐式处理 WM_RUN 隐藏窗调度与末窗 WM_QUIT 退出），返回后同线程 CefRuntime.Shutdown()。
/// </summary>
public sealed class CefPlatform : IWebWindowPlatform
{
    private const CefSchemeOptions SchemeOptions =
        CefSchemeOptions.Standard
        | CefSchemeOptions.DisplayIsolated
        | CefSchemeOptions.Secure
        | CefSchemeOptions.CorsEnabled
        | CefSchemeOptions.FetchEnabled;

    private static readonly IMessageLoop _message = new Win32MessageLoop();

    private static readonly ConcurrentDictionary<int, CefWindow> _browsers = new();

    public CefPlatform()
    {
#if !MACOS
        CefSubProcess.Run(Environment.GetCommandLineArgs(), true);
#endif

        var cachePath = Path.Combine(Path.GetTempPath(), "CefGlue", Environment.ProcessId.ToString());
        var logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "logs");
        Directory.CreateDirectory(logPath);

        // **durable：CefRuntimeLoader.Initialize 只是登记延迟初始化委托，真实 cef_initialize
        // （CefRuntime.Initialize）要等 CefRuntimeLoader.Load() 才触发；且 Load() 在 Windows 上
        // 强制 MultiThreadedMessageLoop=true，与本平台单线程消息循环设计（CEF UI 线程==主线程、
        // CefRuntime.RunMessageLoop）冲突。此前从不调 Load() → CEF 从未初始化 →
        // CefBrowserHost.CreateBrowser 对未初始化 libcef 调 create_browser → 原生崩溃（EXIT=3）。
        // 这里绕开 loader 直接单线程初始化（MTML=false，经典 CefGlue 用法），CreateBrowser 才在 UI 线程安全。**
        // **durable：ResourcesDirPath/LocalesDirPath 必须显式指向 app 基目录（cef_initialize 后
        // resource_bundle 加载 en-US.pak 需要，否则 locale_file_path.empty() abort；CEF 151 的
        // ICU 只认 libcef.dll 所在目录（DIR_ASSETS），icudtl.dat 已随 runtime 平铺在基目录，
        // resources_dir_path 对它无效但 .pak/locales 仍需此设置）。**
        CefRuntime.Initialize(
            new CefMainArgs(Environment.GetCommandLineArgs()),
            new CefSettings
            {
                RootCachePath = cachePath,
                NoSandbox = true,
                MultiThreadedMessageLoop = false,
                ResourcesDirPath = AppContext.BaseDirectory,
                LocalesDirPath = Path.Combine(AppContext.BaseDirectory, "locales"),
                LogSeverity = CefLogSeverity.Verbose,
                LogFile = Path.Combine(logPath, "cef_debug.log"),
            },
            new WwuiCefApp(),
            IntPtr.Zero);

        // scheme 处理器工厂须在 cef_initialize 后注册（loader 的 InternalInitialize 亦在此注册）。
        CefRuntime.RegisterSchemeHandlerFactory(WebWindowResource.Scheme, "", new ResourceSchemeHandlerFactory());
        CefRuntime.RegisterSchemeHandlerFactory(WebWindowResource.SchemeData, "", new MessageSchemeHandlerFactory());

        _message.InitMessageLoop();
    }

    /// <summary>
    /// on_after_created：记录浏览器 id → 窗口映射，供工厂 Create 分派回对应窗口。
    /// </summary>
    internal static void RegisterBrowser(CefBrowser browser, CefWindow window)
        => _browsers[browser.Identifier] = window;

    /// <summary>
    /// on_before_close：摘除映射，避免回调落到已关闭窗口。
    /// </summary>
    internal static void UnregisterBrowser(CefBrowser browser)
        => _browsers.TryRemove(browser.Identifier, out _);

    /// <summary>
    /// 当前请求匹配的窗口
    /// </summary>
    internal static bool TryGetWindow(CefBrowser browser, out CefWindow? window)
    {
        var id = browser.Identifier;
        if (_browsers.TryGetValue(id, out window))
            return true;
        for (int i = 0; i < 500; i++)
        {
            Thread.Sleep(10);
            if (_browsers.TryGetValue(id, out window))
                return true;
        }
        window = null;
        return false;
    }

    /// <summary>
    /// CefApp 子类：浏览器进程侧。on_register_custom_schemes（app/appbin）注册自定义 scheme
    /// （scheme 注册须全进程一致，CEF 要求），on_before_child_process_launch 把同一份 scheme 列表
    /// 经 --custom-scheme 传给各子进程（绕过 loader 后由本处理器补齐，格式同 Common.Shared 的
    /// CustomScheme.ToCommandLineValue：SchemeName|DomainName|Options，; 分隔）。
    /// </summary>
    internal sealed class WwuiCefApp : CefApp
    {
        protected override void OnRegisterCustomSchemes(CefSchemeRegistrar registrar)
        {
            registrar.AddCustomScheme(WebWindowResource.Scheme, SchemeOptions);
            registrar.AddCustomScheme(WebWindowResource.SchemeData, SchemeOptions);
        }

        protected override CefBrowserProcessHandler GetBrowserProcessHandler()
            => new WwuiCefBrowserProcessHandler();
    }

    /// <summary>
    /// 浏览器进程处理器：子进程启动前注入 --custom-scheme（renderer 侧 CustomScheme.FromCommandLineValue
    /// 还原注册，否则 appbin:// fetch 在 renderer 里被 CORS 门控）与 --parent-pid（子进程监听父进程退出）。
    /// 镜像 loader 的 CommonBrowserProcessHandler。
    /// </summary>
    internal sealed class WwuiCefBrowserProcessHandler : CefBrowserProcessHandler
    {
        private readonly string _customSchemes =
            $"{WebWindowResource.Scheme}||{(int)SchemeOptions};{WebWindowResource.SchemeData}||{(int)SchemeOptions}";
        private readonly string _parentPid = Environment.ProcessId.ToString();

        protected override void OnBeforeChildProcessLaunch(CefCommandLine commandLine)
        {
            commandLine.AppendSwitch("--custom-scheme", _customSchemes);
            commandLine.AppendSwitch("--parent-pid", _parentPid);
        }
    }

    internal class MessageSchemeHandlerFactory : CefSchemeHandlerFactory
    {
        protected override CefResourceHandler Create(CefBrowser browser, CefFrame frame, string schemeName, CefRequest request)
        {
            try
            {
                var payload = ReadPostDataPayload(request);
                if (TryGetWindow(browser, out var window))
                {
                    _message.RunOnUiThread(() => window!.OnMessageFromWeb(payload));
                }
            }
            catch
            {
                // 单个请求失败回退 404，不影响其他请求
            }
            return new WwuiResourceHandler(Encoding.UTF8.GetBytes("404 Not Found"), "text/plain; charset=utf-8", 404, "no-store");
        }
    }

    internal class ResourceSchemeHandlerFactory : CefSchemeHandlerFactory
    {
        protected override CefResourceHandler Create(CefBrowser browser, CefFrame frame, string schemeName, CefRequest request)
        {
            try
            {
                var uri = request.Url ?? "";

                if (WebWindowResource.TryResolvePath(uri, out string? relative, out string? mimeType) is { } stream)
                {
                    var data = new byte[stream.Length];
                    stream.ReadExactly(data);
                    return new WwuiResourceHandler(data, mimeType!, 200, ResourceHeaders.CacheControl(relative!));
                }
            }
            catch
            {
                // 单个请求失败回退 404，不影响其他请求
            }
            return new WwuiResourceHandler(Encoding.UTF8.GetBytes("404 Not Found"), "text/plain; charset=utf-8", 404, "no-store");
        }
    }

    /// <summary>
    /// 单请求 resource handler：整读进内存后同步输出（Open 即完成 → GetResponseHeaders → Read）。
    /// </summary>
    internal sealed class WwuiResourceHandler(byte[] data, string mime, int status, string? cacheControl) : CefResourceHandler
    {
        private int _offset;

        /// <summary>
        /// 同步处理：handle_request=1 + 返回 true → CEF 继续 GetResponseHeaders → Read。
        /// </summary>
        protected override bool Open(CefRequest request, out bool handleRequest, CefCallback callback)
        {
            handleRequest = true;
            return true;
        }

        protected override void GetResponseHeaders(CefResponse response, out long responseLength, out string redirectUrl)
        {
            responseLength = data.LongLength;
            redirectUrl = string.Empty;
            response.Status = status;
            response.StatusText = StatusText(status);
            response.MimeType = mime;

            response.SetHeaderByName("Access-Control-Allow-Origin", "*", true);
            if (cacheControl is { } cache)
            {
                response.SetHeaderByName("Cache-Control", cache, true);
            }
        }

        protected override bool Read(Stream response, int bytesToRead, out int bytesRead, CefResourceReadCallback callback)
        {
            bytesRead = Math.Min(bytesToRead, data.Length - _offset);
            if (bytesRead > 0)
            {
                response.Write(data, _offset, bytesRead);
                _offset += bytesRead;
                return true;
            }
            bytesRead = 0;
            return false;
        }

        protected override bool Skip(long bytesToSkip, out long bytesSkipped, CefResourceSkipCallback callback)
        {
            var remaining = data.Length - _offset;
            var actual = Math.Min(bytesToSkip, remaining);
            _offset += (int)actual;
            bytesSkipped = actual;
            return _offset < data.Length;
        }

        /// <summary>
        /// 请求被取消：byte[] 可 GC（CefGlue 的 HANDLER 引用计数归零时释放原生包装）。
        /// </summary>
        protected override void Cancel() 
        {
            
        }
    }

    private static string StatusText(int status) => status switch
    {
        200 => "OK",
        204 => "No Content",
        404 => "Not Found",
        _ => "OK",
    };

    /// <summary>读 POST body：CEF 的 fetch POST 把 JS 字符串按 UTF-8 编码成 post data 字节，
    /// 这里 UTF-8 解码还原成 NUL 转义串，再经 WebView2StringCodec.Decode 还原成 protobuf 字节
    /// （与前端 bytesToEscaped 对称，桥两侧同一 codec）。</summary>
    private static byte[] ReadPostDataPayload(CefRequest request)
    {
        var postData = request.PostData;
        if (postData is null)
            return [];
        var merged = new List<byte>();
        foreach (var element in postData.GetElements())
            merged.AddRange(element.GetBytes());
        if (merged.Count == 0)
            return [];
        // fetch 把 JS 字符串按 UTF-8 编码成 body 字节，先解码回 NUL 转义串，再还原成 protobuf 字节
        var escaped = Encoding.UTF8.GetString([.. merged]);
        return WebView2StringCodec.Decode(escaped);
    }

    public IWindowBackend CreateWindow(WebWindowOptions options)
    {
        return new CefWindow(options);
    }

    public void RunMessageLoop()
    {
        CefRuntime.RunMessageLoop();
        CefRuntime.Shutdown();
    }

    /// <summary>
    /// 把动作 marshal 到 UI 线程同步执行：UI 线程直接运行；非 UI 线程经
    /// Win32MessageLoop（回 UI 线程并阻塞等待）。
    /// Win32 窗口 API（DestroyWindow/SetForegroundWindow/SetWindowTextW/SendMessage）都要求 UI 线程。
    /// </summary>
    public void RunOnUiThread(Action action)
        => _message.RunOnUiThread(action);

    /// <summary>
    /// 当前线程是否是 UI 线程（CEF UI 线程 == 主线程）。
    /// </summary>
    public bool IsUiThread() => _message.IsUiThread();

    public void ShowMessageBox(string title, string message, bool error)
        => Win32Native.ShowMessage(title, message, error);

    public string[]? OpenFileDialog(string title, string filter, string? initialDirectory = null, bool fileMustExist = true, bool allowMultiSelect = true)
        => Win32Native.OpenFileDialog(title, filter, initialDirectory, fileMustExist, allowMultiSelect)?.ToArray();

    public string? SaveFileDialog(string title, string filter, string? defaultFileName = null, string? defaultExt = null)
        => Win32Native.SaveFileDialog(title, filter, defaultFileName, defaultExt);
}
