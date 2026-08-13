using System.Collections.Concurrent;
using System.Text;
using WebWindowUI.Core;
using WebWindowUI.Core.Protocol;
using WebWindowUI.Natives.Windows;
using Xilium.CefGlue;
using Xilium.CefGlue.BrowserProcess;

namespace WebWindowUI.Platforms.Cef;

/// <summary>
/// CEF 平台实现（Windows：CefGlue 托管包装 + 裸 Win32 子窗口 + 启动自动下载运行时），与 Windows 平台互斥。
/// 单线程消息循环（MTML=false）：CEF UI 线程 == 主线程，RunMessageLoop 用 CefRuntime.RunMessageLoop()。
/// </summary>
public sealed class CefPlatform : IWebWindowPlatform
{
    /// <summary>
    /// 自定义 scheme 选项（Standard/Secure/CorsEnabled 等）。
    /// </summary>
    private const CefSchemeOptions SchemeOptions =
        CefSchemeOptions.Standard
        | CefSchemeOptions.DisplayIsolated
        | CefSchemeOptions.Secure
        | CefSchemeOptions.CorsEnabled
        | CefSchemeOptions.FetchEnabled;

    /// <summary>
    /// Win32 消息循环（隐藏消息窗口调度，供跨线程 marshal 回 UI 线程）。
    /// </summary>
    private static readonly IMessageLoop _message = new Win32MessageLoop();

    /// <summary>
    /// 浏览器 id → 窗口映射，供 scheme 请求回调分派回对应窗口。
    /// </summary>
    private static readonly ConcurrentDictionary<int, CefWindow> _browsers = new();

    /// <summary>
    /// 初始化 CEF 运行时（单线程模式）并注册 app/appbin 自定义 scheme 处理器。
    /// </summary>
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
    /// <param name="browser">已创建的浏览器。</param>
    /// <param name="window">承载该浏览器的窗口。</param>
    internal static void RegisterBrowser(CefBrowser browser, CefWindow window)
        => _browsers[browser.Identifier] = window;

    /// <summary>
    /// on_before_close：摘除映射，避免回调落到已关闭窗口。
    /// </summary>
    /// <param name="browser">正在关闭的浏览器。</param>
    internal static void UnregisterBrowser(CefBrowser browser)
        => _browsers.TryRemove(browser.Identifier, out _);

    /// <summary>
    /// 当前请求匹配的窗口
    /// </summary>
    /// <param name="browser">发起请求的浏览器。</param>
    /// <param name="window">命中的窗口；未命中为 null。</param>
    /// <returns>是否命中。</returns>
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
    /// 浏览器进程侧 CefApp：注册自定义 scheme，并把 scheme 列表经 --custom-scheme 传给各子进程。
    /// </summary>
    internal sealed class WwuiCefApp : CefApp
    {
        /// <summary>
        /// 注册 app/appdata 自定义 scheme（scheme 注册须全进程一致）。
        /// </summary>
        /// <param name="registrar">scheme 注册器。</param>
        protected override void OnRegisterCustomSchemes(CefSchemeRegistrar registrar)
        {
            registrar.AddCustomScheme(WebWindowResource.Scheme, SchemeOptions);
            registrar.AddCustomScheme(WebWindowResource.SchemeData, SchemeOptions);
        }

        /// <summary>
        /// 返回浏览器进程处理器。
        /// </summary>
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
        /// <summary>
        /// 传给子进程的 --custom-scheme 参数值。
        /// </summary>
        private readonly string _customSchemes =
            $"{WebWindowResource.Scheme}||{(int)SchemeOptions};{WebWindowResource.SchemeData}||{(int)SchemeOptions}";
        /// <summary>
        /// 传给子进程的 --parent-pid 参数值。
        /// </summary>
        private readonly string _parentPid = Environment.ProcessId.ToString();

        /// <summary>
        /// 子进程启动前注入 scheme 列表与父进程 pid（renderer 侧据此还原 scheme 注册与监听父进程退出）。
        /// </summary>
        /// <param name="commandLine">子进程命令行。</param>
        protected override void OnBeforeChildProcessLaunch(CefCommandLine commandLine)
        {
            commandLine.AppendSwitch("--custom-scheme", _customSchemes);
            commandLine.AppendSwitch("--parent-pid", _parentPid);
        }
    }

    /// <summary>
    /// appbin:// scheme 处理器：读 POST body 还原 protobuf 消息并投递给对应窗口。
    /// </summary>
    internal class MessageSchemeHandlerFactory : CefSchemeHandlerFactory
    {
        /// <summary>
        /// 读 POST body 并 marshal 回 UI 线程投递窗口；失败回退 404。
        /// </summary>
        /// <param name="browser">发起请求的浏览器。</param>
        /// <param name="frame">发起请求的 frame。</param>
        /// <param name="schemeName">scheme 名。</param>
        /// <param name="request">请求。</param>
        /// <returns>响应处理器。</returns>
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

    /// <summary>
    /// app:// scheme 处理器：解析 wwwroot/数据通道资源并返回响应。
    /// </summary>
    internal class ResourceSchemeHandlerFactory : CefSchemeHandlerFactory
    {
        /// <summary>
        /// 解析请求 URI 为资源字节并构造响应；失败回退 404。
        /// </summary>
        /// <param name="browser">发起请求的浏览器。</param>
        /// <param name="frame">发起请求的 frame。</param>
        /// <param name="schemeName">scheme 名。</param>
        /// <param name="request">请求。</param>
        /// <returns>响应处理器。</returns>
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
    /// <param name="data">响应内容字节。</param>
    /// <param name="mime">响应 MIME 类型。</param>
    /// <param name="status">响应状态码。</param>
    /// <param name="cacheControl">Cache-Control 响应头；null 则不设。</param>
    internal sealed class WwuiResourceHandler(byte[] data, string mime, int status, string? cacheControl) : CefResourceHandler
    {
        /// <summary>
        /// 当前已读字节偏移。
        /// </summary>
        private int _offset;

