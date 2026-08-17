using System.Runtime.InteropServices;
using WebWindowUI.Core;
using WebWindowUI.Core.Platform;

namespace WebWindowUI.Natives.Linux;

/// <summary>
/// Linux 原生窗口（GTK3 顶层窗口），镜像 Win32NativeWindow：封装 GTK 窗口句柄生命周期与
/// destroy/configure 信号桥 + 窗口状态面（装饰/状态/位置/尺寸/任务栏/活动/屏幕，真实现），
/// 平台经 <see cref="INativeWindow"/> 消费。GTK 窗口 API 只允许主线程访问，调用方负责 marshal。
/// </summary>
public sealed class LinuxNativeWindow : INativeWindow
{
    // 保活：native 只持有函数指针，委托实例必须被静态字段强引用。
    private static readonly SignalDestroyCallback _destroyTrampoline = OnDestroyed;
    private static readonly SignalConfigureCallback _configureTrampoline = OnConfigure;
    private static readonly SignalNotifyCallback _positionTrampoline = OnPositionChanged;
    private static readonly SignalNotifyCallback _activeTrampoline = OnActiveChanged;
    private static readonly SignalStateCallback _stateTrampoline = OnWindowStateEvent;

    private readonly IntPtr _window;
    private readonly GCHandle _handle;
    private ulong _destroyHandlerId;
    private ulong _configureHandlerId;
    private ulong _positionHandlerId;
    private ulong _activeHandlerId;
    private ulong _stateHandlerId;

    // 窗口状态跟踪字段（setter 应用原生 API；getter 读字段或系统状态推导）。
    private SystemDecorations _decorations = SystemDecorations.Full;
    private bool _canResize = true;
    private bool _canMinimize = true; // GTK3 无独立 per-window 最小化开关，仅跟踪（no-op，文档注明）
    private bool _canMaximize = true; // GTK3 无独立 per-window 最大化开关，仅跟踪（no-op，文档注明）
    private bool _showInTaskbar = true;
    private bool _dialog;
    private bool _fullScreen;
    private bool _borderlessFull;
    private Point2I _minSize;
    private Point2I _maxSize;

    /// <summary>
    /// 窗口句柄。
    /// </summary>
    public IntPtr WindowHandle => _window;

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
    /// 创建 GTK 窗口并连接 destroy/configure/position/is-active/window-state-event 信号。
    /// </summary>
    /// <param name="options">窗口选项（标题/尺寸）。</param>
    public LinuxNativeWindow(WebWindowOptions options)
    {
        _window = GtkNative.CreateWindow(options.Title, options.Width, options.Height);
        _handle = GCHandle.Alloc(this);
        _destroyHandlerId = GtkNative.ConnectSignal(_window, "destroy", _destroyTrampoline, _handle);
        _configureHandlerId = GtkNative.ConnectSignal(_window, "configure-event", _configureTrampoline, _handle);
        _positionHandlerId = GtkNative.ConnectSignal(_window, "notify::position", _positionTrampoline, _handle);
        _activeHandlerId = GtkNative.ConnectSignal(_window, "notify::is-active", _activeTrampoline, _handle);
        _stateHandlerId = GtkNative.ConnectSignal(_window, "window-state-event", _stateTrampoline, _handle);
    }

    /// <summary>
    /// 把子控件挂到窗口（创建 WebView 后调用）。gtk_container_add 收浮点引用，窗口接管一个引用。
    /// </summary>
    public void SetChild(IntPtr child) => GtkNative.SetChild(_window, child);

    /// <summary>
    /// 显示窗口。
    /// </summary>
    public void Show() => GtkNative.Show(_window);

    /// <summary>
    /// 隐藏窗口（不销毁）。
    /// </summary>
    public void Hide() => GtkNative.Hide(_window);

    /// <summary>
    /// 关闭窗口。gtk_window_close → 默认 close-request 处理器 destroy → <see cref="Destory"/>。
    /// </summary>
    public void Close() => GtkNative.Close(_window);

    /// <summary>
    /// 激活窗口。
    /// </summary>
    public void Activate() => GtkNative.Activate(_window);

