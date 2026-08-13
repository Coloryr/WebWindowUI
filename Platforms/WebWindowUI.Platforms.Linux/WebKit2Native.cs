using WebWindowUI.Core;

namespace WebWindowUI.Platforms.Linux;

/// <summary>
/// 手写 P/Invoke 绑定：libwebkit2gtk-4.1（GIR 命名空间 WebKit2-4.1，GTK3 端口）+ libjavascriptcoregtk-4.1。
/// GirCore 只发布 WebKitGTK 6.0（GTK4）的绑定，4.1（GTK3）无托管绑定可换，故按后端实际用到的
/// API 子集手写。本类保持 GTK 无关（只含 WebKit/JavaScriptCore/GObject/GLib/Gio）；GTK 窗口层见
/// GtkNative / LinuxNativeWindow。原生符号经 soname（lib*.so.0）引用，运行时不依赖 dev 符号链接。
///
/// 所有权约定（来自 GIR / WebKitGTK 文档）：
///  - <see cref="webkit_uri_scheme_request_get_uri"/> 返回借用字符串，不要释放；
///  - 完成 scheme 请求走 WebKitURISchemeResponse（≥2.36，能带 HTTP 头）：response_new 以构造属性 ref
///    stream、set_http_headers 以 (transfer full) 接管 headers 所有权（不 ref，调用方不得再释放）、
///    finish_with_response 对 response 也是 ref；故调用方各自 unref stream/response，headers 交出后不碰；
///  - set_http_headers 的 SoupMessageHeaders 必须与 WebKitGTK 自身链接的 libsoup 同版本（soup2/soup3
///    结构体不兼容、释放函数不同，错配即崩溃），初始化时按 /proc/self/maps 探测，见 libsoup 节；
///  - GMemoryInputStream 持有其 GBytes 的引用，故 g_bytes_new 后由 stream 接管、我们释放自己的引用；
///  - jsc_value_to_json / jsc_value_to_string 返回新分配字符串，须 g_free；
///  - GError 无 g_error_get_message() API，message 按公开结构体字段偏移直接读（见 <see cref="ReadAndFreeGError"/>）；
///  - EvaluateJavascriptAsync 的 GAsyncReadyCallback 在主循环线程触发（即框架主线程）。
/// </summary>
internal static partial class WebKit2Native
{
    // ---- 原生库 soname ----
    private const string WebKitLib = "libwebkit2gtk-4.1.so.0";
    private const string JavaScriptCoreLib = "libjavascriptcoregtk-4.1.so.0";
    private const string GObjectLib = "libgobject-2.0.so.0";
    private const string GLibLib = "libglib-2.0.so.0";
    private const string GioLib = "libgio-2.0.so.0";
    private const string SoupLib3 = "libsoup-3.0.so.0"; // soup3（WebKitGTK ≥ 2.42 / Ubuntu 24.04 等）
    private const string SoupLib2 = "libsoup-2.4.so.1"; // soup2（WebKitGTK < 2.42 / Ubuntu 22.04、Debian 12 等）

    /// <summary>
    /// WebKitLoadEvent 枚举（WEBKIT_LOAD_*）。
    /// </summary>
    public enum LoadEvent
    {
        Started = 0,
        Redirected = 1,
        Committed = 2,
        Finished = 3,
    }

    // ==================== GObject ====================

    [LibraryImport(GObjectLib, EntryPoint = "g_object_ref")]
    public static partial IntPtr g_object_ref(IntPtr obj);

    [LibraryImport(GObjectLib, EntryPoint = "g_object_unref")]
    public static partial void g_object_unref(IntPtr obj);

    [LibraryImport(GLibLib, EntryPoint = "g_error_free")]
    private static partial void g_error_free(IntPtr error);

    // ==================== GLib / Gio ====================

    [LibraryImport(GLibLib, EntryPoint = "g_free")]
    private static partial void g_free(IntPtr mem);

    [LibraryImport(GLibLib, EntryPoint = "g_bytes_new")]
    private static partial IntPtr g_bytes_new([In] byte[] data, nuint len);

    [LibraryImport(GLibLib, EntryPoint = "g_bytes_unref")]
    private static partial void g_bytes_unref(IntPtr bytes);

    [LibraryImport(GioLib, EntryPoint = "g_memory_input_stream_new_from_bytes")]
    private static partial IntPtr g_memory_input_stream_new_from_bytes(IntPtr bytes);

