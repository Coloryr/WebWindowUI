using WebWindowUI.Core;
using WebWindowUI.Core.Platform;
using WebWindowUI.Core.Protocol;
using WebWindowUI.Natives.Windows;
using Xilium.CefGlue;
using Xilium.CefGlue.Common.Events;

namespace WebWindowUI.Platforms.Cef;

/// <summary>
/// CEF 平台窗口：纯 CefGlue 公共 API 自建浏览器托管（<see cref="CefBrowserHosting"/> 隐藏宿主 +
/// 重挂载），承载于裸 Win32 顶层窗口，可创建多个实例。浏览器关闭（仅主浏览器）销毁顶层窗口。
/// 窗口状态面经 <see cref="INativeWindow"/> 真实现；Model 双向绑定在基类契约内完成。
/// </summary>
public sealed class CefWindow : WebWindow
{
    private readonly Win32NativeWindow _nativeWindow;
    private readonly CefBrowserHosting _hosting;
    private readonly ManualResetEventSlim _closedEvent = new(false);

    private string _title;
    private WebWindowModel? _model;
    private readonly Action<byte[]> _modelPushHandler;
    private bool _isLoaded;
    private bool _closed;

    /// <summary>
    /// 主浏览器实例（浏览器初始化时记录；BrowserClosed 过滤 DevTools 等弹窗用）。
    /// </summary>
    private CefBrowser? _mainBrowser;

    /// <summary>
    /// 主浏览器 id（scheme 回调分派用）。
    /// </summary>
    private long _browserId;

    /// <summary>
    /// 构造窗口：建 Win32 顶层窗口 + 浏览器托管（隐藏宿主创建浏览器，OnAfterCreated 重挂载并导航初始 URL）。
    /// </summary>
    /// <param name="options">窗口选项。</param>
    internal CefWindow(WebWindowOptions options) : base(options)
    {
        // CEF 初始化由 CefPlatform.Init 完成（首个窗口创建前）；此处仅校验。
        if (!CefRuntime.IsInitialized)
            throw new InvalidOperationException("CEF 未初始化，请先调用 WebWindowUIPlatform.Init");

        _title = options.Title;
        _modelPushHandler = ModelPushed;
        _nativeWindow = new Win32NativeWindow(options);

        // 浏览器托管：隐藏宿主创建浏览器 → OnAfterCreated 重挂载进本窗口并导航初始 URL。
        _hosting = new CefBrowserHosting(WebWindowResource.GetWindowIndexUrl(Options.WindowPath, Options.Query));
        _hosting.Initialized += OnBrowserInitialized;
        _hosting.BrowserClosed += OnBrowserClosed;
        _hosting.LoadEnd += OnLoadEnd;

        var size = _nativeWindow.GetSize();
        _hosting.Create(_nativeWindow.WindowHandle, size.X, size.Y);

        _nativeWindow.Destory += NativeWindow_Destory;
        _nativeWindow.Resize += NativeWindow_Resize;
        _nativeWindow.Move += NativeWindow_Move;
        _nativeWindow.Active += NativeWindow_Active;
        _nativeWindow.WindowStateChange += NativeWindow_WindowStateChange;
        _nativeWindow.SystemDecorationsChange += NativeWindow_SystemDecorationsChange;
    }

    /// <summary>
    /// 原生窗口句柄。
    /// </summary>
    public IntPtr Hwnd => _nativeWindow.WindowHandle;

    /// <summary>
    /// 原生窗口（平台窗口内部使用）。
    /// </summary>
    internal override INativeWindow NativeWindow
    {
        get => _nativeWindow;
        set => throw new NotSupportedException("CefWindow 自建原生窗口，不支持替换。");
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
            if (_nativeWindow is not null)
                WebWindowPlatform.Current.RunOnUiThread(() => _nativeWindow.SetTitle(value));
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
        get => _nativeWindow.SystemDecorations;
        set => WebWindowPlatform.Current.RunOnUiThread(() => _nativeWindow.SystemDecorations = value);
    }

    /// <summary>
    /// 窗口状态。
    /// </summary>
    public override WindowState WindowState
    {
        get => _nativeWindow.WindowState;
        set => WebWindowPlatform.Current.RunOnUiThread(() => _nativeWindow.WindowState = value);
    }

    /// <summary>
    /// 窗口位置。
    /// </summary>
    public override Point2I Position
    {
        get => _nativeWindow.Position;
        set => WebWindowPlatform.Current.RunOnUiThread(() => _nativeWindow.Position = value);
    }

    /// <summary>
    /// 窗口尺寸（客户区）。
    /// </summary>
    public override Point2I Size
    {
        get => _nativeWindow.Size;
        set => WebWindowPlatform.Current.RunOnUiThread(() => _nativeWindow.Size = value);
    }

    /// <summary>
    /// 最小尺寸（0 = 不限）。
    /// </summary>
    public override Point2I MinSize
    {
        get => _nativeWindow.MinSize;
        set => _nativeWindow.MinSize = value;
    }