    /// <summary>
    /// 修改标题。
    /// </summary>
    /// <param name="title">新标题。</param>
    public void SetTitle(string title) => GtkNative.SetTitle(_window, title);

    public void SetIcon(WindowIcon icon)
    {
        // GTK3 的 gtk_window_set_icon 在 CSD/Wayland 下不显示 per-window 图标，平台限制，无操作。
    }

    /// <summary>
    /// 取窗口当前尺寸。
    /// </summary>
    public Point2I GetSize()
    {
        GtkNative.GetSize(_window, out int width, out int height);
        return new Point2I { X = width, Y = height };
    }

    /// <summary>
    /// 窗口装饰样式：GTK3 装饰是二元（set_decorated），None 无标题栏，Border/Full 均带标题栏。
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
                GtkNative.SetDecorated(_window, value != SystemDecorations.None);
            SystemDecorationsChange?.Invoke(value);
        }
    }

    /// <summary>
    /// 窗口状态：get 从 GdkWindowState 推导；set 走 iconify/maximize/fullscreen。
    /// FullBorderLess 全屏 = 全屏 + 剥装饰（退出时恢复 _decorations）。
    /// </summary>
    public WindowState WindowState
    {
        get
        {
            if (_fullScreen)
                return _borderlessFull ? WindowState.FullBorderLess : WindowState.Full;
            IntPtr gdk = GtkNative.GetGdkWindow(_window);
            if (gdk == IntPtr.Zero)
                return WindowState.Normal; // 未 realized
            int state = GtkNative.GetWindowState(gdk);
            return MapState(state);
        }
        set
        {
            switch (value)
            {
                case WindowState.Minimize:
                    ExitFullScreen();
                    GtkNative.Iconify(_window);
                    break;
                case WindowState.Maximize:
                    ExitFullScreen();
                    GtkNative.Maximize(_window);
                    break;
                case WindowState.Normal:
                    ExitFullScreen();
                    IntPtr gdk = GtkNative.GetGdkWindow(_window);
                    if (gdk != IntPtr.Zero && (GtkNative.GetWindowState(gdk) & GtkNative.GdkWindowStateIconified) != 0)
                        GtkNative.Deiconify(_window);
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
    /// 窗口位置（屏幕坐标，左上角）：get 走 gtk_window_get_position；set 走 gtk_window_move。
    /// </summary>
    public Point2I Position
    {
        get
        {
            GtkNative.GetPosition(_window, out int x, out int y);
            return new Point2I { X = x, Y = y };
        }
        set => GtkNative.Move(_window, value.X, value.Y);
    }

    /// <summary>
    /// 窗口尺寸：get 同 GetSize；set 走 gtk_window_resize。
    /// </summary>
    public Point2I Size
    {
        get => GetSize();
        set => GtkNative.Resize(_window, value.X, value.Y);
    }

    /// <summary>
    /// 最小尺寸（0 表示不限制）：经 gtk_window_set_geometry_hints 生效。
    /// </summary>
    public Point2I MinSize
    {
        get => _minSize;
        set
        {
            _minSize = value;
            ApplyGeometryHints();
        }
    }

    /// <summary>
    /// 最大尺寸（0 表示不限制）：经 gtk_window_set_geometry_hints 生效。
    /// </summary>
    public Point2I MaxSize
    {
        get => _maxSize;
        set
        {
            _maxSize = value;
            ApplyGeometryHints();
        }
    }

    /// <summary>
    /// 是否显示在任务栏：set 走 skip_taskbar_hint。
    /// </summary>
    public bool ShowInTaskbar
    {
        get => _showInTaskbar;
        set
        {
            if (_showInTaskbar == value)
                return;
            _showInTaskbar = value;
            GtkNative.SetSkipTaskbarHint(_window, !value);
        }
    }

    /// <summary>
    /// 是否可调整大小：set 走 gtk_window_set_resizable。
    /// </summary>
    public bool CanResize
    {
        get => _canResize;
        set
        {
            if (_canResize == value)
                return;
            _canResize = value;
            GtkNative.SetResizable(_window, value);
        }
    }

    /// <summary>
    /// 是否可最小化：GTK3 无独立 per-window API，仅跟踪取值（no-op，文档注明）。
    /// </summary>
    public bool CanMinimize
    {
        get => _canMinimize;
        set => _canMinimize = value;
    }

    /// <summary>
    /// 是否可最大化：GTK3 无独立 per-window API，仅跟踪取值（no-op，文档注明）。
    /// </summary>
    public bool CanMaximize
    {
        get => _canMaximize;
        set => _canMaximize = value;
    }

    /// <summary>
    /// 是否对话框式窗口：set 走 gtk_window_set_type_hint(DIALOG)。
    /// </summary>
    public bool IsDialog
    {
        get => _dialog;
        set
        {
            if (_dialog == value)
                return;
            _dialog = value;
            GtkNative.SetTypeHint(_window, value ? GtkNative.GdkWindowTypeHintDialog : GtkNative.GdkWindowTypeHintNormal);
        }
    }

    /// <summary>
    /// 窗口当前是否活动。
    /// </summary>
    public bool IsActive => GtkNative.IsActive(_window);

    /// <summary>
    /// 窗口所在显示器（monitor_at_window；序号即显示器索引）。
    /// </summary>
    public Screen Screens
    {
        get
        {
            IntPtr gdk = GtkNative.GetGdkWindow(_window);
            int monitor = gdk == IntPtr.Zero ? 0 : GtkNative.GetMonitorAtWindow(gdk);
            GtkNative.GetMonitorGeometry(monitor, out GtkNative.GdkRectangle rect);
            return new Screen(monitor, new Point2I { X = rect.width, Y = rect.height });
        }
    }

    /// <summary>
    /// 把 GdkWindowState 标志映射为窗口状态。
    /// </summary>
    /// <param name="gdkState">GdkWindowState 标志。</param>
    /// <returns>窗口状态。</returns>
    private WindowState MapState(int gdkState)
    {
        if ((gdkState & GtkNative.GdkWindowStateFullscreen) != 0)
            return _borderlessFull ? WindowState.FullBorderLess : WindowState.Full;
        if ((gdkState & GtkNative.GdkWindowStateIconified) != 0)
            return WindowState.Minimize;
        if ((gdkState & GtkNative.GdkWindowStateMaximized) != 0)
            return WindowState.Maximize;
        return WindowState.Normal;
    }

    /// <summary>
    /// 应用 min/max 几何约束（GdkGeometry + GdkWindowHints）。
    /// </summary>
    private void ApplyGeometryHints()
    {
        var geo = new GtkNative.GdkGeometry
        {
            min_width = _minSize.X,
            min_height = _minSize.Y,
            max_width = _maxSize.X,
            max_height = _maxSize.Y,
        };
        int mask = 0;
        if (_minSize.X > 0 && _minSize.Y > 0)
            mask |= GtkNative.GdkHintMinSize;
        if (_maxSize.X > 0 && _maxSize.Y > 0)
            mask |= GtkNative.GdkHintMaxSize;
        if (mask != 0)
            GtkNative.SetGeometryHints(_window, ref geo, mask);
    }

    /// <summary>
    /// 进入全屏：全屏 + （FullBorderLess 时）剥装饰。
    /// </summary>
    /// <param name="borderless">是否无边框全屏。</param>
    private void EnterFullScreen(bool borderless)
    {
        if (_fullScreen && _borderlessFull == borderless)
            return;
        _fullScreen = true;
        _borderlessFull = borderless;
        GtkNative.SetDecorated(_window, !borderless && _decorations != SystemDecorations.None);
        GtkNative.Fullscreen(_window);
    }

    /// <summary>
    /// 退出全屏：恢复装饰。
    /// </summary>
    private void ExitFullScreen()
    {
        if (!_fullScreen)
            return;
        _fullScreen = false;
        GtkNative.Unfullscreen(_window);
        GtkNative.SetDecorated(_window, _decorations != SystemDecorations.None);
    }

    /// <summary>
    /// 断开全部信号并释放路由 GCHandle。窗口已销毁时 DisconnectSignal 吞掉异常。
    /// </summary>
    public void Dispose()
    {
        GtkNative.DisconnectSignal(_window, _destroyHandlerId);
        GtkNative.DisconnectSignal(_window, _configureHandlerId);
        GtkNative.DisconnectSignal(_window, _positionHandlerId);
        GtkNative.DisconnectSignal(_window, _activeHandlerId);
        GtkNative.DisconnectSignal(_window, _stateHandlerId);
        _destroyHandlerId = 0;
        _configureHandlerId = 0;
        _positionHandlerId = 0;
        _activeHandlerId = 0;
        _stateHandlerId = 0;
        if (_handle.IsAllocated)
            _handle.Free();
    }

    /// <summary>
    /// destroy 信号 trampoline：经 GCHandle 路由回 Destory。
    /// </summary>
    private static void OnDestroyed(IntPtr window, IntPtr userData)
    {
        try
        {
            (GCHandle.FromIntPtr(userData).Target as LinuxNativeWindow)?.Destory?.Invoke();
        }
        catch
        {
            // 窗口已销毁 / GCHandle 已释放等，忽略
        }
    }

    /// <summary>
    /// configure-event：窗口 move/resize/show 都会触发。返回 FALSE 表示未处理，交给 GTK 继续。
    /// </summary>
    private static int OnConfigure(IntPtr widget, IntPtr event_, IntPtr userData)
    {
        try
        {
            (GCHandle.FromIntPtr(userData).Target as LinuxNativeWindow)?.Resize?.Invoke();
        }
        catch
        {
            // 窗口已销毁 / GCHandle 已释放等，忽略
        }
        return 0; // FALSE：不拦截事件，GTK 默认处理
    }

    /// <summary>
    /// notify::position 信号 trampoline：位置变化 → Move。
    /// </summary>
    private static void OnPositionChanged(IntPtr instance, IntPtr pspec, IntPtr userData)
    {
        try
        {
            var w = GCHandle.FromIntPtr(userData).Target as LinuxNativeWindow;
            if (w is null)
                return;
            GtkNative.GetPosition(w._window, out int x, out int y);
            w.Move?.Invoke(new Point2I { X = x, Y = y });
        }
        catch
        {
            // 窗口已销毁，忽略
        }
    }

    /// <summary>
    /// notify::is-active 信号 trampoline：激活状态变化 → Active。
    /// </summary>
    private static void OnActiveChanged(IntPtr instance, IntPtr pspec, IntPtr userData)
    {
        try
        {
            var w = GCHandle.FromIntPtr(userData).Target as LinuxNativeWindow;
            if (w is null)
                return;
            w.Active?.Invoke(GtkNative.IsActive(w._window));
        }
        catch
        {
            // 窗口已销毁，忽略
        }
    }

    /// <summary>
    /// window-state-event 信号 trampoline：最小化/最大化/全屏等状态变化 → WindowStateChange。
    /// </summary>
    private static int OnWindowStateEvent(IntPtr widget, IntPtr event_, IntPtr userData)
    {
        try
        {
            var w = GCHandle.FromIntPtr(userData).Target as LinuxNativeWindow;
            if (w is null)
                return 0;
            var ev = Marshal.PtrToStructure<GdkEventWindowState>(event_);
            w.WindowStateChange?.Invoke(w.MapState(ev.new_window_state));
        }
        catch
        {
            // 窗口已销毁，忽略
        }
        return 0; // FALSE：不拦截事件，GTK 默认处理
    }

    /// <summary>
    /// GdkEventWindowState 布局（type/window/new_window_state；send_event 补齐对齐，未读取）。
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct GdkEventWindowState
    {
        public int type;
        public IntPtr window;
        public int new_window_state;
        public int send_event;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void SignalDestroyCallback(IntPtr instance, IntPtr userData);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I4)]
    private delegate int SignalConfigureCallback(IntPtr widget, IntPtr event_, IntPtr userData);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void SignalNotifyCallback(IntPtr instance, IntPtr pspec, IntPtr userData);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I4)]
    private delegate int SignalStateCallback(IntPtr widget, IntPtr event_, IntPtr userData);
}
