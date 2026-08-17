using System.ComponentModel;
using System.Runtime.InteropServices;
using WebWindowUI.Core;
using WebWindowUI.Core.Platform;

namespace WebWindowUI.Natives.Windows;

/// <summary>
/// Win32 裸窗口：封装 HWND 生命周期（创建/显示/销毁）+ 窗口状态面（装饰/状态/位置/尺寸/任务栏/
/// 可调性/活动/屏幕），经窗口过程路由回框架事件。窗口状态全部真实现（样式位 + WM_GETMINMAXINFO）。
/// </summary>
public class Win32NativeWindow : INativeWindow
{
    /// <summary>
    /// 注册的窗口类名。
    /// </summary>
    public const string WindowClass = "WebView2Window";

    private IntPtr _hIcon;

    private readonly IntPtr _hwnd;

    // 窗口状态跟踪字段（真实现：setter 应用原生样式位/显示命令，getter 读字段或系统状态推导）。
    private SystemDecorations _decorations = SystemDecorations.Full;
    private bool _canResize = true;
    private bool _canMinimize = true;
    private bool _canMaximize = true;
    private bool _showInTaskbar = true;
    private bool _dialog;
    private bool _fullScreen;
    private bool _borderlessFull;
    private Point2I _minSize;
    private Point2I _maxSize;
    private Win32.RECT _savedRect; // 全屏前的窗口矩形（ExitFullScreen 恢复用）
    private bool _savedRectKnown;

    /// <summary>
    /// 窗口句柄。
    /// </summary>
    public IntPtr WindowHandle => _hwnd;

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
    /// 创建窗口并登记进消息循环窗口表。
    /// </summary>
    /// <param name="options">窗口选项（标题/尺寸）。</param>
    public Win32NativeWindow(WebWindowOptions options)
    {
        _hwnd = Win32.CreateWindowExW(
            0, WindowClass, options.Title, Win32.WS_OVERLAPPEDWINDOW,
            Win32.CW_USEDEFAULT, Win32.CW_USEDEFAULT, options.Width, options.Height,
            IntPtr.Zero, IntPtr.Zero, Win32.GetModuleHandleW(null), IntPtr.Zero);
        if (_hwnd == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "创建窗口失败 (CreateWindowExW)");

        ApplyStyle();

        Win32MessageLoop.WindowOpened(this);
    }

    /// <summary>
    /// 显示窗口。
    /// </summary>
    public void Show()
    {
        Win32.ShowWindow(_hwnd, Win32.SW_SHOW);
    }

    /// <summary>
    /// 隐藏窗口（不销毁）。
    /// </summary>
    public void Hide()
    {
        Win32.ShowWindow(_hwnd, Win32.SW_HIDE);
    }

    /// <summary>
    /// 销毁窗口。
    /// </summary>
    public void Close()
    {
        Win32.DestroyWindow(_hwnd);
    }

    /// <summary>
    /// 激活窗口：先恢复最小化，再置前并聚焦。
    /// </summary>
    public void Activate()
    {
        if (Win32.IsIconic(_hwnd))
            Win32.ShowWindow(_hwnd, Win32.SW_RESTORE);
        Win32.SetForegroundWindow(_hwnd);
        Win32.SetFocus(_hwnd);
    }

    /// <summary>
    /// 修改标题。
    /// </summary>
    /// <param name="title">新标题。</param>
    public void SetTitle(string title)
    {
        Win32.SetWindowTextW(_hwnd, title);
    }

    /// <summary>
    /// 设置窗口图标，替换时释放旧图标句柄。
    /// </summary>
    /// <param name="icon">窗口图标。</param>
    public void SetIcon(WindowIcon icon)
    {
        var hIcon = LoadIconHandle(icon);
        if (hIcon == IntPtr.Zero)
            return;

        if (_hIcon != IntPtr.Zero)
            Win32.DestroyIcon(_hIcon);
        _hIcon = hIcon;

        Win32.SendMessageW(_hwnd, Win32.WM_SETICON, Win32.ICON_BIG, hIcon);
        Win32.SendMessageW(_hwnd, Win32.WM_SETICON, Win32.ICON_SMALL, hIcon);
    }

    /// <summary>
    /// 获取客户区尺寸。
    /// </summary>
    /// <returns>客户区尺寸。</returns>
    public Point2I GetSize()
    {
        Win32.GetClientRect(_hwnd, out Win32.RECT rc);
        return new Point2I { X = rc.Right, Y = rc.Bottom };
    }

