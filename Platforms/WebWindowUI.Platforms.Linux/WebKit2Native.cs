#pragma warning disable CA1416 // 原生 WebKit 类型带 [SupportedOSPlatform]
using WebWindowUI.Core;

namespace WebWindowUI.Linux;

/// <summary>
/// 手写 P/Invoke 绑定：libwebkit2gtk-4.1（GIR 命名空间 WebKit2-4.1，GTK3 端口）+ libjavascriptcoregtk-4.1。
/// GirCore 只发布 WebKitGTK 6.0（GTK4）的绑定，4.1（GTK3）无托管绑定可换，故按后端实际用到的
/// API 子集手写。本类保持 GTK 无关（只含 WebKit/JavaScriptCore/GObject/GLib/Gio）；GTK 窗口层见
/// GtkNative / GtkWindowHost。原生符号经 soname（lib*.so.0）引用，运行时不依赖 dev 符号链接。
///
/// 所有权约定（来自 GIR / WebKitGTK 文档）：
///  - <see cref="webkit_uri_scheme_request_get_uri"/> 返回借用字符串，不要释放；
///  - <see cref="webkit_uri_scheme_request_finish"/> 接管 stream 所有权（WebKit 最终 unref）；
///  - GMemoryInputStream 持有其 GBytes 的引用，故 g_bytes_new 后由 stream 接管、我们释放自己的引用；
///  - jsc_value_to_json / jsc_value_to_string 返回新分配字符串，须 g_free；
///  - GError 无 g_error_get_message() API，message 按公开结构体字段偏移直接读（见 <see cref="ReadAndFreeGError"/>）；
///  - EvaluateJavascriptAsync 的 GAsyncReadyCallback 在主循环线程触发（即框架主线程）。
/// </summary>
internal static class WebKit2Native
{
    // ---- 原生库 soname ----
    private const string WebKitLib = "libwebkit2gtk-4.1.so.0";
    private const string JavaScriptCoreLib = "libjavascriptcoregtk-4.1.so.0";
    private const string GObjectLib = "libgobject-2.0.so.0";
    private const string GLibLib = "libglib-2.0.so.0";
    private const string GioLib = "libgio-2.0.so.0";

    /// <summary>WebKitLoadEvent 枚举（WEBKIT_LOAD_*）。</summary>
    public enum LoadEvent
    {
        Started = 0,
        Redirected = 1,
        Committed = 2,
        Finished = 3,
    }

    // ==================== GObject ====================

    [DllImport(GObjectLib, EntryPoint = "g_object_ref")]
    public static extern IntPtr g_object_ref(IntPtr obj);

    [DllImport(GObjectLib, EntryPoint = "g_object_unref")]
    public static extern void g_object_unref(IntPtr obj);

    [DllImport(GObjectLib, EntryPoint = "g_signal_connect_data")]
    private static extern ulong g_signal_connect_data(
        IntPtr instance,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string detailedSignal,
        IntPtr handler,
        IntPtr data,
        IntPtr destroyData,
        uint connectFlags);

    [DllImport(GObjectLib, EntryPoint = "g_signal_handler_disconnect")]
    private static extern void g_signal_handler_disconnect(IntPtr instance, ulong handlerId);

    [DllImport(GLibLib, EntryPoint = "g_error_free")]
    private static extern void g_error_free(IntPtr error);

    // ==================== GLib / Gio ====================

    [DllImport(GLibLib, EntryPoint = "g_free")]
    private static extern void g_free(IntPtr mem);

    [DllImport(GLibLib, EntryPoint = "g_bytes_new")]
    private static extern IntPtr g_bytes_new(byte[] data, nuint len);

    [DllImport(GLibLib, EntryPoint = "g_bytes_unref")]
    private static extern void g_bytes_unref(IntPtr bytes);

    [DllImport(GioLib, EntryPoint = "g_memory_input_stream_new_from_bytes")]
    private static extern IntPtr g_memory_input_stream_new_from_bytes(IntPtr bytes);

    // ==================== WebKit2 4.1 ====================

