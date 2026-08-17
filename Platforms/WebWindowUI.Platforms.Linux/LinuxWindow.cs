using WebWindowUI.Core;
using WebWindowUI.Core.Platform;
using WebWindowUI.Core.Protocol;
using WebWindowUI.Natives.Linux;

namespace WebWindowUI.Platforms.Linux;

/// <summary>
/// Linux 平台：GTK3 窗口 + libwebkit2gtk-4.1（GTK3 端口）WebView，可创建多个实例。WebKit 与 GTK3
/// 均为手写 P/Invoke（GirCore 无 GTK3/WebKit2-4.1 绑定）；所有 WebView 共享默认 WebContext，
/// 自定义 scheme 每进程注册一次，请求回调按发起 WebView 指针经平台窗口表分派回对应窗口。
/// 窗口状态面经 <see cref="INativeWindow"/> 真实现；Model 双向绑定在基类契约内完成。
/// </summary>
public sealed class LinuxWindow : WebWindow
{
    internal const string BridgeHandlerName = "wwui"; // 与前端桥 webwindowui-bridge 的 HANDLER_NAME 一致

    private readonly LinuxNativeWindow _window;
    private readonly IntPtr _webView;
    private readonly Action<byte[]> _modelPushHandler;
    private string _title;
    private WebKit2SignalBridge? _signals;
    private WebWindowModel? _model;
    private bool _isLoaded;
    private bool _closed;

    /// <summary>
    /// 承载 WebView 的原生指针，作为窗口表（LinuxPlatform._windows）的键，镜像 Windows 的 Hwnd。
    /// </summary>
    internal IntPtr WebView => _webView;

    /// <summary>
    /// 原生窗口（平台窗口内部使用）。
    /// </summary>
    internal override INativeWindow NativeWindow
    {
        get => _window;
        set => throw new NotSupportedException("LinuxWindow 自建原生窗口，不支持替换。");
    }

    /// <summary>
    /// 构造并登记窗口（注册进平台窗口表）。
    /// </summary>
    /// <param name="window">GTK 窗口壳。</param>
    /// <param name="webView">WebKitWebView 指针。</param>
    /// <param name="options">窗口选项。</param>
    private LinuxWindow(LinuxNativeWindow window, IntPtr webView, WebWindowOptions options) : base(options)
    {
        _title = options.Title;
        _modelPushHandler = ModelPushed;
        _window = window;
        _webView = webView;
        LinuxPlatform.WindowOpen(this);
    }

    /// <summary>
    /// 创建并注册一个尚未显示的窗口。
    /// </summary>
    /// <param name="options">窗口选项。</param>
    /// <returns>平台窗口。</returns>
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
        w.Resize += () => window.RaiseResize(window._window.Size);
        w.Move += window.RaiseMove;
        w.Active += window.RaiseActive;
        w.WindowStateChange += window.RaiseWindowStateChange;
        w.SystemDecorationsChange += window.RaiseSystemDecorationsChange;

