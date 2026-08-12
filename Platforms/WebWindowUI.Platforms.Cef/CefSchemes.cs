using System.Collections.Concurrent;
using System.Text;
using WebWindowUI.Core;
using WebWindowUI.Core.Protocol;
using Xilium.CefGlue;

namespace WebWindowUI.Cef;

/// <summary>
/// CEF 自定义 scheme 支撑：app（UI 静态资源）+ appbin（数据通道）两套 scheme 的资源提供
/// 与 JS → native 消息回传通道。逐行镜像 Windows/Linux 平台的资源路由：
///   GET  → WebResourceLocator 定位 + WebResourceResolver/DataRoutes 读流，回填 status/mime/Cache-Control；
///   POST → 消息回传通道（前端桥 resolveSendChannel 的 CEF 分支经 fetch POST 到 app://localhost/__wwui，
///          body 是 NUL 转义串的 UTF-8 编码），读 post data → UTF-8 解码 → WebView2StringCodec.Decode
///          还原 protobuf 字节 → marshal 回 UI 线程投递对应窗口的 MessageReceived。
///
/// CefGlue 结构（替换原手写 C API 扁平回调，见 git 历史）：
///   - <see cref="WwuiCefApp"/>（CefApp 子类）：OnRegisterCustomSchemes 在 cef_initialize 期间由 CEF
///     回调（每个进程，scheme 须全进程一致），经 CefSchemeRegistrar.AddCustomScheme 注册 app/appbin；
///     进程启动早期 OnBeforeCommandLineProcessing 注入 --disable-gpu（仅浏览器进程）。
///   - cef_initialize 之后 <see cref="RegisterHandlerFactories"/>：CefRuntime.RegisterSchemeHandlerFactory
///     注册 app/appbin 各一个 <see cref="WwuiSchemeHandlerFactory"/> 实例。
///   - 工厂 Create 收到每个请求（IO 线程），按 request 方法分派：POST=消息 / GET=资源；
///     浏览器指针 → 窗口的映射由 <see cref="CefWindow"/> 在 on_after_created/on_before_close 维护。
///
/// 线程模型：工厂 Create 与 resource handler 全部回调（Open/GetResponseHeaders/Read/Skip/Cancel）
/// 都在 CEF IO 线程到达——资源读取线程安全（WebResourceResolver 懒扫描带锁、磁盘读每请求独立），
/// 消息投递经 <see cref="MessageLoopSynchronizationContext.Post"/> marshal 回 UI 线程（模型更新/推送要求）。
/// 浏览器映射用 ConcurrentDictionary（跨 UI↔IO 线程读写）。
///
/// 生命周期：resource handler 是 CefGlue HANDLER 角色对象，工厂返回时 CefGlue 把 ToNative() 的
/// add_ref 交给 CEF（_roots 字典按引用计数保活托管对象），请求完成/取消后 CEF release 归零 → 自动释放。
/// app / factory 是进程期对象，static 字段显式保活，绝不被 GC 提前回收。
/// </summary>
internal static class CefSchemes
{
    // 默认 scheme 名（与 WebWindowOptions.Scheme/DataScheme 默认一致；scheme 须在 cef_initialize 前
    // 经 OnRegisterCustomSchemes 注册，故全局钉死默认值而非按窗口——自定义 scheme 值是本平台限制，文档注明）。
    internal const string SchemeName = "app";
    internal const string DataSchemeName = "appbin";

    private const CefSchemeOptions SchemeOptions =
        CefSchemeOptions.Standard
        | CefSchemeOptions.DisplayIsolated
        | CefSchemeOptions.Secure
        | CefSchemeOptions.CorsEnabled
        | CefSchemeOptions.FetchEnabled;