    /// <summary>
    /// 最大尺寸（0 = 不限）。
    /// </summary>
    public override Point2I MaxSize
    {
        get => _nativeWindow.MaxSize;
        set => _nativeWindow.MaxSize = value;
    }

    /// <summary>
    /// 是否显示在任务栏。
    /// </summary>
    public override bool ShowInTaskbar
    {
        get => _nativeWindow.ShowInTaskbar;
        set => WebWindowPlatform.Current.RunOnUiThread(() => _nativeWindow.ShowInTaskbar = value);
    }

    /// <summary>
    /// 是否可调整大小。
    /// </summary>
    public override bool CanResize
    {
        get => _nativeWindow.CanResize;
        set => WebWindowPlatform.Current.RunOnUiThread(() => _nativeWindow.CanResize = value);
    }

    /// <summary>
    /// 是否可最小化。
    /// </summary>
    public override bool CanMinimize
    {
        get => _nativeWindow.CanMinimize;
        set => WebWindowPlatform.Current.RunOnUiThread(() => _nativeWindow.CanMinimize = value);
    }

    /// <summary>
    /// 是否可最大化。
    /// </summary>
    public override bool CanMaximize
    {
        get => _nativeWindow.CanMaximize;
        set => WebWindowPlatform.Current.RunOnUiThread(() => _nativeWindow.CanMaximize = value);
    }

    /// <summary>
    /// 是否对话框式窗口。
    /// </summary>
    public override bool IsDialog
    {
        get => _nativeWindow.IsDialog;
        set => WebWindowPlatform.Current.RunOnUiThread(() => _nativeWindow.IsDialog = value);
    }

    /// <summary>
    /// 窗口当前是否活动（由系统推导；置前台请用 <see cref="Activate"/>）。
    /// </summary>
    public override bool IsActive
    {
        get => _nativeWindow.IsActive;
        set { }
    }

    /// <summary>
    /// 窗口所在显示器。
    /// </summary>
    public override Screen Screens => _nativeWindow.Screens;

    /// <summary>
    /// 显示窗口（浏览器已创建，随顶层窗口一起可见）。
    /// </summary>
    public override void Show(WebWindow? Parent = null)
    {
        WebWindowPlatform.Current.RunOnUiThread(() =>
        {
            if (!Options.Headless)
                _nativeWindow.Show();
            _hosting.Reapply();
        });
    }

    /// <summary>
    /// 模态显示：显示窗口后阻塞调用线程直到关闭（UI 线程走嵌套消息泵；后台线程直接等待，
    /// 关闭由主循环泵触发）。对话框关闭结果经 Close(result) 传入。
    /// </summary>
    public override void ShowDialog(WebWindow? Parent = null)
    {
        Show(Parent);
        if (WebWindowPlatform.Current.IsUiThread())
        {
            // UI 线程：嵌套消息泵直到本窗口关闭（经 CefPlatform 公共泵 API，Win32 内部不可直引）
            CefPlatform.RunModalLoop(() => _closedEvent.IsSet);
        }
        else
        {
            _closedEvent.Wait();
        }
    }

    /// <summary>
    /// 隐藏窗口（不关闭、不销毁）。
    /// </summary>
    public override void Hide()
    {
        if (!Options.Headless)
        {
            WebWindowPlatform.Current.RunOnUiThread(_nativeWindow.Hide);
        }
    }

    /// <summary>
    /// 关闭窗口：浏览器已初始化则先关浏览器（OnBrowserClosed 回调销毁顶层窗口），未创建则直接销毁。
    /// 关闭最后一个窗口后程序自动退出。
    /// </summary>
    /// <param name="result">对话框关闭结果（当前平台未使用）。</param>
    public override void Close(object? result)
    {
        WebWindowPlatform.Current.RunOnUiThread(() =>
        {
            if (_closed)
                return;
            if (_hosting.IsInitialized)
            {
                try
                {
                    _hosting.Close(true);
                }
                catch
                {
                    // 浏览器已销毁时忽略
                }
            }
            else
            {
                _nativeWindow.Close();
            }
        });
    }

    /// <summary>
    /// 把窗口带到前台并聚焦：先恢复最小化，再置前、设焦点。
    /// </summary>
    public override void Activate()
    {
        WebWindowPlatform.Current.RunOnUiThread(_nativeWindow.Activate);
    }

    /// <summary>
    /// 设置窗口图标（标题栏 + 任务栏）。
    /// </summary>
    /// <param name="icon">窗口图标；null 不操作。</param>
    public override void SetIcon(WindowIcon? icon)
    {
        if (icon is null)
            return;
        WebWindowPlatform.Current.RunOnUiThread(() => _nativeWindow.SetIcon(icon));
    }