    // ==================== WebKit2 4.1 ====================

    [LibraryImport(WebKitLib, EntryPoint = "webkit_web_view_new")]
    private static partial IntPtr webkit_web_view_new();

    [LibraryImport(WebKitLib, EntryPoint = "webkit_web_view_load_uri")]
    private static partial void webkit_web_view_load_uri(IntPtr view, [MarshalAs(UnmanagedType.LPUTF8Str)] string uri);

    [LibraryImport(WebKitLib, EntryPoint = "webkit_web_view_get_user_content_manager")]
    private static partial IntPtr webkit_web_view_get_user_content_manager(IntPtr view);

    [LibraryImport(WebKitLib, EntryPoint = "webkit_web_view_evaluate_javascript")]
    private static partial void webkit_web_view_evaluate_javascript(
        IntPtr view,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string script,
        nint length,
        IntPtr worldName,
        IntPtr sourceUri,
        IntPtr cancellable,
        IntPtr callback,
        IntPtr userData);

    [LibraryImport(WebKitLib, EntryPoint = "webkit_web_view_evaluate_javascript_finish")]
    private static partial IntPtr webkit_web_view_evaluate_javascript_finish(IntPtr view, IntPtr result, out IntPtr error);

    [LibraryImport(WebKitLib, EntryPoint = "webkit_web_context_get_default")]
    private static partial IntPtr webkit_web_context_get_default();

    // LibraryImport 不支持回调委托参数（SYSLIB1051），改 IntPtr + GetFunctionPointerForDelegate；
    // 委托实例由调用方静态字段保活（LinuxPlatform._schemeCallback）。
    [LibraryImport(WebKitLib, EntryPoint = "webkit_web_context_register_uri_scheme")]
    private static partial void webkit_web_context_register_uri_scheme(
        IntPtr context,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string scheme,
        IntPtr callback,
        IntPtr userData,
        IntPtr destroyNotify);

    [LibraryImport(WebKitLib, EntryPoint = "webkit_user_content_manager_register_script_message_handler")]
    private static partial int webkit_user_content_manager_register_script_message_handler(
        IntPtr manager,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name);

    [LibraryImport(WebKitLib, EntryPoint = "webkit_uri_scheme_request_get_uri")]
    private static partial IntPtr webkit_uri_scheme_request_get_uri(IntPtr request);

    [LibraryImport(WebKitLib, EntryPoint = "webkit_uri_scheme_request_get_web_view")]
    private static partial IntPtr webkit_uri_scheme_request_get_web_view(IntPtr request);

    // WebKitURISchemeResponse（≥ 2.36）：旧 finish 只能带 content-type，响应头（ACAO）须走 response API。
    [LibraryImport(WebKitLib, EntryPoint = "webkit_uri_scheme_response_new")]
    private static partial IntPtr webkit_uri_scheme_response_new(IntPtr stream, long streamLength);

    [LibraryImport(WebKitLib, EntryPoint = "webkit_uri_scheme_response_set_content_type")]
    private static partial void webkit_uri_scheme_response_set_content_type(
        IntPtr response, [MarshalAs(UnmanagedType.LPUTF8Str)] string contentType);

    [LibraryImport(WebKitLib, EntryPoint = "webkit_uri_scheme_response_set_http_headers")]
    private static partial void webkit_uri_scheme_response_set_http_headers(IntPtr response, IntPtr headers);

    [LibraryImport(WebKitLib, EntryPoint = "webkit_uri_scheme_request_finish_with_response")]
    private static partial void webkit_uri_scheme_request_finish_with_response(IntPtr request, IntPtr response);

    // 跨源 fetch 门控：自定义 scheme 默认不开放跨源，须在 security manager 注册为 CORS-enabled。
    [LibraryImport(WebKitLib, EntryPoint = "webkit_web_context_get_security_manager")]
    private static partial IntPtr webkit_web_context_get_security_manager(IntPtr context);

    [LibraryImport(WebKitLib, EntryPoint = "webkit_security_manager_register_uri_scheme_as_cors_enabled")]
    private static partial void webkit_security_manager_register_uri_scheme_as_cors_enabled(
        IntPtr manager, [MarshalAs(UnmanagedType.LPUTF8Str)] string scheme);