    [DllImport(WebKitLib, EntryPoint = "webkit_web_view_new")]
    private static extern IntPtr webkit_web_view_new();

    [DllImport(WebKitLib, EntryPoint = "webkit_web_view_load_uri")]
    private static extern void webkit_web_view_load_uri(IntPtr view, [MarshalAs(UnmanagedType.LPUTF8Str)] string uri);

    [DllImport(WebKitLib, EntryPoint = "webkit_web_view_get_user_content_manager")]
    private static extern IntPtr webkit_web_view_get_user_content_manager(IntPtr view);

    [DllImport(WebKitLib, EntryPoint = "webkit_web_view_evaluate_javascript")]
    private static extern void webkit_web_view_evaluate_javascript(
        IntPtr view,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string script,
        nint length,
        IntPtr worldName,
        IntPtr sourceUri,
        IntPtr cancellable,
        IntPtr callback,
        IntPtr userData);

    [DllImport(WebKitLib, EntryPoint = "webkit_web_view_evaluate_javascript_finish")]
    private static extern IntPtr webkit_web_view_evaluate_javascript_finish(IntPtr view, IntPtr result, out IntPtr error);

    [DllImport(WebKitLib, EntryPoint = "webkit_web_context_get_default")]
    private static extern IntPtr webkit_web_context_get_default();

    [DllImport(WebKitLib, EntryPoint = "webkit_web_context_register_uri_scheme")]
    private static extern void webkit_web_context_register_uri_scheme(
        IntPtr context,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string scheme,
        WebKitUriSchemeRequestCallback callback,
        IntPtr userData,
        IntPtr destroyNotify);

    [DllImport(WebKitLib, EntryPoint = "webkit_user_content_manager_register_script_message_handler")]
    private static extern int webkit_user_content_manager_register_script_message_handler(
        IntPtr manager,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name);

    [DllImport(WebKitLib, EntryPoint = "webkit_uri_scheme_request_get_uri")]
    private static extern IntPtr webkit_uri_scheme_request_get_uri(IntPtr request);

    [DllImport(WebKitLib, EntryPoint = "webkit_uri_scheme_request_get_web_view")]
    private static extern IntPtr webkit_uri_scheme_request_get_web_view(IntPtr request);

    [DllImport(WebKitLib, EntryPoint = "webkit_uri_scheme_request_finish")]
    private static extern void webkit_uri_scheme_request_finish(
        IntPtr request,
        IntPtr stream,
        long streamLength,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string contentType);

    [DllImport(WebKitLib, EntryPoint = "webkit_javascript_result_get_js_value")]
    private static extern IntPtr webkit_javascript_result_get_js_value(IntPtr jsResult);

    // ==================== JavaScriptCore 4.1 ====================

    [DllImport(JavaScriptCoreLib, EntryPoint = "jsc_value_to_json")]
    private static extern IntPtr jsc_value_to_json(IntPtr value, uint indentation);

    [DllImport(JavaScriptCoreLib, EntryPoint = "jsc_value_to_string")]
    private static extern IntPtr jsc_value_to_string(IntPtr value);

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

    /// <summary>初始化 WebKit（触发类型/子系统注册，等价于旧 WebKit.Module.Initialize()）。</summary>
    public static void Initialize()
    {
        webkit_web_context_get_default();
    }

    /// <summary>创建 WebKitWebView（GtkWidget*），并给 .NET 侧持有一个引用（窗口销毁时 <see cref="ReleaseWebView"/> 释放）。</summary>
    public static IntPtr CreateWebView()
    {
        var view = webkit_web_view_new();
        g_object_ref(view);
        lock (_liveViews)
            _liveViews.Add(view);
        return view;
    }

    /// <summary>释放 .NET 侧持有的 webview 引用（先移出存活集合，之后异步回调不再触碰它）。</summary>
    public static void ReleaseWebView(IntPtr view)
    {
        lock (_liveViews)
            _liveViews.Remove(view);
        g_object_unref(view);
    }

    public static void LoadUri(IntPtr view, string uri) => webkit_web_view_load_uri(view, uri);