        WebWindowLog.Debug($"create window '{options.Title}' (view={v})");
        return window;
    }

    /// <summary>
    /// 窗口标题：get 返回跟踪字段；set 同步到标题栏（构造期基类先赋值、原生窗口尚未建，跳过原生调用）。
    /// </summary>
    public override string Title
    {
        get => _title;
        set
        {
            _title = value;
            if (_window is not null)
                RunOnUiThread(() => _window.SetTitle(value));
        }
    }

    /// <summary>
    /// 窗口数据模型：绑定时订阅模型推送（属性变化/集合变更→PostMessage），解绑时退订；
    /// 页面加载完成后补发完整快照。同一实例绑多窗口 = 共享广播。
    /// </summary>
    public override WebWindowModel? Model
    {
        get => _model;
        set
        {
            if (_model == value)
                return;
            _model?.UnsubscribePushed(_modelPushHandler);
            _model = value;
            _model?.SubscribePushed(_modelPushHandler);
            if (_model is not null && _isLoaded)
                PostMessage(_model.BuildSnapshotEnvelope());
        }
    }

    /// <summary>
    /// 窗口装饰样式。
    /// </summary>
    public override SystemDecorations SystemDecorations
    {
        get => _window.SystemDecorations;
        set => RunOnUiThread(() => _window.SystemDecorations = value);
    }

    /// <summary>
    /// 窗口状态。
    /// </summary>
    public override WindowState WindowState
    {
        get => _window.WindowState;
        set => RunOnUiThread(() => _window.WindowState = value);
    }

    /// <summary>
    /// 窗口位置。
    /// </summary>
    public override Point2I Position
    {
        get => _window.Position;
        set => RunOnUiThread(() => _window.Position = value);
    }

    /// <summary>
    /// 窗口尺寸。
    /// </summary>
    public override Point2I Size
    {
        get => _window.Size;
        set => RunOnUiThread(() => _window.Size = value);
    }

    /// <summary>
    /// 最小尺寸（0 = 不限）。
    /// </summary>
    public override Point2I MinSize
    {
        get => _window.MinSize;
        set => _window.MinSize = value;
    }

    /// <summary>
    /// 最大尺寸（0 = 不限）。
    /// </summary>
    public override Point2I MaxSize
    {
        get => _window.MaxSize;
        set => _window.MaxSize = value;
    }

    /// <summary>
    /// 是否显示在任务栏。
    /// </summary>
    public override bool ShowInTaskbar
    {
        get => _window.ShowInTaskbar;
        set => RunOnUiThread(() => _window.ShowInTaskbar = value);
    }

    /// <summary>
    /// 是否可调整大小。
    /// </summary>
    public override bool CanResize
    {
        get => _window.CanResize;
        set => RunOnUiThread(() => _window.CanResize = value);
    }

    /// <summary>
    /// 是否可最小化。
    /// </summary>
    public override bool CanMinimize
    {
        get => _window.CanMinimize;
        set => RunOnUiThread(() => _window.CanMinimize = value);
    }

    /// <summary>
    /// 是否可最大化。
    /// </summary>
    public override bool CanMaximize
    {
        get => _window.CanMaximize;
        set => RunOnUiThread(() => _window.CanMaximize = value);
    }

    /// <summary>
    /// 是否对话框式窗口。
    /// </summary>
    public override bool IsDialog
    {
        get => _window.IsDialog;
        set => RunOnUiThread(() => _window.IsDialog = value);
    }

    /// <summary>
    /// 窗口当前是否活动（由系统推导；置前台请用 <see cref="Activate"/>）。
    /// </summary>
    public override bool IsActive
    {
        get => _window.IsActive;
        set { }
    }

    /// <summary>
    /// 窗口所在显示器。
    /// </summary>
    public override Screen Screens => _window.Screens;

    /// <summary>
    /// 显示窗口并加载首页。GTK 的窗口 API 只允许在主线程访问，非主线程调用时 marshal 回主线程。
    /// 无头模式下跳过 Present()（窗口不出现在屏幕/任务栏），但照常加载首页。
    /// </summary>
    public override void Show(WebWindow? Parent = null)
    {
        RunOnUiThread(() =>
        {
            if (!Options.Headless)
                _window.Show();
            WebKit2Native.LoadUri(_webView, WebWindowResource.GetWindowIndexUrl(Options.WindowPath));
        });
    }

    /// <summary>
    /// 模态显示：显示窗口后跑嵌套 GLib 主循环直到本窗口关闭（GTK 模态语义）。对话框关闭结果经 Close(result) 传入。
    /// </summary>
    public override void ShowDialog(WebWindow? Parent = null)
    {
        Show(Parent);
        var nested = MainLoop.New(null, false);
        Closed += (_, _) => nested.Quit();
        nested.RunWithSynchronizationContext();
    }

    /// <summary>
    /// 隐藏窗口（不关闭、不销毁）。
    /// </summary>
    public override void Hide() => RunOnUiThread(() => _window.Hide());

    /// <summary>
    /// 关闭窗口。gtk_window_close → 默认 close-request 处理器 destroy → OnDestroyed。
    /// </summary>
    /// <param name="result">对话框关闭结果（当前平台未使用）。</param>
    public override void Close(object? result)
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
    public override void Activate() => RunOnUiThread(() => _window.Activate());

    /// <summary>
    /// 设置窗口图标（gtk_window_set_icon；CSD 画标题栏图标，X11 同时给 WM 任务栏）。
    /// </summary>
    /// <param name="icon">窗口图标；null 不操作。</param>
    public override void SetIcon(WindowIcon? icon)
    {
        if (icon is null)
            return;
        RunOnUiThread(() => _window.SetIcon(icon));
    }

    /// <summary>
    /// 向页面 JS 发送一条消息。protobuf 字节经 <see cref="StringCodec"/> 转成不含 NUL 的
    /// Latin-1 字符串，再用 <see cref="JsStringLiteral.Quote"/> 嵌进 <c>window.wwuiReceive("...")</c>
    /// 经 evaluateJavascript 注入。JS 端 wwuiReceive 还原后 protobufjs 解码。
    /// 与 Windows 一致：WebKit 对象只能主线程访问，属性变更可能发生在任意线程，先投递回主线程。
    /// </summary>
    /// <param name="message">protobuf 字节。</param>
    internal override void PostMessage(byte[] message)
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
            var js = "window.wwuiReceive(" + JsStringLiteral.Quote(StringCodec.Encode(message)) + ")";
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
    /// <param name="script">要执行的 JS。</param>
    /// <returns>JS 执行结果（JSON 字符串）。</returns>
    internal override async Task<string> ExecuteScriptAsync(string script)
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
    /// script message 回调：解码 NUL 转义 Latin-1 字符串为 protobuf 字节并交给基类分派。
    /// </summary>
    /// <param name="message">桥回传的 NUL 转义字符串。</param>
    private void OnScriptMessageReceived(string message)
    {
        if (message.Length == 0)
        {
            WebWindowLog.Debug("空 script message 收到");
            return;
        }
        OnBackendMessageReceived(StringCodec.Decode(message));
    }

    /// <summary>
    /// 加载进度回调：主 frame 加载完成触发 Loaded 并补发 Model 快照。
    /// </summary>
    /// <param name="loadEvent">加载事件枚举值。</param>
    private void OnLoadChanged(int loadEvent)
    {
        WebWindowLog.Debug($"load-changed: {loadEvent}");
        if (loadEvent == (int)WebKit2Native.LoadEvent.Finished)
        {
            _isLoaded = true;
            RaiseLoaded();
            if (_model is not null)
                PostMessage(_model.BuildSnapshotEnvelope());
        }
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
        _model?.UnsubscribePushed(_modelPushHandler);
        RaiseClosed();
        // 断开信号、释放 .NET 侧持有的 webview 引用（窗口仍持有其子级引用，至此引用计数归零 → 销毁）
        _signals?.Dispose();
        _signals = null;
        WebKit2Native.ReleaseWebView(_webView);
        _window.Dispose(); // 断开 destroy 信号并释放路由 GCHandle
        LinuxPlatform.WindowClose(this); // 注销窗口表 + 通知框架关闭 + 最后窗口退出主循环
    }

    /// <summary>
    /// 模型推送回调：把变化信封转发给页面。
    /// </summary>
    /// <param name="envelope">模型变化信封（protobuf 字节）。</param>
    private void ModelPushed(byte[] envelope) => PostMessage(envelope);

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
