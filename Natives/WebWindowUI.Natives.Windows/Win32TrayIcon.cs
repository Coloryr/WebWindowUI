using WebWindowUI.Core;
using WebWindowUI.Core.Platform;

namespace WebWindowUI.Natives.Windows;

/// <summary>
/// Win32 系统托盘图标：Shell_NotifyIcon + 弹出菜单树 + 气泡通知。
/// 消息（左键单击/双击/右键菜单）由所属窗口的 WM_TRAYICON 路由进来（见 <see cref="OnTrayMessage"/>）。
/// </summary>
public class Win32TrayIcon : ITrayIcon
{
    private const uint TrayIconId = 0x1001;

    private readonly IntPtr _hwnd;
    private readonly uint _uid;
    private IntPtr _hIcon;
    private PopupMenu? _menu;
    private bool _deleted;
    private int _nextId = 1;
    private readonly Dictionary<int, PopupMenu> _menuById = new();

    /// <summary>
    /// 单击（左/右/中键，经 <see cref="TrayClickEvent.Type"/> 区分；右键同时弹出菜单）。
    /// </summary>
    public event Action<TrayClickEvent>? Click;

    /// <summary>
    /// 双击（左键）。
    /// </summary>
    public event Action<TrayClickEvent>? DoubleClick;

    /// <summary>
    /// 创建托盘图标（NIM_ADD + 设版本 4 以获得 WM_CONTEXTMENU 右键菜单行为）。
    /// </summary>
    /// <param name="hwnd">所属窗口句柄（接收托盘消息）。</param>
    /// <param name="name">托盘提示文本。</param>
    public Win32TrayIcon(IntPtr hwnd, string name)
    {
        _hwnd = hwnd;
        _uid = TrayIconId;

        var nid = new NOTIFYICONDATA
        {
            cbSize = NOTIFYICONDATA.Size,
            hWnd = _hwnd,
            uID = _uid,
            uFlags = Win32.NIF_MESSAGE | Win32.NIF_TIP,
            uCallbackMessage = Win32.WM_TRAYICON,
            szTip = name,
        };
        if (Win32.Shell_NotifyIcon(Win32.NIM_ADD, in nid))
        {
            nid.uVersion = Win32.NOTIFYICON_VERSION_4;
            Win32.Shell_NotifyIcon(Win32.NIM_SETVERSION, in nid);
        }
    }

    /// <summary>
    /// 设置托盘图标（替换时释放旧句柄）。
    /// </summary>
    /// <param name="icon">图标数据。</param>
    public void SetIcon(WindowIcon icon)
    {
        var hIcon = LoadIconHandle(icon);
        if (hIcon == IntPtr.Zero)
            return;

        var old = _hIcon;
        _hIcon = hIcon;

        var nid = new NOTIFYICONDATA
        {
            cbSize = NOTIFYICONDATA.Size,
            hWnd = _hwnd,
            uID = _uid,
            uFlags = Win32.NIF_ICON,
            hIcon = hIcon,
        };
        Win32.Shell_NotifyIcon(Win32.NIM_MODIFY, in nid);

        if (old != IntPtr.Zero)
            Win32.DestroyIcon(old);
    }

    /// <summary>
    /// 设置托盘提示文本。
    /// </summary>
    /// <param name="tip">提示文本。</param>
    public void SetTip(string tip)
    {
        var nid = new NOTIFYICONDATA
        {
            cbSize = NOTIFYICONDATA.Size,
            hWnd = _hwnd,
            uID = _uid,
            uFlags = Win32.NIF_TIP,
            szTip = tip,
        };
        Win32.Shell_NotifyIcon(Win32.NIM_MODIFY, in nid);
    }

    /// <summary>
    /// 设置右键菜单（保存引用，弹出时构建原生菜单树）。
    /// </summary>
    /// <param name="menu">菜单树。</param>
    public void SetMenu(PopupMenu menu)
    {
        _menu = menu;
    }

    /// <summary>
    /// 手动弹出右键菜单（构建菜单树 → TrackPopupMenu 返回选中项 id → 触发 Click）。
    /// </summary>
    public void ShowMenu()
    {
        if (_menu is null)
            return;

        _menuById.Clear();
        _nextId = 1;
        IntPtr hMenu = BuildMenu(_menu);

        Win32.GetCursorPos(out POINT pt);
        Win32.SetForegroundWindow(_hwnd);
        uint cmd = Win32.TrackPopupMenu(hMenu,
            Win32.TPM_RIGHTBUTTON | Win32.TPM_RETURNCMD,
            pt.X, pt.Y, 0, _hwnd, IntPtr.Zero);
        Win32.DestroyMenu(hMenu);

        if (cmd != 0 && _menuById.TryGetValue((int)cmd, out PopupMenu? item))
            item.OnClick();
    }

    /// <summary>
    /// 显示气泡通知。
    /// </summary>
    /// <param name="title">标题（最多 63 字符）。</param>
    /// <param name="text">内容（最多 255 字符）。</param>
    /// <param name="type">通知样式。</param>
    public void ShowBalloon(string title, string text, TrayIconType type = TrayIconType.Info)
    {
        uint flag = type switch
        {
            TrayIconType.Warning => Win32.NIIF_WARNING,
            TrayIconType.Error => Win32.NIIF_ERROR,
            _ => Win32.NIIF_INFO,
        };
        var nid = new NOTIFYICONDATA
        {
            cbSize = NOTIFYICONDATA.Size,
            hWnd = _hwnd,
            uID = _uid,
            uFlags = Win32.NIF_INFO,
            szInfo = text,
            szInfoTitle = title,
            dwInfoFlags = flag,
        };
        Win32.Shell_NotifyIcon(Win32.NIM_MODIFY, in nid);
    }

