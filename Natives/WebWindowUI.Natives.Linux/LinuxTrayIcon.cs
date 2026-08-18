using System.Runtime.InteropServices;
using WebWindowUI.Core;
using WebWindowUI.Core.Platform;

namespace WebWindowUI.Natives.Linux;

/// <summary>
/// Linux 系统托盘图标：GtkStatusIcon + GtkMenu 菜单树。左键单击（activate 信号，带单击/双击
/// 间隔判定，双击窗口内被二次 activate 消费）、右键（popup-menu 信号，上报后弹菜单）；气泡通知
/// 经 <see cref="LinuxNotification"/>（libnotify）。GtkStatusIcon 无中键信号，Middle 不上报。
/// GTK 非线程安全，所有调用须在主线程。
/// </summary>
public sealed class LinuxTrayIcon : ITrayIcon
{
    /// <summary>
    /// 单击/双击判定窗口（两次 activate 间隔小于该值视为双击，单击延迟上报）。
    /// </summary>
    private const int DoubleClickWindowMs = 350;

    // 保活：native 只持有函数指针，委托实例必须被静态字段强引用。
    private static readonly SignalActivateCallback _activateTrampoline = OnActivate;
    private static readonly SignalActivateCallback _popupMenuTrampoline = OnPopupMenu;
    private static readonly SignalActivateCallback _menuItemTrampoline = OnMenuItemActivate;
    private static readonly SignalActivateCallback _menuHideTrampoline = OnMenuHide;
    private static readonly GtkNative.GSourceFunc _clickTimeoutTrampoline = OnClickTimeout;

    private readonly IntPtr _icon;
    private readonly GCHandle _handle;
    private ulong _activateHandlerId;
    private ulong _popupHandlerId;
    private PopupMenu? _menu;
    private bool _deleted;

    // 菜单树（弹出期间）：item 指针 → 信号 id + PopupMenu；hide 信号后整体销毁。
    private IntPtr _currentMenu;
    private ulong _menuHideId;
    private readonly Dictionary<IntPtr, MenuEntry> _menuByWidget = new();

    // 单击/双击判定状态（主线程访问）。
    private long _lastActivateTicks;
    private bool _pendingClick;
    private uint _clickTimeoutId;

    private readonly record struct MenuEntry(ulong HandlerId, PopupMenu Menu);

    /// <summary>
    /// 单击（左键 activate 延迟上报 / 右键 popup-menu 即时上报，右键同时弹菜单）。
    /// </summary>
    public event Action<TrayClickEvent>? Click;

    /// <summary>
    /// 双击（左键，两次 activate 间隔小于 <see cref="DoubleClickWindowMs"/>）。
    /// </summary>
    public event Action<TrayClickEvent>? DoubleClick;

    /// <summary>
    /// 创建托盘图标（GtkStatusIcon 强引用 + activate/popup-menu 信号）。
    /// </summary>
    /// <param name="name">托盘提示文本。</param>
    public LinuxTrayIcon(string name)
    {
        _icon = GtkNative.CreateStatusIcon();
        _handle = GCHandle.Alloc(this);
        _activateHandlerId = GtkNative.ConnectSignal(_icon, "activate", _activateTrampoline, _handle);
        _popupHandlerId = GtkNative.ConnectSignal(_icon, "popup-menu", _popupMenuTrampoline, _handle);
        GtkNative.StatusIconSetTooltip(_icon, name);
        GtkNative.StatusIconSetVisible(_icon, true);
    }

