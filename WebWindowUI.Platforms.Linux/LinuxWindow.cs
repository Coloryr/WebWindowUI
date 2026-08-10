#if LINUX
#pragma warning disable CA1416 // WebKit 类型带 [SupportedOSPlatform]，但本文件 #if LINUX 门控，Windows/macOS 构建不含
using System.Text;

namespace WebWindowUI.Linux;

/// <summary>
/// Linux 平台：GTK3 窗口 + libwebkit2gtk-4.1（WebKit2-4.1 GIR 命名空间，GTK3 端口）的 WebView，可创建多个实例。
/// WebKit 绑定是手写 P/Invoke（见 Native/WebKit2Native.cs），因 GirCore 只发布 WebKitGTK 6.0（GTK4）的绑定；
/// GTK3 窗口壳也是手写（见 Native/GtkNative.cs + Native/GtkWindowHost.cs），因 GirCore 无 GTK3 绑定。
/// 所有 WebView 共享默认 WebContext（webkit_web_context_get_default）——自定义 scheme 每进程注册一次，
/// 请求回调按发起 WebView 指针经 <see cref="_windows"/> 分派回对应窗口。
///
/// 平台限制（与 Windows 有差异，README 也注明）：
///  - SetIcon 无操作：CSD/Wayland 不用 per-window 图标，只有主题图标（GTK3 虽有 gtk_window_set_icon 但不实现）。
///  - 自定义 scheme 响应不设 Cache-Control：WebKitGTK 的 finish 只带 content-type（v1）。
///  - ExecuteScriptAsync 返回 JSC 值的 JSON 表示，与 WebView2 的 JSON 序列化对齐是 best-effort。
/// </summary>
public sealed class LinuxWindow : IWindowBackend
{
    internal const string BridgeHandlerName = "wwui"; // 与前端桥 webwindowui-bridge 的 HANDLER_NAME 一致

    // ---- 进程级共享状态 ----
    private static readonly Dictionary<IntPtr, LinuxWindow> _windows = [];
    private static readonly HashSet<string> _registeredSchemes = [];
    private static readonly object _schemeLock = new();
    // 保活：webkit_web_context_register_uri_scheme 的 C 回调只在调用期被 marshaller 生根，
    // 注册本身是进程级的，委托实例必须由静态字段强引用存续。
    private static readonly WebKit2Native.WebKitUriSchemeRequestCallback _schemeCallback = OnUriSchemeRequest;

    private readonly GtkWindowHost _window;
    private readonly IntPtr _webView;
    private readonly WebWindowOptions _options;
    private WebKit2SignalBridge? _signals;
    private bool _closed;

    /// <summary>窗口销毁时触发（用户关闭或 Close()）。宿主在此清理与窗口关联的状态。</summary>
    public event Action? Closed;

    private LinuxWindow(GtkWindowHost window, IntPtr webView, WebWindowOptions options)
    {
        _window = window;
        _webView = webView;
        _options = options;
        _windows[webView] = this;
    }

    /// <summary>创建并注册一个尚未显示的窗口。</summary>
    public static LinuxWindow Create(string title, WebWindowOptions options, int width, int height)
    {
        var w = new GtkWindowHost(title, width, height);

        IntPtr v = WebKit2Native.CreateWebView(); // webkit_web_view_new + 持有引用
        w.SetChild(v);                            // gtk_container_add，窗口接管一个引用

        var window = new LinuxWindow(w, v, options);

        // 自定义 scheme 每进程注册一次（默认 WebContext 跨窗口共享，register_uri_scheme 是 context 级）
        RegisterSchemeOnce(options.Scheme);
        if (!string.IsNullOrEmpty(options.DataScheme) && options.DataScheme != options.Scheme)
            RegisterSchemeOnce(options.DataScheme!);

        // JS → native 通道：先连信号（含 script-message-received::wwui）再注册 handler，
        // 避免「消息已到但 handler 未就绪」的竞态（WebKit 文档建议的连接顺序）。
        window._signals = new WebKit2SignalBridge(v);
        window._signals.Connect();
        window._signals.ScriptMessageReceived += window.OnScriptMessageReceived;
        window._signals.LoadChanged += window.OnLoadChanged;
        WebKit2Native.RegisterScriptMessageHandler(v, BridgeHandlerName);

        // 窗口销毁（用户关标题栏或 Close() 的 gtk_window_close → 默认处理器 destroy）→ 通知框架关闭
        w.Destroyed += window.OnDestroyed;

        Log.Debug($"create window '{title}' (view={v})");
        return window;
    }