    /// <summary>
    /// 显示或隐藏托盘图标（NIF_STATE + NIS_HIDDEN）。
    /// </summary>
    /// <param name="visible">是否可见。</param>
    public void SetVisible(bool visible)
    {
        var nid = new NOTIFYICONDATA
        {
            cbSize = NOTIFYICONDATA.Size,
            hWnd = _hwnd,
            uID = _uid,
            uFlags = Win32.NIF_STATE,
            dwState = visible ? 0 : Win32.NIS_HIDDEN,
            dwStateMask = Win32.NIS_HIDDEN,
        };
        Win32.Shell_NotifyIcon(Win32.NIM_MODIFY, in nid);
    }

    /// <summary>
    /// 移除托盘图标并释放图标句柄（幂等，窗口销毁时自动调用）。
    /// </summary>
    public void Delete()
    {
        if (_deleted)
            return;
        _deleted = true;

        var nid = new NOTIFYICONDATA
        {
            cbSize = NOTIFYICONDATA.Size,
            hWnd = _hwnd,
            uID = _uid,
        };
        Win32.Shell_NotifyIcon(Win32.NIM_DELETE, in nid);

        if (_hIcon != IntPtr.Zero)
        {
            Win32.DestroyIcon(_hIcon);
            _hIcon = IntPtr.Zero;
        }
    }

    /// <summary>
    /// 处理托盘消息（所属窗口 WM_TRAYICON 路由进来；wParam 为托盘 id，lParam 为鼠标消息）。
    /// 版本 4 下：左键单击 WM_LBUTTONUP / 双击 WM_LBUTTONDBLCLK / 右键 WM_CONTEXTMENU；
    /// 版本 3 回退：右键 WM_RBUTTONUP。单击/双击事件携带按钮类型与点击屏幕坐标（GetCursorPos）。
    /// </summary>
    /// <param name="msg">WM_TRAYICON 的 wParam（托盘 id）。</param>
    /// <param name="lParam">鼠标消息 id。</param>
    internal void OnTrayMessage(IntPtr msg, IntPtr lParam)
    {
        if ((uint)msg != _uid)
            return;

        var evt = new TrayClickEvent(MapClickType((uint)lParam), GetCursorPos());
        switch ((uint)lParam)
        {
            case Win32.WM_LBUTTONUP:
                Click?.Invoke(evt);
                break;
            case Win32.WM_LBUTTONDBLCLK:
                DoubleClick?.Invoke(evt);
                break;
            case Win32.WM_CONTEXTMENU:
            case Win32.WM_RBUTTONUP:
                Click?.Invoke(evt); // 右键单击也上报（Type=Right），菜单另弹
                ShowMenu();
                break;
            case Win32.WM_MBUTTONUP:
                Click?.Invoke(evt);
                break;
        }
    }

    /// <summary>
    /// 把鼠标消息 id 映射为点击按钮类型。
    /// </summary>
    /// <param name="mouseMsg">鼠标消息 id。</param>
    private static TrayClickType MapClickType(uint mouseMsg) => mouseMsg switch
    {
        Win32.WM_LBUTTONUP or Win32.WM_LBUTTONDBLCLK => TrayClickType.Left,
        Win32.WM_RBUTTONUP or Win32.WM_CONTEXTMENU => TrayClickType.Right,
        Win32.WM_MBUTTONUP => TrayClickType.Middle,
        _ => TrayClickType.Left,
    };

    /// <summary>
    /// 取当前鼠标屏幕坐标。
    /// </summary>
    private static Point2I GetCursorPos()
    {
        Win32.GetCursorPos(out POINT pt);
        return new Point2I { X = pt.X, Y = pt.Y };
    }

    /// <summary>
    /// 递归构建原生菜单树：分隔符 MF_SEPARATOR、子菜单 MF_POPUP、普通项分配自增 id（按构建序
    /// 与 <see cref="_menuById"/> 对应，TrackPopupMenu 返回该 id 再映射回 <see cref="PopupMenu"/>）。
    /// </summary>
    /// <param name="menu">菜单树。</param>
    /// <returns>HMENU 句柄。</returns>
    private IntPtr BuildMenu(PopupMenu menu)
    {
        IntPtr hMenu = Win32.CreatePopupMenu();
        foreach (var item in menu.Menus)
        {
            if (item.IsSeparator)
            {
                Win32.AppendMenuW(hMenu, Win32.MF_SEPARATOR, IntPtr.Zero, null);
                continue;
            }

            uint flags = 0;
            if (item.Menus.Count > 0)
            {
                // 子菜单：MF_POPUP，uIDNewItem 为子菜单句柄
                IntPtr hSub = BuildMenu(item);
                flags = Win32.MF_POPUP;
                if (!item.IsEnabled)
                    flags |= Win32.MF_GRAYED | Win32.MF_DISABLED;
                Win32.AppendMenuW(hMenu, flags, hSub, item.Name);
                continue;
            }

            int id = _nextId++;
            _menuById[id] = item;
            if (!item.IsEnabled)
                flags |= Win32.MF_GRAYED | Win32.MF_DISABLED;
            if (item.IsChecked)
                flags |= Win32.MF_CHECKED;
            Win32.AppendMenuW(hMenu, Win32.MF_STRING | flags, (IntPtr)id, item.Name);
        }
        return hMenu;
    }

    /// <summary>
    /// 把 WindowIcon 数据流加载成 HICON（临时文件 + LoadImageW，与窗口图标同法）。
    /// </summary>
    /// <param name="icon">图标数据。</param>
    /// <returns>图标句柄（失败为 IntPtr.Zero）。</returns>
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
}