    /// <summary>取 WebView 的 UserContentManager（用于注册 script message handler）。</summary>
    public static IntPtr GetUserContentManager(IntPtr view) => webkit_web_view_get_user_content_manager(view);

    /// <summary>注册 script message handler「wwui」，JS 侧 window.webkit.messageHandlers.wwui.postMessage(...)。</summary>
    public static void RegisterScriptMessageHandler(IntPtr view, string name)
    {
        var ok = webkit_user_content_manager_register_script_message_handler(GetUserContentManager(view), name);
        Log.Debug($"register_script_message_handler({name}) = {ok}"); // 0 表示注册失败（Debug 日志）
    }

    /// <summary>在共享默认 WebContext 上注册自定义 scheme。回调委托必须保活（由调用方静态持有）。</summary>
    public static void RegisterUriScheme(string scheme, WebKitUriSchemeRequestCallback callback)
        => webkit_web_context_register_uri_scheme(webkit_web_context_get_default(), scheme, callback, IntPtr.Zero, IntPtr.Zero);

    /// <summary>连接 WebKit 信号到托管回调。data 是调用方预先分配的 GCHandle（由调用方释放）；
    /// handler 委托必须被强引用保活。detail 支持 "signal::detail"。</summary>
    public static ulong ConnectSignal(IntPtr instance, string detailedSignal, Delegate handler, GCHandle data)
        => g_signal_connect_data(instance, detailedSignal,
            Marshal.GetFunctionPointerForDelegate(handler), GCHandle.ToIntPtr(data), IntPtr.Zero, 0);

    /// <summary>断开信号。实例已销毁时忽略错误。</summary>
    public static void DisconnectSignal(IntPtr instance, ulong handlerId)
    {
        if (handlerId != 0 && instance != IntPtr.Zero)
        {
            try { g_signal_handler_disconnect(instance, handlerId); }
            catch { /* 实例已销毁 */ }
        }
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

    /// <summary>读取 scheme 请求的 URI（借用字符串，不释放）。</summary>
    public static string GetSchemeRequestUri(IntPtr request)
    {
        var p = webkit_uri_scheme_request_get_uri(request);
        return p == IntPtr.Zero ? "" : Marshal.PtrToStringUTF8(p) ?? "";
    }

    /// <summary>scheme 请求发起的 webview 指针（用于回查窗口）。</summary>
    public static IntPtr GetSchemeRequestWebView(IntPtr request) => webkit_uri_scheme_request_get_web_view(request);

    /// <summary>以字节数据完成 scheme 请求。WebKit 接管 stream 所有权，本方法内部不保留任何引用。</summary>
    public static void FinishSchemeRequest(IntPtr request, byte[] data, string contentType)
    {
        var bytes = g_bytes_new(data, (nuint)data.Length);
        var stream = IntPtr.Zero;
        try
        {
            stream = g_memory_input_stream_new_from_bytes(bytes); // stream 持有 bytes 引用
            g_bytes_unref(bytes);                                  // 释放我们自己的引用
            bytes = IntPtr.Zero;
            webkit_uri_scheme_request_finish(request, stream, data.LongLength, contentType);
            stream = IntPtr.Zero; // WebKit 已接管
        }
        finally
        {
            if (bytes != IntPtr.Zero)
                g_bytes_unref(bytes);
            if (stream != IntPtr.Zero)
                g_object_unref(stream);
        }
    }

    /// <summary>把 script-message-received 信号里的 WebKitJavascriptResult 转成消息字符串。</summary>
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

    /// <summary>evaluate_javascript 异步完成回调（主循环线程）。userData 是本次调用的 GCHandle。</summary>
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
                Log.Debug("evaluate_javascript 跳过：窗口已关闭，WebView 已销毁");
                tcs.TrySetException(new InvalidOperationException("窗口已关闭，WebView 已销毁。"));
                return;
            }
        }

        var jscValue = webkit_web_view_evaluate_javascript_finish(sourceObject, result, out var error);
        if (error != IntPtr.Zero)
        {
            var message = ReadAndFreeGError(error);
            Log.Debug($"evaluate_javascript error: {message}");
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
}