    // ---- 浏览器 id → 窗口（工厂 Create 分派用；UI 线程注册/摘除、IO 线程读取）----
    // durable：不能按 CefBrowser 指针键——on_after_created（UI 线程）与工厂 Create（IO 线程）拿到的
    // browser 包装可能不是同一份，首屏请求会"无对应窗口" 404。用 GetIdentifier 的全局唯一 id（进程内稳定）。
    private static readonly ConcurrentDictionary<int, CefWindow> _browsers = new();

    // 进程期保活（CEF 持有原生引用；这里再显式 static 引用，杜绝托管对象被提前回收）
    private static WwuiCefApp? _app;
    private static WwuiSchemeHandlerFactory? _factoryApp;
    private static WwuiSchemeHandlerFactory? _factoryData;

    /// <summary>创建进程期 app 实例（OnRegisterCustomSchemes + --disable-gpu），供 ExecuteProcess/Initialize 使用。幂等。</summary>
    internal static CefApp CreateApp() => _app ??= new WwuiCefApp();

    /// <summary>注册 app/appbin 的 scheme handler 工厂。必须在 cef_initialize 之后、任何浏览器请求之前调用。幂等。</summary>
    internal static void RegisterHandlerFactories()
    {
        if (_factoryApp is not null)
            return;
        // 每个 scheme 独立工厂实例：RegisterSchemeHandlerFactory 会对工厂 add_ref，
        // 同一实例注册两次会在 shutdown 时双 release（引用计数穿透）——分开分配互不影响。
        _factoryApp = new WwuiSchemeHandlerFactory();
        _factoryData = new WwuiSchemeHandlerFactory();
        CefRuntime.RegisterSchemeHandlerFactory(SchemeName, null, _factoryApp);
        CefRuntime.RegisterSchemeHandlerFactory(DataSchemeName, null, _factoryData);
    }

    /// <summary>on_after_created：记录浏览器 id → 窗口映射，供工厂 Create 分派回对应窗口。</summary>
    internal static void RegisterBrowser(CefBrowser browser, CefWindow window)
        => _browsers[browser.Identifier] = window;

    /// <summary>on_before_close：摘除映射，避免回调落到已关闭窗口。</summary>
    internal static void UnregisterBrowser(CefBrowser browser)
        => _browsers.TryRemove(browser.Identifier, out _);

    /// <summary>
    /// 当前请求匹配的窗口（工厂 Create 分派用）。
    /// 竞态：首屏导航的 app:// 请求在 IO 线程到达时，on_after_created（UI 线程）可能还没注册
    /// 映射——直接查表会 404 掉首页。UI 线程不被阻塞，短时轮询等映射注册即可
    /// （on_after_created 在 CreateBrowser 成功后就必然触发）。超时回退到"无对应窗口"404。
    /// </summary>
    internal static bool TryGetWindow(CefBrowser browser, out CefWindow? window)
    {
        var id = browser.Identifier;
        if (_browsers.TryGetValue(id, out window))
            return true;
        for (int i = 0; i < 500; i++) // 最多 5 秒，10ms 步进
        {
            Thread.Sleep(10);
            if (_browsers.TryGetValue(id, out window))
                return true;
        }
        window = null;
        return false;
    }

    // ===== WwuiCefApp：进程级 app（scheme 注册 + 命令行注入）=====

    /// <summary>
    /// CefApp 子类：唯一两个回调——on_before_command_line_processing（--disable-gpu）与
    /// on_register_custom_schemes（app/appbin）。每个进程（浏览器 + 各子进程）都执行，
    /// scheme 注册须全进程一致（CEF 要求），--disable-gpu 只注入浏览器进程。
    /// </summary>
    internal sealed class WwuiCefApp : CefApp
    {
        /// <summary>
        /// 每个进程启动早期由 CEF 回调；process_type 为空 = 浏览器进程。
        /// 这里只改浏览器进程命令行：--disable-gpu 关闭 GPU 硬件加速（软件合成），子进程命令行继承自浏览器。
        /// 动机：本机 GPU 进程启动即崩（exit_code=-2147483645 = STATUS_BREAKPOINT，
        /// gpu_channel_manager.cc "Failed to create shared context for virtualization"——ANGLE/D3D
        /// 建共享上下文失败后 __debugbreak），3 次后 Chromium 放弃 GPU，渲染器卡死在
        /// "Timeout of new browser info response"——页面无法渲染。durable：CEF 桌面壳默认开硬件加速，
        /// 但 D3D 在虚拟机/远程会话上可能不可用，须显式 --disable-gpu 兜底。
        /// </summary>
        protected override void OnBeforeCommandLineProcessing(string processType, CefCommandLine commandLine)
        {
            if (!string.IsNullOrEmpty(processType))
                return; // 只注入浏览器进程；子进程继承其命令行
            commandLine.AppendSwitch("disable-gpu");
        }