    /// <summary>
    /// 向页面 JS 发送一条消息：protobuf 字节经 <see cref="StringCodec"/> 做 NUL 转义后
    /// 嵌进 <c>window.wwuiReceive("...")</c>，投递到 CEF UI 线程注入。
    /// </summary>
    /// <param name="message">protobuf 字节。</param>
    internal override void PostMessage(byte[] message)
    {
        try
        {
            if (_closed)
                return;
            var js = "window.wwuiReceive(" + JsStringLiteral.Quote(StringCodec.Encode(message)) + ")";
            CefPlatform.PostToCefUiThread(() => _hosting.ExecuteJavaScript(js, "about:blank", 1));
        }
        catch
        {
            // 窗口关闭后浏览器已销毁，忽略（定时器仍在后台推送）
        }
    }

    /// <summary>
    /// 在页面里执行一段 JavaScript 并返回结果（best-effort；失败回退空串）。
    /// </summary>
    /// <param name="script">要执行的 JS 脚本。</param>
    /// <returns>执行结果（JSON 编码字符串）。</returns>
    internal override Task<string> ExecuteScriptAsync(string script)
    {
        if (_closed)
            throw new InvalidOperationException("窗口已关闭。");
        return _hosting.EvaluateJavaScript(script, "about:blank", 1);
    }

    /// <summary>
    /// scheme 处理器收到 JS 回传、解码后调用本方法。回调在 CEF IO/UI 线程。
    /// </summary>
    /// <param name="payload">protobuf 字节。</param>
    internal void OnMessageFromWeb(byte[] payload) => OnBackendMessageReceived(payload);

    /// <summary>
    /// 浏览器初始化完成（CEF UI 线程回调）：记录主浏览器并登记 id → 窗口映射。
    /// </summary>
    private void OnBrowserInitialized()
    {
        var browser = _hosting.Browser;
        if (browser is not null)
        {
            _mainBrowser = browser;
            _browserId = browser.Identifier;
            CefPlatform.RegisterBrowser(_browserId, this);
        }
    }

    /// <summary>
    /// 浏览器销毁（CEF UI 线程回调）：仅主浏览器销毁顶层窗口（DevTools 等弹窗忽略）。
    /// </summary>
    /// <param name="browser">已销毁的浏览器。</param>
    private void OnBrowserClosed(CefBrowser browser)
    {
        if (!ReferenceEquals(browser, _mainBrowser))
            return;
        CefPlatform.UnregisterBrowser(_browserId);
        WebWindowPlatform.Current.RunOnUiThread(() =>
        {
            if (_closed)
                return;
            _nativeWindow.Close();
        });
    }

    /// <summary>
    /// 加载结束（CEF UI 线程回调）：主帧完成 → 页面就绪，触发 Loaded 并补发 Model 快照。
    /// </summary>
    /// <param name="sender">事件源。</param>
    /// <param name="e">事件参数。</param>
    private void OnLoadEnd(object? sender, LoadEndEventArgs e)
    {
        if (e.Frame.IsMain && !_closed)
        {
            _isLoaded = true;
            RaiseLoaded();
            if (_model is not null)
                PostMessage(_model.BuildSnapshotEnvelope());
        }
    }

    /// <summary>
    /// 原生窗口尺寸变化：同步浏览器尺寸并触发 Resize 事件。
    /// </summary>
    private void NativeWindow_Resize()
    {
        _hosting.SetSize(_nativeWindow.Size.X, _nativeWindow.Size.Y);
        RaiseResize(_nativeWindow.Size);
    }

    /// <summary>
    /// 原生窗口位置变化：触发 Move 事件。
    /// </summary>
    /// <param name="position">新位置。</param>
    private void NativeWindow_Move(Point2I position) => RaiseMove(position);

    /// <summary>
    /// 原生窗口激活状态变化：触发 Active 事件。
    /// </summary>
    /// <param name="active">是否激活。</param>
    private void NativeWindow_Active(bool active) => RaiseActive(active);

    /// <summary>
    /// 原生窗口状态变化：触发 WindowStateChange 事件。
    /// </summary>
    /// <param name="state">新状态。</param>
    private void NativeWindow_WindowStateChange(WindowState state) => RaiseWindowStateChange(state);

    /// <summary>
    /// 原生窗口装饰变化：触发 SystemDecorationsChange 事件。
    /// </summary>
    /// <param name="decorations">新装饰样式。</param>
    private void NativeWindow_SystemDecorationsChange(SystemDecorations decorations)
        => RaiseSystemDecorationsChange(decorations);

    /// <summary>
    /// 原生窗口销毁：解除模型订阅并触发 Closed（浏览器应已在 OnBrowserClosed 路径关闭）。
    /// </summary>
    private void NativeWindow_Destory()
    {
        _model?.UnsubscribePushed(_modelPushHandler);
        _closed = true;
        _closedEvent.Set();
        RaiseClosed();
    }

    /// <summary>
    /// 模型推送回调：把变化信封转发给页面。
    /// </summary>
    /// <param name="envelope">模型变化信封（protobuf 字节）。</param>
    private void ModelPushed(byte[] envelope) => PostMessage(envelope);
}
