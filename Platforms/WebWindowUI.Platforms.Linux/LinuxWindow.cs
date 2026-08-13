using WebWindowUI.Core;
using WebWindowUI.Core.Protocol;
using WebWindowUI.Natives.Linux;

namespace WebWindowUI.Platforms.Linux;

/// <summary>
/// Linux 平台：GTK3 窗口 + libwebkit2gtk-4.1（GTK3 端口）WebView，可创建多个实例。WebKit 与 GTK3
/// 均为手写 P/Invoke（GirCore 无 GTK3/WebKit2-4.1 绑定）；所有 WebView 共享默认 WebContext，
/// 自定义 scheme 每进程注册一次，请求回调按发起 WebView 指针经平台窗口表分派回对应窗口。
/// </summary>
public sealed class LinuxWindow : IWindowBackend
{
    internal const string BridgeHandlerName = "wwui"; // 与前端桥 webwindowui-bridge 的 HANDLER_NAME 一致

    private readonly LinuxNativeWindow _window;
    private readonly IntPtr _webView;
    private readonly WebWindowOptions _options;
    private WebKit2SignalBridge? _signals;
    private bool _closed;

    /// <summary>
    /// 承载 WebView 的原生指针，作为窗口表（LinuxPlatform._windows）的键，镜像 Windows 的 Hwnd。
    /// </summary>
    internal IntPtr WebView => _webView;

    /// <summary>
    /// 窗口销毁时触发（用户关闭或 Close()）。宿主在此清理与窗口关联的状态。
    /// </summary>
    public event Action? Closed;

    /// <summary>
    /// 构造并登记窗口（注册进平台窗口表）。
    /// </summary>
    /// <param name="window">GTK 窗口壳。</param>
    /// <param name="webView">WebKitWebView 指针。</param>
    /// <param name="options">窗口选项。</param>
    private LinuxWindow(LinuxNativeWindow window, IntPtr webView, WebWindowOptions options)
    {
        _window = window;
        _webView = webView;
        _options = options;
        LinuxPlatform.WindowOpen(this);
    }

    /// <summary>
    /// 创建并注册一个尚未显示的窗口。
    /// </summary>
    public static LinuxWindow Create(WebWindowOptions options)
    {
        var w = new LinuxNativeWindow(options);

        var v = WebKit2Native.CreateWebView(); // webkit_web_view_new + 持有引用
        w.SetChild(v);                            // gtk_container_add，窗口接管一个引用

        var window = new LinuxWindow(w, v, options)
        {
            _signals = new WebKit2SignalBridge(v)
        };
        window._signals.Connect();
        window._signals.ScriptMessageReceived += window.OnScriptMessageReceived;
        window._signals.LoadChanged += window.OnLoadChanged;
        WebKit2Native.RegisterScriptMessageHandler(v, BridgeHandlerName);

        // 窗口销毁（用户关标题栏或 Close() 的 gtk_window_close → 默认处理器 destroy）→ 通知框架关闭
        w.Destory += window.OnDestroyed;

        WebWindowLog.Debug($"create window '{options.Title}' (view={v})");
        return window;
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
            WebKit2Native.LoadUri(_webView, WebWindowResource.GetWindowIndexUrl(_options.WindowPath));
        });
    }

    /// <summary>
    /// 隐藏窗口（不关闭、不销毁）。
    /// </summary>
    public void Hide() => RunOnUiThread(() => _window.Hide());

    /// <summary>
    /// 关闭窗口。gtk_window_close → 默认 close-request 处理器 destroy → OnDestroyed。
    /// </summary>
    public void Close()
    {
        RunOnUiThread(() =>
        {
            if (_closed)
                return;
            _window.Close();
        });
    }

    /// <summary>
    /// 把窗口带到前台并聚焦。
    /// </summary>
    public void Activate() => RunOnUiThread(() => _window.Activate());

    /// <summary>
    /// 修改窗口标题（立即同步到标题栏）。
    /// </summary>
    public void SetTitle(string title) => RunOnUiThread(() => _window.SetTitle(title));

    /// <summary>
    /// 设置窗口图标。GTK3 虽有 gtk_window_set_icon 但 CSD/Wayland 不显示 per-window 图标，无操作。
    /// </summary>
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
            var js = "window.wwuiReceive(" + JsStringLiteral.Quote(WebView2StringCodec.Encode(message)) + ")";
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

    /// <summary>
    /// 页面导航完成时触发（用于在页面就绪后推送 Model 初始快照）。
    /// </summary>
    public event Action? NavigationCompleted;

    /// <summary>
    /// 页面 JS 通过 script message handler 回传的消息（protobuf 字节，由 NUL 转义串还原）。
    /// </summary>
    public event Action<byte[]>? MessageReceived;

    /// <summary>
    /// script message 回调：解码 NUL 转义 Latin-1 字符串为 protobuf 字节并触发 MessageReceived。
    /// </summary>
    /// <param name="message">桥回传的 NUL 转义字符串。</param>
    private void OnScriptMessageReceived(string message)
    {
        if (message.Length == 0)
        {
            WebWindowLog.Debug("空 script message 收到");
            return;
        }
        MessageReceived?.Invoke(WebView2StringCodec.Decode(message));
    }

    /// <summary>
    /// 加载进度回调：主 frame 加载完成触发 NavigationCompleted。
    /// </summary>
    /// <param name="loadEvent">加载事件枚举值。</param>
    private void OnLoadChanged(int loadEvent)
    {
        WebWindowLog.Debug($"load-changed: {loadEvent}");
        if (loadEvent == (int)WebKit2Native.LoadEvent.Finished)
            NavigationCompleted?.Invoke();
    }

    /// <summary>
    /// 窗口销毁：断开信号、释放 WebView 引用并注销窗口表。
    /// </summary>
    private void OnDestroyed()
    {
        if (_closed)
            return;
        _closed = true;
        WebWindowLog.Debug($"window closed (view={_webView})");
        Closed?.Invoke();
        // 断开信号、释放 .NET 侧持有的 webview 引用（窗口仍持有其子级引用，至此引用计数归零 → 销毁）
        _signals?.Dispose();
        _signals = null;
        WebKit2Native.ReleaseWebView(_webView);
        _window.Dispose(); // 断开 destroy 信号并释放路由 GCHandle
        LinuxPlatform.WindowClose(this); // 注销窗口表 + 通知框架关闭 + 最后窗口退出主循环
    }

    /// <summary>
    /// 把动作 marshal 到主线程同步执行：主线程直接运行；非主线程经
    /// <see cref="LinuxMessageLoopSynchronizationContext.Send"/>（回主线程并阻塞等待）。
    /// </summary>
    private static void RunOnUiThread(Action action)
    {
        if (Environment.CurrentManagedThreadId == LinuxMessageLoopSynchronizationContext.UiThreadId)
        {
            action();
            return;
        }
        LinuxMessageLoopSynchronizationContext.Instance.Send(_ => action(), null);
    }
}