    [LibraryImport(WebKitLib, EntryPoint = "webkit_security_manager_register_uri_scheme_as_secure")]
    private static partial void webkit_security_manager_register_uri_scheme_as_secure(
        IntPtr manager, [MarshalAs(UnmanagedType.LPUTF8Str)] string scheme);

    [LibraryImport(WebKitLib, EntryPoint = "webkit_javascript_result_get_js_value")]
    private static partial IntPtr webkit_javascript_result_get_js_value(IntPtr jsResult);

    // ==================== libsoup（构造 WebKitURISchemeResponse 的 HTTP 头） ====================

    // 关键坑（durable）一：set_http_headers 是 (transfer full)——以 GUniquePtr 接管 headers 所有权、不 ref，
    // 调用方传完绝不能自己再 unref/free（旧实现这么干 → response 持有的指针被提前释放，WebKit 异步读回调
    // 迭代它 + 析构时 GUniquePtr 再释放一次 → double-free/UAF 段错误，且旧 finish 不碰 headers 所以「换旧
    // API 就正常」）。headers 交出去后所有权归 WebKit 侧。
    //
    // 关键坑（durable）二：set_http_headers 期望的 SoupMessageHeaders 必须与 WebKitGTK 自身链接的 libsoup
    // 同版本。webkit2gtk-4.1 的 libsoup 依赖随发行版/版本而异：
    //   WebKitGTK < 2.42（Ubuntu 22.04、Debian 12 等）→ libsoup-2.4.so.1（soup2）
    //   WebKitGTK ≥ 2.42（Ubuntu 24.04 等）→ libsoup-3.0.so.0（soup3）
    // soup2/soup3 的 SoupMessageHeaders 内部布局不兼容、释放函数不同（unref vs free）：headers 最终由
    // WebKit 侧按它链接的 soup 释放，我们用错版本构造 → WebKit 按错结构体迭代/释放 → 崩溃。故初始化时扫
    // /proc/self/maps 探测（libwebkit2gtk 加载后其 DT_NEEDED 依赖含 libsoup 已映射进进程），用同版 API
    // 构造。两个 LibraryImport 都惰性加载，只调用被选中的那个。

    // SoupMessageHeadersType 枚举：SOUP_MESSAGE_HEADERS_REQUEST = 0 / SOUP_MESSAGE_HEADERS_RESPONSE = 1
    private const int SoupMessageHeadersResponse = 1;

    private enum SoupVersion { Soup3, Soup2 }
    private static SoupVersion _soupVersion = SoupVersion.Soup3;

    // soup3 是引用计数 boxed 类型（soup2 是 soup_message_headers_free，勿混用）
    [LibraryImport(SoupLib3, EntryPoint = "soup_message_headers_new")]
    private static partial IntPtr soup3_message_headers_new(int type);

    [LibraryImport(SoupLib3, EntryPoint = "soup_message_headers_append")]
    private static partial void soup3_message_headers_append(
        IntPtr headers,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string value);

    [LibraryImport(SoupLib3, EntryPoint = "soup_message_headers_unref")]
    private static partial void soup3_message_headers_unref(IntPtr headers);

    [LibraryImport(SoupLib2, EntryPoint = "soup_message_headers_new")]
    private static partial IntPtr soup2_message_headers_new(int type);

    [LibraryImport(SoupLib2, EntryPoint = "soup_message_headers_append")]
    private static partial void soup2_message_headers_append(
        IntPtr headers,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string value);

    [LibraryImport(SoupLib2, EntryPoint = "soup_message_headers_free")]
    private static partial void soup2_message_headers_free(IntPtr headers);

    // ==================== JavaScriptCore 4.1 ====================

    [LibraryImport(JavaScriptCoreLib, EntryPoint = "jsc_value_to_json")]
    private static partial IntPtr jsc_value_to_json(IntPtr value, uint indentation);

    [LibraryImport(JavaScriptCoreLib, EntryPoint = "jsc_value_to_string")]
    private static partial IntPtr jsc_value_to_string(IntPtr value);

    // ==================== 回调委托（须保活，见下方静态字段） ====================

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void WebKitUriSchemeRequestCallback(IntPtr request, IntPtr userData);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void GAsyncReadyCallback(IntPtr sourceObject, IntPtr result, IntPtr userData);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void SignalLoadChangedCallback(IntPtr view, int loadEvent, IntPtr userData);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void SignalScriptMessageReceivedCallback(IntPtr manager, IntPtr jsResult, IntPtr userData);

