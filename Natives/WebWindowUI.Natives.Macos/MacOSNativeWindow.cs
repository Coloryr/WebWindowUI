using WebWindowUI.Core;
using WebWindowUI.Core.Platform;

namespace WebWindowUI.Natives.Macos;

/// <summary>
/// macOS 原生窗口（NSWindow），镜像 Win32NativeWindow/LinuxNativeWindow：封装 NSWindow 生命周期
/// 与窗口状态面（装饰/状态/位置/尺寸/任务栏/活动/屏幕，真实现）。Cocoa 只允许主线程访问，调用方负责
/// marshal（MacOSWindow 经 MacOSMessageLoopSynchronizationContext）。生命周期事件经 NSWindowDelegate 转发。
/// </summary>
public sealed class MacOSNativeWindow : INativeWindow
{
    private readonly NSWindow _window;
    private readonly MacOSNativeWindowDelegate _delegate;

    // 窗口状态跟踪字段（setter 应用原生 API；getter 读字段或系统状态推导）。
    private SystemDecorations _decorations = SystemDecorations.Full;
    private bool _canResize = true;
    private bool _canMinimize = true;
    private bool _canMaximize = true; // 缩放（zoom）与 Resizable 绑定，无独立开关，仅跟踪（no-op，文档注明）
    private bool _showInTaskbar = true; // macOS 窗口不单独出现在 Dock（按 App），仅跟踪（no-op，文档注明）
    private bool _dialog; // 对话框式窗口用 NSPanel，运行时不可切换，仅跟踪（no-op，文档注明）
    private bool _fullScreen;
    private bool _borderlessFull;
    private Point2I _minSize;
    private Point2I _maxSize;

    /// <summary>
    /// 原生窗口句柄。
    /// </summary>
    public IntPtr WindowHandle => _window.Handle;

    /// <summary>
    /// 承载的 NSWindow（平台窗口取 ContentView 挂 WebView）。
    /// </summary>
    public NSWindow Window => _window;

    /// <summary>
    /// 窗口销毁时触发。
    /// </summary>
    public event Action? Destory;

    /// <summary>
    /// 窗口尺寸变化时触发。
    /// </summary>
    public event Action? Resize;

    /// <summary>
    /// 窗口位置变化时触发。
    /// </summary>
    public event Action<Point2I>? Move;

    /// <summary>
    /// 窗口激活状态变化时触发。
    /// </summary>
    public event Action<bool>? Active;

    /// <summary>
    /// 窗口状态变化时触发。
    /// </summary>
    public event Action<WindowState>? WindowStateChange;

    /// <summary>
    /// 窗口装饰样式变化时触发。
    /// </summary>
    public event Action<SystemDecorations>? SystemDecorationsChange;

    /// <summary>
    /// 创建 NSWindow 并挂生命周期委托（关闭/移动/缩放/全屏/激活等）。
    /// </summary>
    /// <param name="options">窗口选项（标题/尺寸）。</param>
    public MacOSNativeWindow(WebWindowOptions options)
    {
        _window = new NSWindow(
            new CGRect(0, 0, options.Width, options.Height),
            NSWindowStyle.Titled | NSWindowStyle.Closable | NSWindowStyle.Resizable | NSWindowStyle.Miniaturizable,
            NSBackingStore.Buffered,
            false) // defer: 立即创建原生窗口
        {
            Title = options.Title,
#pragma warning disable CS0618 // ReleasedWhenClosed 属性过时（新 API ReleaseWhenClosed() 语义相反：关闭即释放，这里正是要防止它）
            ReleasedWhenClosed = false, // 否则 Close() 后 NSObject 可能被过度释放
#pragma warning restore CS0618
        };
        _delegate = new MacOSNativeWindowDelegate(this);
        _window.Delegate = _delegate;
    }

    /// <summary>
    /// 显示窗口并聚焦。
    /// </summary>
    public void Show() => _window.MakeKeyAndOrderFront(null);

    /// <summary>
    /// 隐藏窗口（不关闭）。
    /// </summary>
    public void Hide() => _window.OrderOut(null);

    /// <summary>
    /// 关闭窗口（windowWillClose: → <see cref="Destory"/>）。
    /// </summary>
    public void Close() => _window.Close();

    /// <summary>
    /// 激活窗口：置前并聚焦内容视图。
    /// </summary>
    public void Activate()
    {
        _window.MakeKeyAndOrderFront(null);
        _window.MakeFirstResponder(_window.ContentView);
    }

    /// <summary>
    /// 修改标题。
    /// </summary>
    /// <param name="title">新标题。</param>
    public void SetTitle(string title) => _window.Title = title;

    /// <summary>
    /// 设置窗口图标。macOS 窗口无 per-window 图标（图标属于 App Bundle），平台限制，无操作。
    /// </summary>
    public void SetIcon(WindowIcon icon)
    {
    }