        /// <summary>
        /// 同步处理：handle_request=1 + 返回 true → CEF 继续 GetResponseHeaders → Read。
        /// </summary>
        /// <param name="request">请求。</param>
        /// <param name="handleRequest">已同步处理，置 1。</param>
        /// <param name="callback">异步回调（同步完成无需使用）。</param>
        /// <returns>始终 true（数据已就绪）。</returns>
        protected override bool Open(CefRequest request, out bool handleRequest, CefCallback callback)
        {
            handleRequest = true;
            return true;
        }

        /// <summary>
        /// 填响应状态、MIME 与响应头（含 ACAO 与可选 Cache-Control）。
        /// </summary>
        /// <param name="response">响应对象。</param>
        /// <param name="responseLength">响应体长度。</param>
        /// <param name="redirectUrl">无重定向，置空串。</param>
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

        /// <summary>
        /// 输出下一段响应体；读完返回 false。
        /// </summary>
        /// <param name="response">输出流。</param>
        /// <param name="bytesToRead">本次可读字节数。</param>
        /// <param name="bytesRead">实际写入字节数。</param>
        /// <param name="callback">异步回调（同步完成无需使用）。</param>
        /// <returns>是否还有数据可读。</returns>
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

        /// <summary>
        /// 跳过一段响应体；跳到末尾返回 false。
        /// </summary>
        /// <param name="bytesToSkip">要跳过的字节数。</param>
        /// <param name="bytesSkipped">实际跳过的字节数。</param>
        /// <param name="callback">异步回调（同步完成无需使用）。</param>
        /// <returns>是否还有数据可读。</returns>
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

    /// <summary>
    /// 状态码 → 状态文本。
    /// </summary>
    /// <param name="status">HTTP 状态码。</param>
    /// <returns>对应状态文本。</returns>
    private static string StatusText(int status) => status switch
    {
        200 => "OK",
        204 => "No Content",
        404 => "Not Found",
        _ => "OK",
    };

    /// <summary>
    /// 读 POST body：UTF-8 解码 JS 字符串字节还原成 NUL 转义串，再经 WebView2StringCodec.Decode 还原成 protobuf 字节。
    /// </summary>
    /// <param name="request">请求。</param>
    /// <returns>还原后的 protobuf 字节；无 body 为空数组。</returns>
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

    /// <summary>
    /// 创建 CEF 窗口。
    /// </summary>
    /// <param name="options">窗口选项。</param>
    /// <returns>窗口后端。</returns>
    public IWindowBackend CreateWindow(WebWindowOptions options)
    {
        return new CefWindow(options);
    }

    /// <summary>
    /// 运行 CEF 主消息循环（末窗关闭后返回），随后同线程关闭 CEF 运行时。
    /// </summary>
    public void RunMessageLoop()
    {
        CefRuntime.RunMessageLoop();
        CefRuntime.Shutdown();
    }

    /// <summary>
    /// 把动作 marshal 到 UI 线程同步执行（Win32 窗口 API 与 CEF 调用都要求 UI 线程）。
    /// </summary>
    /// <param name="action">要执行的动作。</param>
    public void RunOnUiThread(Action action)
        => _message.RunOnUiThread(action);

    /// <summary>
    /// 当前线程是否是 UI 线程（CEF UI 线程 == 主线程）。
    /// </summary>
    /// <returns>是否在 UI 线程。</returns>
    public bool IsUiThread() => _message.IsUiThread();

    /// <summary>
    /// 系统消息框。
    /// </summary>
    /// <param name="title">标题。</param>
    /// <param name="message">内容。</param>
    /// <param name="error">是否错误样式。</param>
    public void ShowMessageBox(string title, string message, bool error)
        => Win32Native.ShowMessage(title, message, error);

    /// <summary>
    /// 文件打开对话框；取消返回 null。
    /// </summary>
    /// <param name="title">对话框标题。</param>
    /// <param name="filter">文件过滤器。</param>
    /// <param name="initialDirectory">初始目录；null 用默认。</param>
    /// <param name="fileMustExist">是否要求文件存在。</param>
    /// <param name="allowMultiSelect">是否允许多选。</param>
    /// <returns>选中的文件路径；取消为 null。</returns>
    public string[]? OpenFileDialog(string title, string filter, string? initialDirectory = null, bool fileMustExist = true, bool allowMultiSelect = true)
        => Win32Native.OpenFileDialog(title, filter, initialDirectory, fileMustExist, allowMultiSelect)?.ToArray();

    /// <summary>
    /// 文件保存对话框；取消返回 null。
    /// </summary>
    /// <param name="title">对话框标题。</param>
    /// <param name="filter">文件过滤器。</param>
    /// <param name="defaultFileName">默认文件名。</param>
    /// <param name="defaultExt">默认扩展名。</param>
    /// <returns>选择的保存路径；取消为 null。</returns>
    public string? SaveFileDialog(string title, string filter, string? defaultFileName = null, string? defaultExt = null)
        => Win32Native.SaveFileDialog(title, filter, defaultFileName, defaultExt);
}