        protected override void OnRegisterCustomSchemes(CefSchemeRegistrar registrar)
        {
            registrar.AddCustomScheme(SchemeName, SchemeOptions);
            registrar.AddCustomScheme(DataSchemeName, SchemeOptions);
        }
    }

    // ===== 工厂 Create（IO 线程）：按请求方法分派 POST=消息 / GET=资源 =====

    internal sealed class WwuiSchemeHandlerFactory : CefSchemeHandlerFactory
    {
        protected override CefResourceHandler Create(CefBrowser browser, CefFrame frame, string schemeName, CefRequest request)
        {
            try
            {
                var method = request.Method ?? "GET";
                if (string.Equals(method, "POST", StringComparison.OrdinalIgnoreCase))
                    return CreateMessageHandler(browser, request); // 前端桥 fetch 回传通道

                // GET：按发起浏览器分派回对应窗口，走该窗口的 resolver
                if (TryGetWindow(browser, out CefWindow? window))
                    return CreateResourceHandler(window!, request);
            }
            catch
            {
                // 单个请求失败回退 404，不影响其他请求
            }
            return CreateNotFoundHandler();
        }
    }

    /// <summary>POST 消息通道：读 post data 还原 protobuf 字节 → marshal 回 UI 线程投递窗口；响应 204。</summary>
    private static CefResourceHandler CreateMessageHandler(CefBrowser browser, CefRequest request)
    {
        var payload = ReadPostDataPayload(request);
        if (TryGetWindow(browser, out CefWindow? window))
            MessageLoopSynchronizationContext.Instance.Post(_ => window!.OnMessageFromWeb(payload), null);
        return CreateHandler([], "text/plain; charset=utf-8", 204, null);
    }

    /// <summary>GET 资源：WebResourceLocator 定位 + 窗口 resolver 读流 → 整读进 byte[]（与 Linux 平台同策略）。</summary>
    private static CefResourceHandler CreateResourceHandler(CefWindow window, CefRequest request)
    {
        try
        {
            var uri = request.Url ?? "";
            var options = window.Options;
            var isData = WebWindowResource.IsScheme(uri, options.DataScheme);
            var scheme = isData ? options.DataScheme! : options.Scheme;
            var resolver = isData ? (Func<string, Stream?>)DataRoutes.Resolve : WebResourceResolver.Resolve;

            if (resolver is not null && WebWindowResource.TryResolvePath(uri, scheme, out string? relative, out string? mimeType))
            {
                using var stream = resolver(relative!);
                if (stream is not null)
                {
                    using var ms = new MemoryStream();
                    stream.CopyTo(ms);
                    return CreateHandler(ms.ToArray(), mimeType!, 200, ResourceHeaders.CacheControl(relative!));
                }
            }
        }
        catch
        {
            // 读取或构造响应失败时回退 404（与 Windows/Linux 一致）
        }
        return CreateNotFoundHandler();
    }

    // ===== resource handler（IO 线程）=====

    private static CefResourceHandler CreateHandler(byte[] data, string mime, int status, string? cacheControl)
        => new WwuiResourceHandler(data, mime, status, cacheControl);