    /// <summary>
    /// 创建窗口托盘（NSStatusItem 未实现，抛异常明示）。
    /// </summary>
    /// <param name="name">托盘提示文本。</param>
    /// <exception cref="NotSupportedException">macOS 平台尚未实现托盘。</exception>
    public ITrayIcon CreateTrayIcon(string name)
        => throw new NotSupportedException("macOS 平台暂不支持系统托盘（NSStatusItem 未实现）。");

    /// <summary>
    /// 取窗口当前尺寸（Frame 外框尺寸）。
    /// </summary>
    public Point2I GetSize()
    {
        var f = _window.Frame;
        return new Point2I { X = (int)f.Width, Y = (int)f.Height };
    }

    /// <summary>
    /// 窗口装饰样式：None 无标题栏（Borderless）；Border/Full 带标题栏。macOS 无「仅边框」样式，
    /// Border 与 Full 区别只在 Resizable/Miniaturizable 是否置位（文档注明）。
    /// </summary>
    public SystemDecorations SystemDecorations
    {
        get => _decorations;
        set
        {
            if (_decorations == value)
                return;
            _decorations = value;
            if (!_fullScreen)
                ApplyStyleMask();
            SystemDecorationsChange?.Invoke(value);
        }
    }

    /// <summary>
    /// 窗口状态：get 从系统推导（全屏/最小化/缩放/普通）；set 走 Miniaturize/PerformZoom/ToggleFullScreen。
    /// FullBorderLess = 全屏 + Borderless（退出恢复样式）。
    /// </summary>
    public WindowState WindowState
    {
        get
        {
            if (_fullScreen)
                return _borderlessFull ? WindowState.FullBorderLess : WindowState.Full;
            if (_window.IsMiniaturized)
                return WindowState.Minimize;
            if (_window.IsZoomed)
                return WindowState.Maximize;
            return WindowState.Normal;
        }
        set
        {
            switch (value)
            {
                case WindowState.Minimize:
                    ExitFullScreen();
                    _window.Miniaturize(null);
                    break;
                case WindowState.Maximize:
                    ExitFullScreen();
                    _window.PerformZoom(null);
                    break;
                case WindowState.Normal:
                    ExitFullScreen();
                    if (_window.IsMiniaturized)
                        _window.Deminiaturize(null);
                    if (_window.IsZoomed)
                        _window.Zoom(null);
                    break;
                case WindowState.Full:
                    EnterFullScreen(borderless: false);
                    break;
                case WindowState.FullBorderLess:
                    EnterFullScreen(borderless: true);
                    break;
            }
        }
    }

    /// <summary>
    /// 窗口位置（Frame 原点，屏幕坐标）。macOS 屏幕原点在左下（Y 向上）。
    /// </summary>
    public Point2I Position
    {
        get
        {
            var f = _window.Frame;
            return new Point2I { X = (int)f.X, Y = (int)f.Y };
        }
        set => _window.SetFrameOrigin(new CGPoint(value.X, value.Y));
    }

    /// <summary>
    /// 窗口尺寸：get 同 GetSize；set 经 SetContentSize（内容区尺寸）。
    /// </summary>
    public Point2I Size
    {
        get => GetSize();
        set => _window.SetContentSize(new CGSize(value.X, value.Y));
    }

    /// <summary>
    /// 最小尺寸（0 表示不限制）：经 ContentMinSize 生效。
    /// </summary>
    public Point2I MinSize
    {
        get => _minSize;
        set
        {
            _minSize = value;
            ApplySizeLimits();
        }
    }

    /// <summary>
    /// 最大尺寸（0 表示不限制）：经 ContentMaxSize 生效。
    /// </summary>
    public Point2I MaxSize
    {
        get => _maxSize;
        set
        {
            _maxSize = value;
            ApplySizeLimits();
        }
    }

    /// <summary>
    /// 是否显示在任务栏（Dock）：macOS 按 App 不按窗口，no-op（文档注明）。
    /// </summary>
    public bool ShowInTaskbar
    {
        get => _showInTaskbar;
        set => _showInTaskbar = value;
    }

    /// <summary>
    /// 是否可调整大小：set 切换 Resizable 样式位。
    /// </summary>
    public bool CanResize
    {
        get => _canResize;
        set
        {
            if (_canResize == value)
                return;
            _canResize = value;
            ApplyStyleMask();
        }
    }

    /// <summary>
    /// 是否可最小化：set 切换 Miniaturizable 样式位。
    /// </summary>
    public bool CanMinimize
    {
        get => _canMinimize;
        set
        {
            if (_canMinimize == value)
                return;
            _canMinimize = value;
            ApplyStyleMask();
        }
    }

    /// <summary>
    /// 是否可最大化：macOS zoom 与 Resizable 绑定，无独立开关，仅跟踪（no-op，文档注明）。
    /// </summary>
    public bool CanMaximize
    {
        get => _canMaximize;
        set => _canMaximize = value;
    }