    // 保活：委托实例只有被静态字段强引用才不会被 GC 回收（原生侧只持有函数指针）。
    private static readonly GAsyncReadyCallback _asyncTrampoline = OnEvaluateJavascriptReady;

    // 存活 WebView 集合：evaluate_javascript 的异步完成回调触发时，WebView 可能已被销毁
    // （示例窗口的定时器在窗口关闭后仍推模型），此时不能再调 *_finish（对已释放对象 → UAF）。
    // CreateWebView/ReleaseWebView/OnEvaluateJavascriptReady 实际都在主循环线程，加锁仅为防御。
    private static readonly HashSet<IntPtr> _liveViews = [];

    // ==================== 公开 API ====================

    /// <summary>
    /// 初始化 WebKit（触发类型/子系统注册，等价于旧 WebKit.Module.Initialize()）。
    /// </summary>
    public static void Initialize()
    {
        webkit_web_context_get_default();
        DetectSoupVersion(); // 探测 WebKitGTK 链接的 libsoup，之后构造响应头按同版 API
    }

    /// <summary>
    /// 创建 WebKitWebView（GtkWidget*），并给 .NET 侧持有一个引用（窗口销毁时 <see cref="ReleaseWebView"/> 释放）。
    /// </summary>
    public static IntPtr CreateWebView()
    {
        var view = webkit_web_view_new();
        g_object_ref(view);
        lock (_liveViews)
            _liveViews.Add(view);
        return view;
    }

    /// <summary>
    /// 释放 .NET 侧持有的 webview 引用（先移出存活集合，之后异步回调不再触碰它）。
    /// </summary>
    public static void ReleaseWebView(IntPtr view)
    {
        lock (_liveViews)
            _liveViews.Remove(view);
        g_object_unref(view);
    }

    public static void LoadUri(IntPtr view, string uri) => webkit_web_view_load_uri(view, uri);

    /// <summary>
    /// 取 WebView 的 UserContentManager（用于注册 script message handler）。
    /// </summary>
    public static IntPtr GetUserContentManager(IntPtr view) => webkit_web_view_get_user_content_manager(view);

    /// <summary>
    /// 注册 script message handler「wwui」，JS 侧 window.webkit.messageHandlers.wwui.postMessage(...)。
    /// </summary>
    public static void RegisterScriptMessageHandler(IntPtr view, string name)
    {
        var ok = webkit_user_content_manager_register_script_message_handler(GetUserContentManager(view), name);
        WebWindowLog.Debug($"register_script_message_handler({name}) = {ok}"); // 0 表示注册失败（Debug 日志）
    }

    /// <summary>
    /// 在共享默认 WebContext 上注册自定义 scheme，并把 scheme 注册为 CORS-enabled（镜像 Windows 的
    /// AllowedOrigins="*"；不注册则 appdata 跨源 fetch 被 WebKit 的 CORS 门控拦截）与 secure
    /// （镜像 Windows 的 TreatAsSecure=true，页面按 https 安全上下文求值）。回调委托必须保活（由调用方静态持有）。
    /// </summary>
    public static void RegisterUriScheme(string scheme, WebKitUriSchemeRequestCallback callback)
    {
        var context = webkit_web_context_get_default();
        webkit_web_context_register_uri_scheme(context, scheme,
            Marshal.GetFunctionPointerForDelegate(callback), IntPtr.Zero, IntPtr.Zero);
        var securityManager = webkit_web_context_get_security_manager(context);
        webkit_security_manager_register_uri_scheme_as_cors_enabled(securityManager, scheme);
        webkit_security_manager_register_uri_scheme_as_secure(securityManager, scheme);
    }

    /// <summary>在页面里执行 JS，返回 JSC 值的 JSON 表示（与 WebView2 ExecuteScriptAsync 对齐；非 JSON 值退回字符串）。
    /// 每次调用分配独立 GCHandle 路由回各自的 TaskCompletionSource，可并发多次求值。
    /// 完成回调在主循环线程触发，tcs 以 RunContinuationsAsynchronously 避免回调线程内联执行续体。</summary>
    public static Task<string> EvaluateJavascriptAsync(IntPtr view, string script)
    {
        var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var gch = GCHandle.Alloc(tcs);
        webkit_web_view_evaluate_javascript(view, script, -1, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero,
            Marshal.GetFunctionPointerForDelegate(_asyncTrampoline), GCHandle.ToIntPtr(gch));
        return tcs.Task;
    }

