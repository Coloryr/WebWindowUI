using System.Collections.Concurrent;
using System.Text;
using WebWindowUI.Core;
using WebWindowUI.Core.Protocol;
using WebWindowUI.Natives.Windows;
using Xilium.CefGlue;
using Xilium.CefGlue.BrowserProcess;
using Xilium.CefGlue.Common;
using Xilium.CefGlue.Common.Shared;

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

        CefRuntimeLoader.Initialize(new CefSettings
        {
            RootCachePath = cachePath,
            NoSandbox = true,
            LogSeverity = CefLogSeverity.Verbose,
            LogFile = Path.Combine(logPath, "cef_debug.log"),
        }, customSchemes: 
        [
            new CustomScheme() { SchemeName = WebWindowResource.Scheme, SchemeHandlerFactory = new ResourceSchemeHandlerFactory() },
            new CustomScheme() { SchemeName = WebWindowResource.SchemeData, SchemeHandlerFactory = new MessageSchemeHandlerFactory() }
        ]);

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
    /// CefApp 子类：唯一两个回调——on_before_command_line_processing（--disable-gpu）与
    /// on_register_custom_schemes（app/appbin）。每个进程（浏览器 + 各子进程）都执行，
    /// scheme 注册须全进程一致（CEF 要求），--disable-gpu 只注入浏览器进程。
    /// </summary>
    internal sealed class WwuiCefApp : CefApp
    {
        protected override void OnRegisterCustomSchemes(CefSchemeRegistrar registrar)
        {
            registrar.AddCustomScheme(WebWindowResource.Scheme, SchemeOptions);
            registrar.AddCustomScheme(WebWindowResource.SchemeData, SchemeOptions);
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
        protected override void Cancel() { }
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
