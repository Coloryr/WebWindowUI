using System.Drawing;
using Microsoft.Web.WebView2.Core;
using WebWindowUI.Core;
using WebWindowUI.Core.Platform;
using WebWindowUI.Core.Protocol;
using WebWindowUI.Natives.Windows;

namespace WebWindowUI.Platforms.Windows;

/// <summary>
/// Windows 平台：承载 WebView2 的 Win32 裸窗口，可创建多个实例。
/// 同类 scheme 的所有实例共享同一个 CoreWebView2Environment（自定义 scheme 只注册一次）。
/// 窗口状态面经 <see cref="INativeWindow"/> 真实现；Model 双向绑定（快照/推送/回写）在基类契约内完成。
/// </summary>
public sealed class WindowsWindow : WebWindow
{
    private readonly Win32NativeWindow _nativeWindow;
    private readonly ManualResetEventSlim _closedEvent = new(false);

    private CoreWebView2Controller? _controller;
    private string _title;
    private WebWindowModel? _model;
    private readonly Action<byte[]> _modelPushHandler;
    private bool _isLoaded;
    private bool _closed;

    /// <summary>
    /// 原生窗口句柄。
    /// </summary>
    public IntPtr Hwnd => _nativeWindow.WindowHandle;

    internal WindowsWindow(WebWindowOptions options) : base(options)
    {
        _title = options.Title;
        _modelPushHandler = ModelPushed;
        _nativeWindow = new Win32NativeWindow(options);

        _nativeWindow.Destory += NativeWindow_Destory;
        _nativeWindow.Resize += NativeWindow_Resize;
        _nativeWindow.Move += NativeWindow_Move;
        _nativeWindow.Active += NativeWindow_Active;
        _nativeWindow.WindowStateChange += NativeWindow_WindowStateChange;
        _nativeWindow.SystemDecorationsChange += NativeWindow_SystemDecorationsChange;
    }

    /// <summary>
    /// 原生窗口句柄（平台窗口内部使用）。
    /// </summary>
    internal override INativeWindow NativeWindow
    {
        get => _nativeWindow;
        set => throw new NotSupportedException("WindowsWindow 自建原生窗口，不支持替换。");
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
    /// 显示窗口并异步初始化 WebView2（无头模式只初始化不显示）。
    /// </summary>
    public override void Show(WebWindow? Parent = null)
    {
        if (!Options.Headless)
        {
            _nativeWindow.Show();
        }
        _ = InitWebViewAsync();
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
            // UI 线程：嵌套消息泵直到本窗口关闭（经 WindowsPlatform 公共泵 API，Win32 内部不可直引）
            WindowsPlatform.RunModalLoop(() => _closedEvent.IsSet);
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
    /// 关闭窗口。关闭最后一个窗口后程序自动退出。
    /// </summary>
    /// <param name="result">对话框关闭结果（当前平台未使用）。</param>
    public override void Close(object? result)
    {
        // DestroyWindow 必须在创建窗口的线程调用；宿主可能从任意线程关窗，marshal 回 UI 线程同步执行。
        WebWindowPlatform.Current.RunOnUiThread(() =>
        {
            if (_closed)
                return;
            _closed = true;
            _nativeWindow.Close();
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
    /// 设置窗口图标（标题栏 + 任务栏）。替换旧图标时释放旧的句柄。
    /// </summary>
    /// <param name="icon">窗口图标；null 不操作。</param>
    public override void SetIcon(WindowIcon? icon)
    {
        if (icon is null)
            return;
        WebWindowPlatform.Current.RunOnUiThread(() => _nativeWindow.SetIcon(icon));
    }

    /// <summary>
    /// 向页面 JS 发送一条 protobuf 消息：经 <see cref="StringCodec"/> 做 NUL 转义后走
    /// PostWebMessageAsString（WebView2 消息通道在首个 NUL 处截断，protobuf 字节普遍含 0x00）。
    /// </summary>
    /// <param name="message">protobuf 字节。</param>
    internal override void PostMessage(byte[] message)
    {
        try
        {
            if (WebWindowPlatform.Current.IsUiThread())
            {
                _controller?.CoreWebView2.PostWebMessageAsString(StringCodec.Encode(message));
            }
            else
            {
                WebWindowPlatform.Current.RunOnUiThread(() => PostMessage(message));
            }
        }
        catch
        {
            // 窗口关闭后控制器已释放，忽略（定时器仍在后台推送）
        }
    }

    /// <summary>
    /// 在页面里执行一段 JavaScript 并返回结果（JSON 编码的字符串，与 WebView2 一致）。
    /// 与 <see cref="PostMessage"/> 一样：CoreWebView2 只能在 UI 线程访问，非 UI 线程调用时
    /// 先投递回 UI 线程再执行，并等待结果。
    /// </summary>
    /// <param name="script">要执行的 JS。</param>
    /// <returns>JS 执行结果（JSON 字符串）。</returns>
    internal override async Task<string> ExecuteScriptAsync(string script)
    {
        if (!WebWindowPlatform.Current.IsUiThread())
        {
            var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            WebWindowPlatform.Current.RunOnUiThread(async () =>
            {
                try { tcs.TrySetResult(await ExecuteScriptAsync(script)); }
                catch (Exception ex) { tcs.TrySetException(ex); }
            });
            return await tcs.Task;
        }

        if (_controller?.CoreWebView2 is null)
            throw new InvalidOperationException("WebView2 尚未初始化完成。");
        return await _controller.CoreWebView2.ExecuteScriptAsync(script);
    }

    /// <summary>
    /// 原生窗口尺寸变化：同步 WebView2 控件边界并触发 Resize 事件。
    /// </summary>
    private void NativeWindow_Resize()
    {
        if (_controller is not null)
            _controller.Bounds = new Rectangle(0, 0, _nativeWindow.Size.X, _nativeWindow.Size.Y);
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
    /// 原生窗口销毁：关闭 WebView2 控制器并触发 Closed。
    /// </summary>
    private void NativeWindow_Destory()
    {
        _controller?.Close();
        _controller = null;
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

    /// <summary>
    /// 创建 WebView2 控制器、导航到窗口页面并挂导航/消息回调。
    /// </summary>
    private async Task InitWebViewAsync()
    {
        try
        {
            _controller = await WindowsPlatform.CreateCoreWebView2ControllerAsync(_nativeWindow.WindowHandle);
            if (_closed)
            {
                _controller.Close();
                _controller = null;
                return;
            }

            _controller.Bounds = new Rectangle(0, 0, _nativeWindow.Size.X, _nativeWindow.Size.Y);

            var core = _controller.CoreWebView2;

            core.Navigate(WebWindowResource.GetWindowIndexUrl(Options.WindowPath, Options.Query));

            // Model 双向绑定通道：页面就绪通知 + JS 回传消息
            core.NavigationCompleted += (_, _) =>
            {
                _isLoaded = true;
                RaiseLoaded();
                if (_model is not null)
                    PostMessage(_model.BuildSnapshotEnvelope());
            };
            core.WebMessageReceived += (_, args) =>
            {
                var message = args.TryGetWebMessageAsString();
                if (message.Length == 0)
                    return;

                // JS 侧经 NUL 转义编码后回传（模型.bridge 的 bytesToEscaped），这里还原回 protobuf 字节
                OnBackendMessageReceived(StringCodec.Decode(message));
            };
        }
        catch (Exception ex)
        {
            WebWindowLog.Error($"WebView2 初始化失败：{ex.Message}\n请确认已安装 WebView2 运行时。");
        }
    }
}