    private static void RegisterSchemeOnce(string scheme)
    {
        lock (_schemeLock)
        {
            if (_registeredSchemes.Add(scheme))
                WebKit2Native.RegisterUriScheme(scheme, _schemeCallback);
        }
    }

    /// <summary>
    /// 显示窗口并加载首页。GTK 的窗口 API 只允许在主线程访问，非主线程调用时 marshal 回主线程。
    /// 无头模式下跳过 Present()（窗口不出现在屏幕/任务栏），但照常加载首页。
    /// </summary>
    public void Show()
    {
        RunOnUiThread(() =>
        {
            if (!_options.Headless)
                _window.Show();
            WebKit2Native.LoadUri(_webView, _options.HomeUrl);
        });
    }

    /// <summary>隐藏窗口（不关闭、不销毁）。</summary>
    public void Hide() => RunOnUiThread(() => _window.Hide());

    /// <summary>关闭窗口。gtk_window_close → 默认 close-request 处理器 destroy → OnDestroyed。</summary>
    public void Close()
    {
        RunOnUiThread(() =>
        {
            if (_closed)
                return;
            _window.Close();
        });
    }

    /// <summary>把窗口带到前台并聚焦。</summary>
    public void Activate() => RunOnUiThread(() => _window.Activate());

    /// <summary>修改窗口标题（立即同步到标题栏）。</summary>
    public void SetTitle(string title) => RunOnUiThread(() => _window.SetTitle(title));

    /// <summary>设置窗口图标。GTK3 虽有 gtk_window_set_icon 但 CSD/Wayland 不显示 per-window 图标，无操作。</summary>
    public void SetIcon(WindowIcon icon)
    {
        // CSD/Wayland 只用主题图标（gtk_window_set_icon 实际不生效）。平台限制，文档注明。
    }

    /// <summary>
    /// 向页面 JS 发送一条消息。protobuf 字节经 <see cref="WebView2StringCodec"/> 转成不含 NUL 的
    /// Latin-1 字符串，再用 <see cref="JsStringLiteral.Quote"/> 嵌进 <c>window.wwuiReceive("...")</c>
    /// 经 evaluateJavascript 注入。JS 端 wwuiReceive 还原后 protobufjs 解码。
    /// 与 Windows 一致：WebKit 对象只能主线程访问，属性变更可能发生在任意线程，先投递回主线程。
    /// </summary>
    public void PostMessage(byte[] message)
    {
        try
        {
            if (_closed)
                return;
            if (Environment.CurrentManagedThreadId != LinuxMessageLoopSynchronizationContext.UiThreadId)
            {
                LinuxMessageLoopSynchronizationContext.Instance.Post(_ => PostMessage(message), null);
                return;
            }
            // 投递回主线程期间窗口可能已销毁（定时器在窗口关闭后仍会推模型），此时 webview 已被
            // ReleaseWebView 释放，再求值即 UAF → glib 断言 + stack smashing。与 Windows 的
            // _controller?.CoreWebView2 空条件等效的护栏：_closed 后直接跳过。
            if (_closed)
                return;
            string js = "window.wwuiReceive(" + JsStringLiteral.Quote(WebView2StringCodec.Encode(message)) + ")";
            _ = WebKit2Native.EvaluateJavascriptAsync(_webView, js)
                .ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
        }
        catch
        {
            // 窗口关闭后 WebView 已销毁，忽略（定时器仍在后台推送）
        }
    }

    /// <summary>
    /// 在页面里执行一段 JavaScript 并返回结果（与 WebView2 一样是 JSON 编码的字符串；best-effort）。
    /// 与 <see cref="PostMessage"/> 一样：WebKit 只能主线程访问，非主线程调用时先投递回主线程再执行，并等待结果。
    /// </summary>
    public async Task<string> ExecuteScriptAsync(string script)
    {
        if (Environment.CurrentManagedThreadId != LinuxMessageLoopSynchronizationContext.UiThreadId)
        {
            var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            LinuxMessageLoopSynchronizationContext.Instance.Post(async _ =>
            {
                try { tcs.TrySetResult(await ExecuteScriptAsync(script)); }
                catch (Exception ex) { tcs.TrySetException(ex); }
            }, null);
            return await tcs.Task;
        }

        // 与 Windows 的 InvalidOperationException("WebView2 尚未初始化完成。") 对齐：
        // 窗口已关闭时 WebView 已销毁，明确报错而不是对已释放对象求值。
        if (_closed)
            throw new InvalidOperationException("窗口已关闭。");
        return await WebKit2Native.EvaluateJavascriptAsync(_webView, script); // JSC 值 → JSON（best-effort 对齐 WebView2）
    }