    /// <summary>
    /// 窗口装饰样式：get 返回跟踪字段；set 应用样式位（None 去标题栏，Border 带标题栏无厚边框，
    /// Full 带标题栏 + 可缩放边框）并触发变化事件。
    /// </summary>
    public SystemDecorations SystemDecorations
    {
        get => _decorations;
        set
        {
            if (_decorations == value)
                return;
            _decorations = value;
            ApplyStyle();
            SystemDecorationsChange?.Invoke(value);
        }
    }

    /// <summary>
    /// 窗口状态：get 从系统推导（全屏/最小化/最大化/普通）；set 走 ShowWindow 或全屏进出。
    /// Full/FullBorderLess 用 SetWindowPos 铺满所在显示器，退出恢复原矩形与样式。
    /// </summary>
    public WindowState WindowState
    {
        get
        {
            if (_fullScreen)
                return _borderlessFull ? WindowState.FullBorderLess : WindowState.Full;
            if (Win32.IsIconic(_hwnd))
                return WindowState.Minimize;
            if (Win32.IsZoomed(_hwnd))
                return WindowState.Maximize;
            return WindowState.Normal;
        }
        set
        {
            switch (value)
            {
                case WindowState.Minimize:
                    ExitFullScreen();
                    Win32.ShowWindow(_hwnd, Win32.SW_MINIMIZE);
                    break;
                case WindowState.Maximize:
                    ExitFullScreen();
                    Win32.ShowWindow(_hwnd, Win32.SW_MAXIMIZE);
                    break;
                case WindowState.Normal:
                    ExitFullScreen();
                    Win32.ShowWindow(_hwnd, Win32.SW_RESTORE);
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
    /// 窗口位置（屏幕坐标，左上角）：get 走 GetWindowRect；set 用 MoveWindow 保持尺寸只移原点。
    /// </summary>
    public Point2I Position
    {
        get
        {
            Win32.GetWindowRect(_hwnd, out Win32.RECT rc);
            return new Point2I { X = rc.Left, Y = rc.Top };
        }
        set
        {
            Win32.GetWindowRect(_hwnd, out Win32.RECT rc);
            Win32.MoveWindow(_hwnd, value.X, value.Y, rc.Right - rc.Left, rc.Bottom - rc.Top, true);
        }
    }

    /// <summary>
    /// 窗口尺寸（客户区，与 GetSize 一致）：set 经 AdjustWindowRectEx 转成窗口外尺寸再 MoveWindow。
    /// </summary>
    public Point2I Size
    {
        get => GetSize();
        set
        {
            var rc = new Win32.RECT { Left = 0, Top = 0, Right = value.X, Bottom = value.Y };
            int style = Win32.GetWindowLongPtrW(_hwnd, Win32.GWL_STYLE).ToInt32();
            uint ex = (uint)Win32.GetWindowLongPtrW(_hwnd, Win32.GWL_EXSTYLE);
            Win32.AdjustWindowRectEx(ref rc, style, false, ex);
            var pos = Position;
            Win32.MoveWindow(_hwnd, pos.X, pos.Y, rc.Right - rc.Left, rc.Bottom - rc.Top, true);
        }
    }

    /// <summary>
    /// 最小尺寸（0 表示不限制）：经 WM_GETMINMAXINFO 生效。
    /// </summary>
    public Point2I MinSize
    {
        get => _minSize;
        set => _minSize = value;
    }

    /// <summary>
    /// 最大尺寸（0 表示不限制）：经 WM_GETMINMAXINFO 生效。
    /// </summary>
    public Point2I MaxSize
    {
        get => _maxSize;
        set => _maxSize = value;
    }

    /// <summary>
    /// 是否显示在任务栏：set 切换 WS_EX_APPWINDOW/WS_EX_TOOLWINDOW。
    /// </summary>
    public bool ShowInTaskbar
    {
        get => _showInTaskbar;
        set
        {
            if (_showInTaskbar == value)
                return;
            _showInTaskbar = value;
            ApplyStyle();
        }
    }

    /// <summary>
    /// 是否可调整大小：set 切换 WS_THICKFRAME（Full 装饰下始终带厚边框）。
    /// </summary>
    public bool CanResize
    {
        get => _canResize;
        set
        {
            if (_canResize == value)
                return;
            _canResize = value;
            ApplyStyle();
        }
    }

    /// <summary>
    /// 是否可最小化：set 切换 WS_MINIMIZEBOX。
    /// </summary>
    public bool CanMinimize
    {
        get => _canMinimize;
        set
        {
            if (_canMinimize == value)
                return;
            _canMinimize = value;
            ApplyStyle();
        }
    }

    /// <summary>
    /// 是否可最大化：set 切换 WS_MAXIMIZEBOX。
    /// </summary>
    public bool CanMaximize
    {
        get => _canMaximize;
        set
        {
            if (_canMaximize == value)
                return;
            _canMaximize = value;
            ApplyStyle();
        }
    }

    /// <summary>
    /// 是否对话框式窗口：set 切换 WS_EX_DLGMODALFRAME 对话框边框。
    /// </summary>
    public bool IsDialog
    {
        get => _dialog;
        set
        {
            if (_dialog == value)
                return;
            _dialog = value;
            ApplyStyle();
        }
    }

    /// <summary>
    /// 窗口当前是否活动（前台窗口即本窗口）。
    /// </summary>
    public bool IsActive => Win32.GetForegroundWindow() == _hwnd;

    /// <summary>
    /// 窗口所在显示器（MonitorFromWindow 最近匹配；Index 为枚举序，主屏先行）。
    /// </summary>
    public Screen Screens
    {
        get
        {
            var hMonitor = Win32.MonitorFromWindow(_hwnd, Win32.MONITOR_DEFAULTTONEAREST);
            int index = 0;
            var size = new Point2I();
            Win32.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr mon, IntPtr dc, ref Win32.RECT rc, IntPtr data) =>
            {
                if (mon == hMonitor)
                {
                    size = new Point2I { X = rc.Right - rc.Left, Y = rc.Bottom - rc.Top };
                }
                else
                {
                    index++;
                }
                return true;
            }, IntPtr.Zero);
            return new Screen(index, size);
        }
    }

    /// <summary>
    /// 重建窗口样式位（GWL_STYLE/GWL_EXSTYLE）并 SWP_FRAMECHANGED 使生效。
    /// 跟踪字段（装饰/可调性/任务栏/对话框）在此统一落到原生位。
    /// </summary>
    private void ApplyStyle()
    {
        uint ex = (uint)Win32.GetWindowLongPtrW(_hwnd, Win32.GWL_EXSTYLE);
        ex &= ~(Win32.WS_EX_APPWINDOW | Win32.WS_EX_TOOLWINDOW | Win32.WS_EX_DLGMODALFRAME);
        if (_showInTaskbar)
            ex |= Win32.WS_EX_APPWINDOW;
        else
            ex |= Win32.WS_EX_TOOLWINDOW;
        if (_dialog)
            ex |= Win32.WS_EX_DLGMODALFRAME;
        Win32.SetWindowLongPtrW(_hwnd, Win32.GWL_EXSTYLE, (IntPtr)ex);

        // FullBorderLess 全屏时剥掉全部装饰（含标题栏），其余按装饰等级 + 可调性位组装。
        bool stripAll = _fullScreen && _borderlessFull;
        int style = Win32.WS_OVERLAPPED;
        if (!stripAll && _decorations != SystemDecorations.None)
        {
            style |= Win32.WS_CAPTION | Win32.WS_SYSMENU;
            if (_decorations == SystemDecorations.Full || _canResize)
                style |= Win32.WS_THICKFRAME;
            if (_canMinimize)
                style |= Win32.WS_MINIMIZEBOX;
            if (_canMaximize)
                style |= Win32.WS_MAXIMIZEBOX;
        }
        Win32.SetWindowLongPtrW(_hwnd, Win32.GWL_STYLE, (IntPtr)style);

        Win32.SetWindowPos(_hwnd, IntPtr.Zero, 0, 0, 0, 0,
            Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOZORDER | Win32.SWP_NOACTIVATE | Win32.SWP_FRAMECHANGED);
    }

    /// <summary>
    /// 进入全屏：保存当前矩形，铺满所在显示器；FullBorderLess 同时剥装饰。
    /// </summary>
    /// <param name="borderless">是否无边框全屏。</param>
    private void EnterFullScreen(bool borderless)
    {
        if (_fullScreen)
        {
            if (_borderlessFull != borderless)
            {
                _borderlessFull = borderless;
                ApplyStyle();
            }
            return;
        }
        if (!_savedRectKnown)
        {
            Win32.GetWindowRect(_hwnd, out _savedRect);
            _savedRectKnown = true;
        }
        _fullScreen = true;
        _borderlessFull = borderless;
        ApplyStyle();
        Win32.GetWindowRect(_hwnd, out _savedRect); // 重取：样式变更后矩形可能微调
        var hMonitor = Win32.MonitorFromWindow(_hwnd, Win32.MONITOR_DEFAULTTONEAREST);
        var mi = new Win32.MONITORINFO { cbSize = Marshal.SizeOf<Win32.MONITORINFO>() };
        if (Win32.GetMonitorInfoW(hMonitor, ref mi))
        {
            Win32.SetWindowPos(_hwnd, IntPtr.Zero, mi.rcMonitor.Left, mi.rcMonitor.Top,
                mi.rcMonitor.Right - mi.rcMonitor.Left, mi.rcMonitor.Bottom - mi.rcMonitor.Top,
                Win32.SWP_NOZORDER | Win32.SWP_NOACTIVATE);
        }
    }

    /// <summary>
    /// 退出全屏：恢复样式与保存的窗口矩形。
    /// </summary>
    private void ExitFullScreen()
    {
        if (!_fullScreen)
            return;
        _fullScreen = false;
        ApplyStyle();
        if (_savedRectKnown)
        {
            Win32.SetWindowPos(_hwnd, IntPtr.Zero, _savedRect.Left, _savedRect.Top,
                _savedRect.Right - _savedRect.Left, _savedRect.Bottom - _savedRect.Top,
                Win32.SWP_NOZORDER | Win32.SWP_NOACTIVATE);
            _savedRectKnown = false;
        }
    }

    /// <summary>
    /// 把 WindowIcon（文件或流）加载成 HICON。流会先落到临时文件再加载。
    /// </summary>
    private static IntPtr LoadIconHandle(WindowIcon icon)
    {
        var tmp = Path.Combine(Path.GetTempPath(), "webwindowui_" + Guid.NewGuid().ToString("N") + ".ico");
        try
        {
            using (FileStream fs = File.Create(tmp))
                icon.Stream.CopyTo(fs);
            return Win32.LoadImageW(IntPtr.Zero, tmp, Win32.IMAGE_ICON,
                0, 0, Win32.LR_LOADFROMFILE | Win32.LR_DEFAULTSIZE);
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    /// <summary>
    /// 窗口过程：分发 WM_CLOSE/WM_DESTROY/WM_SIZE/WM_MOVE/WM_ACTIVATE/WM_GETMINMAXINFO，
    /// 其余走默认处理。
    /// </summary>
    /// <param name="msg">消息 id。</param>
    /// <param name="wParam">消息参数。</param>
    /// <param name="lParam">消息参数。</param>
    /// <returns>消息处理结果。</returns>
    public IntPtr OnWndProc(uint msg, IntPtr wParam, IntPtr lParam)
    {
        switch (msg)
        {
            case Win32.WM_CLOSE:
                Win32.DestroyWindow(_hwnd);
                return IntPtr.Zero;

            case Win32.WM_DESTROY:
                if (_hIcon != IntPtr.Zero)
                {
                    Win32.DestroyIcon(_hIcon);
                    _hIcon = IntPtr.Zero;
                }
                Destory?.Invoke();
                Win32MessageLoop.WindowClose(this);
                return IntPtr.Zero;

            case Win32.WM_SIZE:
                Resize?.Invoke();
                WindowStateChange?.Invoke(WindowState);
                return IntPtr.Zero;

            case Win32.WM_MOVE:
                int packed = lParam.ToInt32();
                Move?.Invoke(new Point2I { X = packed & 0xFFFF, Y = packed >> 16 });
                return IntPtr.Zero;

            case Win32.WM_ACTIVATE:
                Active?.Invoke(wParam.ToInt32() != Win32.WA_INACTIVE);
                return IntPtr.Zero;

            case Win32.WM_GETMINMAXINFO:
                ApplyMinMaxInfo(lParam);
                return IntPtr.Zero;

            default:
                return Win32.DefWindowProcW(_hwnd, msg, wParam, lParam);
        }
    }

    /// <summary>
    /// WM_GETMINMAXINFO：把 MinSize/MaxSize 写入 lParam 指向的 MINMAXINFO。
    /// </summary>
    /// <param name="lParam">MINMAXINFO 指针。</param>
    private void ApplyMinMaxInfo(IntPtr lParam)
    {
        if (lParam == IntPtr.Zero)
            return;
        var mmi = Marshal.PtrToStructure<Win32.MINMAXINFO>(lParam);
        if (_minSize.X > 0 && _minSize.Y > 0)
            mmi.ptMinTrackSize = new Win32.POINT { X = _minSize.X, Y = _minSize.Y };
        if (_maxSize.X > 0 && _maxSize.Y > 0)
            mmi.ptMaxTrackSize = new Win32.POINT { X = _maxSize.X, Y = _maxSize.Y };
        Marshal.StructureToPtr(mmi, lParam, false);
    }
}