    /// <summary>
    /// 读取 scheme 请求的 URI（借用字符串，不释放）。
    /// </summary>
    public static string GetSchemeRequestUri(IntPtr request)
    {
        var p = webkit_uri_scheme_request_get_uri(request);
        return p == IntPtr.Zero ? "" : Marshal.PtrToStringUTF8(p) ?? "";
    }

    /// <summary>
    /// scheme 请求发起的 webview 指针（用于回查窗口）。
    /// </summary>
    public static IntPtr GetSchemeRequestWebView(IntPtr request) => webkit_uri_scheme_request_get_web_view(request);

    /// <summary>
    /// 以字节数据完成 scheme 请求，并回 Access-Control-Allow-Origin: *（跨源 appdata fetch 必需，
    /// 镜像 Windows 的 ResourceHeaders）与 Cache-Control（有 hash 资产长缓存 / 其余 no-store）。
    /// 走 WebKitGTK ≥ 2.36 的 WebKitURISchemeResponse（旧 finish 只能带 content-type，无法设响应头）。
    /// 所有权：response_new ref stream、set_http_headers (transfer full) 接管 headers（不得再释放）、
    /// finish_with_response ref response——本方法只 unref stream 与 response，headers 交出去即不碰。
    /// </summary>
    public static void FinishSchemeRequest(IntPtr request, byte[] data, string contentType, string? cacheControl)
    {
        var bytes = g_bytes_new(data, (nuint)data.Length);
        var stream = IntPtr.Zero;
        var response = IntPtr.Zero;
        var headers = IntPtr.Zero;
        try
        {
            stream = g_memory_input_stream_new_from_bytes(bytes); // stream 持有 bytes 引用
            g_bytes_unref(bytes);                                  // 释放我们自己的引用
            bytes = IntPtr.Zero;

            response = webkit_uri_scheme_response_new(stream, data.LongLength); // response 持有 stream 引用
            g_object_unref(stream);
            stream = IntPtr.Zero;

            webkit_uri_scheme_response_set_content_type(response, contentType);
            headers = BuildResponseHeaders(cacheControl);
            // (transfer full)：set_http_headers 以 GUniquePtr 接管 headers 所有权（不 ref），
            // 之后由 WebKit 按它链接的 libsoup 释放——调用方绝不能再 unref/free（否则 double-free UAF）。
            webkit_uri_scheme_response_set_http_headers(response, headers);
            headers = IntPtr.Zero; // 所有权已转移，finally 不再释放

            webkit_uri_scheme_request_finish_with_response(request, response); // GRefPtr 赋值 ref response
            g_object_unref(response);
            response = IntPtr.Zero;
        }
        finally
        {
            if (bytes != IntPtr.Zero)
                g_bytes_unref(bytes);
            if (stream != IntPtr.Zero)
                g_object_unref(stream);
            if (headers != IntPtr.Zero)
                FreeSoupHeaders(headers);
            if (response != IntPtr.Zero)
                g_object_unref(response);
        }
    }

    /// <summary>
    /// 构造响应头容器：Access-Control-Allow-Origin: *（跨源数据通道契约，全平台一致）+ 可选 Cache-Control。
    /// 按探测到的 libsoup 版本走同版 API（见 libsoup 节的 soup2/soup3 错配坑）。
    /// </summary>
    private static IntPtr BuildResponseHeaders(string? cacheControl)
    {
        var headers = _soupVersion == SoupVersion.Soup2
            ? soup2_message_headers_new(SoupMessageHeadersResponse)
            : soup3_message_headers_new(SoupMessageHeadersResponse);
        AppendHeader(headers, "Access-Control-Allow-Origin", "*");
        if (!string.IsNullOrEmpty(cacheControl))
            AppendHeader(headers, "Cache-Control", cacheControl);
        return headers;
    }

    private static void AppendHeader(IntPtr headers, string name, string value)
    {
        if (_soupVersion == SoupVersion.Soup2)
            soup2_message_headers_append(headers, name, value);
        else
            soup3_message_headers_append(headers, name, value);
    }