    /// <summary>页面导航完成时触发（用于在页面就绪后推送 Model 初始快照）。</summary>
    public event Action? NavigationCompleted;

    /// <summary>页面 JS 通过 script message handler 回传的消息（protobuf 字节，由 NUL 转义串还原）。</summary>
    public event Action<byte[]>? MessageReceived;

    private void OnScriptMessageReceived(string message)
    {
        // message 是对本桥的 NUL 转义 Latin-1 字符串（trampoline 已用 jsc_value_to_string 还原）。
        if (message.Length == 0)
        {
            Log.Debug("空 script message 收到");
            return;
        }
        MessageReceived?.Invoke(WebView2StringCodec.Decode(message));
    }

    private void OnLoadChanged(int loadEvent)
    {
        Log.Debug($"load-changed: {loadEvent}");
        if (loadEvent == (int)WebKit2Native.LoadEvent.Finished)
            NavigationCompleted?.Invoke();
    }

    private void OnDestroyed(object? sender, EventArgs e)
    {
        if (_closed)
            return;
        _closed = true;
        Log.Debug($"window closed (view={_webView})");
        _windows.Remove(_webView);
        Closed?.Invoke();
        WebWindow.NotifyWindowClosed();
        // 断开信号、释放 .NET 侧持有的 webview 引用（窗口仍持有其子级引用，至此引用计数归零 → 销毁）
        _signals?.Dispose();
        _signals = null;
        WebKit2Native.ReleaseWebView(_webView);
        _window.Dispose(); // 断开 destroy 信号并释放路由 GCHandle
        if (WebWindow.OpenCount == 0)
            LinuxPlatform.QuitMainLoop(); // 最后一个窗口关闭，退出主循环
    }

    /// <summary>共享默认 WebContext 的 scheme 请求回调：按发起 WebView 指针分派回对应窗口。</summary>
    private static void OnUriSchemeRequest(IntPtr request, IntPtr userData)
    {
        if (!_windows.TryGetValue(WebKit2Native.GetSchemeRequestWebView(request), out LinuxWindow? window))
        {
            FinishNotFound(request);
            return;
        }
        window.HandleUriSchemeRequest(request);
    }

    private void HandleUriSchemeRequest(IntPtr request)
    {
        try
        {
            // 数据通道：请求来自 DataScheme 时交给 DataResolver，否则走 UI 资源（ResourceResolver）
            string uri = WebKit2Native.GetSchemeRequestUri(request);
            bool isData = WebResourceLocator.IsScheme(uri, _options.DataScheme);
            string scheme = isData ? _options.DataScheme! : _options.Scheme;
            Func<string, Stream?>? resolver = isData ? _options.DataResolver : _options.ResourceResolver;

            if (resolver is not null && WebResourceLocator.TryResolvePath(uri, scheme, out string? relative, out string? mimeType))
            {
                Stream? stream = resolver(relative!);
                if (stream is not null)
                {
                    // 嵌入式流不可 seek，读全量进 byte[] 再构造 MemoryInputStream（内部 g_bytes_new 拷一份字节）。
                    using (stream)
                    {
                        byte[] bytes;
                        using (var ms = new MemoryStream())
                        {
                            stream.CopyTo(ms);
                            bytes = ms.ToArray();
                        }
                        // WebKit 的 finish 接管 stream 所有权；FinishSchemeRequest 内部不保留引用。
                        WebKit2Native.FinishSchemeRequest(request, bytes, mimeType!);
                    }
                    return;
                }
            }
        }
        catch
        {
            // 读取或构造响应失败时回退 404
        }
        FinishNotFound(request);
    }

    private static void FinishNotFound(IntPtr request)
    {
        try
        {
            byte[] bytes = Encoding.UTF8.GetBytes("404 Not Found");
            WebKit2Native.FinishSchemeRequest(request, bytes, "text/plain; charset=utf-8");
        }
        catch
        {
            // 请求已被取消等，忽略
        }
    }

    /// <summary>
    /// 把动作 marshal 到主线程同步执行：主线程直接运行；非主线程经
    /// <see cref="LinuxMessageLoopSynchronizationContext.Send"/>（回主线程并阻塞等待）。
    /// </summary>
    private void RunOnUiThread(Action action)
    {
        if (Environment.CurrentManagedThreadId == LinuxMessageLoopSynchronizationContext.UiThreadId)
        {
            action();
            return;
        }
        LinuxMessageLoopSynchronizationContext.Instance.Send(_ => action(), null);
    }
}
#endif