    /// <summary>
    /// 设置托盘图标：图标流写临时 PNG → GdkPixbuf → set_from_pixbuf（GTK ref，调用方 unref 自己的）。
    /// </summary>
    /// <param name="icon">图标数据。</param>
    public void SetIcon(WindowIcon icon)
    {
        if (_deleted)
            return;
        var tmp = Path.Combine(Path.GetTempPath(), "webwindowui_" + Guid.NewGuid().ToString("N") + ".png");
        try
        {
            icon.Stream.Seek(0, SeekOrigin.Begin);
            using (FileStream fs = File.Create(tmp))
                icon.Stream.CopyTo(fs);
            var pixbuf = GtkNative.LoadPixbufFromFile(tmp);
            if (pixbuf == IntPtr.Zero)
                return;
            try
            {
                GtkNative.StatusIconSetIcon(_icon, pixbuf);
            }
            finally
            {
                GtkNative.ObjectUnref(pixbuf);
            }
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    /// <summary>
    /// 设置托盘提示文本。
    /// </summary>
    /// <param name="tip">提示文本。</param>
    public void SetTip(string tip)
    {
        if (_deleted)
            return;
        GtkNative.StatusIconSetTooltip(_icon, tip);
    }

    /// <summary>
    /// 设置右键菜单（保存引用，弹出时构建原生菜单树）。
    /// </summary>
    /// <param name="menu">菜单树。</param>
    public void SetMenu(PopupMenu menu) => _menu = menu;

    /// <summary>
    /// 手动弹出右键菜单（构建 GtkMenu 树 → gtk_menu_popup_at_pointer；hide 信号后整体销毁）。
    /// </summary>
    public void ShowMenu()
    {
        if (_deleted || _menu is null)
            return;

        DestroyMenuTree();

        _menuByWidget.Clear();
        var gmenu = BuildMenu(_menu);
        _currentMenu = gmenu;
        _menuHideId = GtkNative.ConnectSignal(gmenu, "hide", _menuHideTrampoline, _handle, after: true);
        GtkNative.MenuPopupAtPointer(gmenu);
    }

    /// <summary>
    /// 气泡通知（libnotify 不可用时无效果）。
    /// </summary>
    /// <param name="title">标题。</param>
    /// <param name="text">内容。</param>
    /// <param name="type">通知样式。</param>
    public void ShowBalloon(string title, string text, TrayIconType type = TrayIconType.Info)
        => LinuxNotification.Instance.Show(title, text, MapType(type));

    /// <summary>
    /// 显示或隐藏托盘图标。
    /// </summary>
    /// <param name="visible">是否可见。</param>
    public void SetVisible(bool visible)
    {
        if (_deleted)
            return;
        GtkNative.StatusIconSetVisible(_icon, visible);
    }

    /// <summary>
    /// 移除托盘图标并释放引用（幂等，窗口销毁时自动调用）。
    /// </summary>
    public void Delete()
    {
        if (_deleted)
            return;
        _deleted = true;

        CancelClickTimeout();
        DestroyMenuTree();

        GtkNative.DisconnectSignal(_icon, _activateHandlerId);
        GtkNative.DisconnectSignal(_icon, _popupHandlerId);
        _activateHandlerId = 0;
        _popupHandlerId = 0;
        GtkNative.StatusIconSetVisible(_icon, false);
        if (_icon != IntPtr.Zero)
            GtkNative.ObjectUnref(_icon);
        if (_handle.IsAllocated)
            _handle.Free();
    }

    /// <summary>
    /// 递归构建原生菜单树：分隔符 / 子菜单（set_submenu）/ 叶子项（可勾选 + 分配 activate 信号映射回
    /// <see cref="PopupMenu"/>）。菜单关闭（hide）时整体销毁并清映射。
    /// </summary>
    /// <param name="menu">菜单树。</param>
    /// <returns>GtkMenu 指针。</returns>
    private IntPtr BuildMenu(PopupMenu menu)
    {
        var gmenu = GtkNative.CreateMenu();
        foreach (var item in menu.Menus)
        {
            if (item.IsSeparator)
            {
                GtkNative.MenuAppend(gmenu, GtkNative.CreateSeparatorMenuItem());
                continue;
            }

            if (item.Menus.Count > 0)
            {
                var subItem = GtkNative.CreateMenuItem(item.Name);
                GtkNative.MenuItemSetSensitive(subItem, item.IsEnabled);
                GtkNative.MenuItemSetSubmenu(subItem, BuildMenu(item));
                GtkNative.MenuAppend(gmenu, subItem);
                continue;
            }

            var mitem = GtkNative.CreateCheckMenuItem(item.Name);
            GtkNative.CheckMenuItemSetActive(mitem, item.IsChecked);
            GtkNative.MenuItemSetSensitive(mitem, item.IsEnabled);
            var id = GtkNative.ConnectSignal(mitem, "activate", _menuItemTrampoline, _handle);
            _menuByWidget[mitem] = new MenuEntry(id, item);
            GtkNative.MenuAppend(gmenu, mitem);
        }
        return gmenu;
    }

    /// <summary>
    /// 断开并销毁当前弹出的菜单树（含全部菜单项 activate 信号与 hide 信号）。
    /// </summary>
    private void DestroyMenuTree()
    {
        var menu = _currentMenu;
        _currentMenu = IntPtr.Zero;
        if (menu == IntPtr.Zero)
            return;

        if (_menuHideId != 0)
        {
            GtkNative.DisconnectSignal(menu, _menuHideId);
            _menuHideId = 0;
        }
        foreach (var kv in _menuByWidget)
            GtkNative.DisconnectSignal(kv.Key, kv.Value.HandlerId);
        _menuByWidget.Clear();
        GtkNative.WidgetDestroy(menu);
    }

    /// <summary>
    /// activate 信号处理（主线程）：两次 activate 间隔在双击窗口内 → 双击并消费未定单击；
    /// 否则记时间戳并调度延迟上报单击（<see cref="OnClickTimeout"/>）。
    /// </summary>
    private void OnLeftActivated()
    {
        long now = Environment.TickCount64;
        if (_pendingClick && now - _lastActivateTicks <= DoubleClickWindowMs)
        {
            _pendingClick = false;
            CancelClickTimeout();
            DoubleClick?.Invoke(new TrayClickEvent(TrayClickType.Left, GetCursorPos()));
            return;
        }
        _lastActivateTicks = now;
        _pendingClick = true;
        _clickTimeoutId = GtkNative.AddTimeout((uint)DoubleClickWindowMs, _clickTimeoutTrampoline, GCHandle.ToIntPtr(_handle));
    }

    /// <summary>
    /// 取消未定的单击上报（双击消费或销毁时）。
    /// </summary>
    private void CancelClickTimeout()
    {
        if (_clickTimeoutId != 0)
        {
            GtkNative.SourceRemove(_clickTimeoutId);
            _clickTimeoutId = 0;
        }
    }

    /// <summary>
    /// 取当前鼠标屏幕坐标。
    /// </summary>
    private static Point2I GetCursorPos()
    {
        GtkNative.GetCursorPos(out int x, out int y);
        return new Point2I { X = x, Y = y };
    }

    /// <summary>
    /// 把托盘图标类型映射为通知类型。
    /// </summary>
    /// <param name="type">托盘图标类型。</param>
    private static NotificationType MapType(TrayIconType type) => type switch
    {
        TrayIconType.Warning => NotificationType.Warning,
        TrayIconType.Error => NotificationType.Error,
        _ => NotificationType.Info,
    };

    /// <summary>
    /// activate 信号 trampoline：左键单击/双击判定。
    /// </summary>
    private static void OnActivate(IntPtr statusIcon, IntPtr userData)
    {
        try
        {
            var tray = GCHandle.FromIntPtr(userData).Target as LinuxTrayIcon;
            if (tray is null || tray._deleted)
                return;
            tray.OnLeftActivated();
        }
        catch
        {
            // 托盘已销毁 / GCHandle 已释放等，忽略
        }
    }

    /// <summary>
    /// popup-menu 信号 trampoline：右键单击（Type=Right）上报 + 弹菜单。
    /// </summary>
    private static void OnPopupMenu(IntPtr statusIcon, IntPtr userData)
    {
        try
        {
            var tray = GCHandle.FromIntPtr(userData).Target as LinuxTrayIcon;
            if (tray is null || tray._deleted)
                return;
            tray.Click?.Invoke(new TrayClickEvent(TrayClickType.Right, GetCursorPos()));
            tray.ShowMenu();
        }
        catch
        {
            // 托盘已销毁 / GCHandle 已释放等，忽略
        }
    }

    /// <summary>
    /// 菜单项 activate 信号 trampoline：按菜单项指针查映射触发 <see cref="PopupMenu.OnClick"/>。
    /// </summary>
    private static void OnMenuItemActivate(IntPtr menuItem, IntPtr userData)
    {
        try
        {
            var tray = GCHandle.FromIntPtr(userData).Target as LinuxTrayIcon;
            if (tray is null || tray._deleted)
                return;
            if (tray._menuByWidget.TryGetValue(menuItem, out var entry))
                entry.Menu.OnClick();
        }
        catch
        {
            // 菜单已销毁 / GCHandle 已释放等，忽略
        }
    }

    /// <summary>
    /// 菜单 hide 信号 trampoline：菜单关闭后整体销毁（释放原生菜单树）。
    /// </summary>
    private static void OnMenuHide(IntPtr menu, IntPtr userData)
    {
        try
        {
            var tray = GCHandle.FromIntPtr(userData).Target as LinuxTrayIcon;
            tray?.DestroyMenuTree();
        }
        catch
        {
            // 菜单已销毁 / GCHandle 已释放等，忽略
        }
    }

    /// <summary>
    /// 单击延迟上报（g_timeout_add 在主循环执行）：未被双击消费则上报单击，一次性。
    /// </summary>
    private static int OnClickTimeout(IntPtr data)
    {
        try
        {
            var tray = GCHandle.FromIntPtr(data).Target as LinuxTrayIcon;
            if (tray is null || !tray._pendingClick)
                return 0;
            tray._pendingClick = false;
            tray._clickTimeoutId = 0;
            tray.Click?.Invoke(new TrayClickEvent(TrayClickType.Left, GetCursorPos()));
        }
        catch
        {
            // 托盘已销毁 / GCHandle 已释放等，忽略
        }
        return 0; // G_SOURCE_REMOVE：一次性
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void SignalActivateCallback(IntPtr instance, IntPtr userData);
}