    private static void FreeSoupHeaders(IntPtr headers)
    {
        if (_soupVersion == SoupVersion.Soup2)
            soup2_message_headers_free(headers);
        else
            soup3_message_headers_unref(headers);
    }

    /// <summary>
    /// 把 script-message-received 信号里的 WebKitJavascriptResult 转成消息字符串。
    /// </summary>
    public static string JavascriptResultToString(IntPtr jsResult)
    {
        var jscValue = webkit_javascript_result_get_js_value(jsResult);
        return JscValueToString(jscValue);
    }

    // ==================== 私有实现 ====================

    private static string JscValueToJson(IntPtr jscValue)
    {
        if (jscValue == IntPtr.Zero)
            return "null";
        var p = jsc_value_to_json(jscValue, 0);
        if (p != IntPtr.Zero)
        {
            try { return Marshal.PtrToStringUTF8(p) ?? "null"; }
            finally { g_free(p); }
        }
        return JscValueToString(jscValue);
    }

    private static string JscValueToString(IntPtr jscValue)
    {
        if (jscValue == IntPtr.Zero)
            return "";
        var p = jsc_value_to_string(jscValue);
        try { return Marshal.PtrToStringUTF8(p) ?? ""; }
        finally { g_free(p); }
    }

    /// <summary>
    /// evaluate_javascript 异步完成回调（主循环线程）。userData 是本次调用的 GCHandle。
    /// </summary>
    private static void OnEvaluateJavascriptReady(IntPtr sourceObject, IntPtr result, IntPtr userData)
    {
        var gch = GCHandle.FromIntPtr(userData);
        var tcs = (TaskCompletionSource<string>)gch.Target!;
        gch.Free();

        // 求值期间窗口可能已被销毁 → WebView 已释放，finish 会对已释放对象解引用（UAF）。跳过并报错。
        if (sourceObject == IntPtr.Zero)
            return;
        lock (_liveViews)
        {
            if (!_liveViews.Contains(sourceObject))
            {
                WebWindowLog.Debug("evaluate_javascript 跳过：窗口已关闭，WebView 已销毁");
                tcs.TrySetException(new InvalidOperationException("窗口已关闭，WebView 已销毁。"));
                return;
            }
        }

        var jscValue = webkit_web_view_evaluate_javascript_finish(sourceObject, result, out var error);
        if (error != IntPtr.Zero)
        {
            var message = ReadAndFreeGError(error);
            WebWindowLog.Debug($"evaluate_javascript error: {message}");
            tcs.TrySetException(new InvalidOperationException(message));
            return;
        }
        tcs.TrySetResult(JscValueToJson(jscValue));
    }

    private static string ReadAndFreeGError(IntPtr error)
    {
        // GError 是公开结构体（GQuark domain; gint code; gchar* message;），GLib 无 g_error_get_message() API，
        // message 字段指针直接按偏移读（domain 4B + code 4B → 32/64 位下指针都在偏移 8）。g_error_free 归 libglib。
        var msg = Marshal.ReadIntPtr(error, 8);
        var text = msg == IntPtr.Zero ? "WebKit 错误" : Marshal.PtrToStringUTF8(msg) ?? "WebKit 错误";
        g_error_free(error);
        return text;
    }

    /// <summary>
    /// 探测 WebKitGTK 实际链接的 libsoup 版本。libwebkit2gtk 一经加载，其 DT_NEEDED 依赖（含 libsoup）
    /// 即映射进进程，扫 /proc/self/maps 即可（必须在 <see cref="Initialize"/> 首次 WebKit 调用之后）。
    /// 两者同时在 maps（罕见，默认 webkit 走 soup3）时按 soup3，与旧行为一致。
    /// </summary>
    private static void DetectSoupVersion()
    {
        try
        {
            var text = File.ReadAllText("/proc/self/maps");
            var hasSoup2 = text.Contains("libsoup-2.4", StringComparison.Ordinal);
            var hasSoup3 = text.Contains("libsoup-3.0", StringComparison.Ordinal);
            _soupVersion = hasSoup2 && !hasSoup3 ? SoupVersion.Soup2 : SoupVersion.Soup3;
        }
        catch
        {
            _soupVersion = SoupVersion.Soup3; // /proc 不可读等极端情况，按默认
        }
    }
}