    private static CefResourceHandler CreateNotFoundHandler()
        => CreateHandler(Encoding.UTF8.GetBytes("404 Not Found"), "text/plain; charset=utf-8", 404, "no-store");

    /// <summary>单请求 resource handler：整读进内存后同步输出（Open 即完成 → GetResponseHeaders → Read）。</summary>
    internal sealed class WwuiResourceHandler(byte[] data, string mime, int status, string? cacheControl) : CefResourceHandler
    {
        private int _offset;

        /// <summary>同步处理：handle_request=1 + 返回 true → CEF 继续 GetResponseHeaders → Read。</summary>
        protected override bool Open(CefRequest request, out bool handleRequest, CefCallback callback)
        {
            handleRequest = true;
            return true;
        }

        protected override void GetResponseHeaders(CefResponse response, out long responseLength, out string redirectUrl)
        {
            responseLength = data.LongLength;
            redirectUrl = string.Empty; // 不重定向（CefGlue 签名非空 out string）
            response.Status = status;
            response.StatusText = StatusText(status);
            response.MimeType = mime;

            // **durable：自定义 scheme 必须带 Access-Control-Allow-Origin: \***。vite 产物 HTML 的
            // `<script type="module" crossorigin>` 走 CORS-mode fetch，即使页面与脚本同源，CEF 对自定义
            // scheme 的 CORS 校验仍可能拦在请求到达 handler 之前（表现 = 首屏导航 RES-HIT 后子资源
            // 一个都不来）。cefclient 的 scheme 示例也是这么回头的。同源场景 ACAO 无副作用。
            response.SetHeaderByName("Access-Control-Allow-Origin", "*", false);
            if (cacheControl is not null)
                response.SetHeaderByName("Cache-Control", cacheControl, false);
        }

        protected override bool Read(Stream response, int bytesToRead, out int bytesRead, CefResourceReadCallback callback)
        {
            bytesRead = Math.Min(bytesToRead, data.Length - _offset);
            if (bytesRead > 0)
            {
                response.Write(data, _offset, bytesRead);
                _offset += bytesRead;
                return true; // 还有数据可读
            }
            bytesRead = 0;
            return false; // bytes_read=0 + 返回 false = 响应完成
        }

        protected override bool Skip(long bytesToSkip, out long bytesSkipped, CefResourceSkipCallback callback)
        {
            var remaining = data.Length - _offset;
            var actual = Math.Min(bytesToSkip, remaining);
            _offset += (int)actual;
            bytesSkipped = actual;
            return _offset < data.Length;
        }

        /// <summary>请求被取消：byte[] 可 GC（CefGlue 的 HANDLER 引用计数归零时释放原生包装）。</summary>
        protected override void Cancel() { }
    }

    private static string StatusText(int status) => status switch
    {
        200 => "OK",
        204 => "No Content",
        404 => "Not Found",
        _ => "OK",
    };

    // ===== 请求读取 =====

    /// <summary>读 POST body：CEF 的 fetch POST 把 JS 字符串按 UTF-8 编码成 post data 字节，
    /// 这里 UTF-8 解码还原成 NUL 转义串，再经 WebView2StringCodec.Decode 还原成 protobuf 字节
    /// （与前端 bytesToEscaped 对称，桥两侧同一 codec）。</summary>
    private static byte[] ReadPostDataPayload(CefRequest request)
    {
        var postData = request.PostData;
        if (postData is null)
            return Array.Empty<byte>();
        var merged = new List<byte>();
        foreach (var element in postData.GetElements())
            merged.AddRange(element.GetBytes());
        if (merged.Count == 0)
            return Array.Empty<byte>();
        // fetch 把 JS 字符串按 UTF-8 编码成 body 字节，先解码回 NUL 转义串，再还原成 protobuf 字节
        var escaped = Encoding.UTF8.GetString([.. merged]);
        return WebView2StringCodec.Decode(escaped);
    }
}