    /// <summary>
    /// 是否对话框式窗口：对话框用 NSPanel，运行时不可切换，仅跟踪（no-op，文档注明）。
    /// </summary>
    public bool IsDialog
    {
        get => _dialog;
        set => _dialog = value;
    }

    /// <summary>
    /// 窗口当前是否活动（key/main 窗口）。
    /// </summary>
    public bool IsActive => _window.IsKeyWindow || _window.IsMainWindow;

    /// <summary>
    /// 窗口所在显示器（窗口中心命中；Index 为 Screens 数组下标）。
    /// </summary>
    public Screen Screens
    {
        get
        {
            var screens = NSScreen.Screens;
            var wf = _window.Frame;
            var cx = wf.GetMidX();
            var cy = wf.GetMidY();
            for (int i = 0; i < screens.Length; i++)
            {
                var f = screens[i].Frame;
                if (f.Contains(cx, cy))
                    return new Screen(i, new Point2I { X = (int)f.Width, Y = (int)f.Height });
            }
            if (screens.Length > 0)
            {
                var f = screens[0].Frame;
                return new Screen(0, new Point2I { X = (int)f.Width, Y = (int)f.Height });
            }
            return new Screen(0, new Point2I());
        }
    }

    /// <summary>
    /// 重建 StyleMask：按装饰等级 + 可调性位组装（FullBorderLess 全屏用 Borderless）。
    /// </summary>
    private void ApplyStyleMask()
    {
        if (_fullScreen && _borderlessFull)
        {
            _window.StyleMask = NSWindowStyle.Borderless;
            return;
        }
        if (_decorations == SystemDecorations.None)
        {
            _window.StyleMask = NSWindowStyle.Borderless;
            return;
        }
        var style = NSWindowStyle.Titled | NSWindowStyle.Closable;
        if (_canResize)
            style |= NSWindowStyle.Resizable;
        if (_canMinimize)
            style |= NSWindowStyle.Miniaturizable;
        _window.StyleMask = style;
    }

    /// <summary>
    /// 应用 min/max 内容区尺寸限制（正无穷 = 不限）。
    /// </summary>
    private void ApplySizeLimits()
    {
        _window.ContentMinSize = new CGSize(_minSize.X, _minSize.Y);
        _window.ContentMaxSize = _maxSize.X > 0 && _maxSize.Y > 0
            ? new CGSize(_maxSize.X, _maxSize.Y)
            : new CGSize(float.PositiveInfinity, float.PositiveInfinity);
    }

    /// <summary>
    /// 进入全屏：全屏 + （FullBorderLess 时）Borderless。
    /// </summary>
    /// <param name="borderless">是否无边框全屏。</param>
    private void EnterFullScreen(bool borderless)
    {
        if (_fullScreen && _borderlessFull == borderless)
            return;
        _fullScreen = true;
        _borderlessFull = borderless;
        ApplyStyleMask();
        _window.ToggleFullScreen(null);
    }

    /// <summary>
    /// 退出全屏：恢复样式。
    /// </summary>
    private void ExitFullScreen()
    {
        if (!_fullScreen)
            return;
        _fullScreen = false;
        _window.ToggleFullScreen(null);
        ApplyStyleMask();
    }

    /// <summary>
    /// NSWindowDelegate：把窗口生命周期信号转发到事件（保活字段持有，防绑定 delegate 被 GC）。
    /// </summary>
    private sealed class MacOSNativeWindowDelegate : NSWindowDelegate
    {
        private readonly MacOSNativeWindow _owner;

        /// <summary>
        /// 构造委托。
        /// </summary>
        /// <param name="owner">宿主原生窗口。</param>
        public MacOSNativeWindowDelegate(MacOSNativeWindow owner) => _owner = owner;

        public override void WillClose(NSNotification notification) => _owner.Destory?.Invoke();

        public override void DidResize(NSNotification notification) => _owner.Resize?.Invoke();

        public override void DidMove(NSNotification notification)
        {
            var p = _owner.Position;
            _owner.Move?.Invoke(p);
        }

        public override void DidBecomeKey(NSNotification notification) => _owner.Active?.Invoke(true);

        public override void DidResignKey(NSNotification notification) => _owner.Active?.Invoke(false);

        public override void DidMiniaturize(NSNotification notification) => _owner.WindowStateChange?.Invoke(WindowState.Minimize);

        public override void DidDeminiaturize(NSNotification notification) => _owner.WindowStateChange?.Invoke(WindowState.Normal);

        public override void DidZoom(NSNotification notification)
            => _owner.WindowStateChange?.Invoke(_owner._window.IsZoomed ? WindowState.Maximize : WindowState.Normal);

        public override void DidEnterFullScreen(NSNotification notification)
            => _owner.WindowStateChange?.Invoke(_owner._borderlessFull ? WindowState.FullBorderLess : WindowState.Full);

        public override void DidExitFullScreen(NSNotification notification) => _owner.WindowStateChange?.Invoke(WindowState.Normal);
    }
}
