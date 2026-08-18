using System.Runtime.InteropServices;
using WebWindowUI.Core.Platform;

namespace WebWindowUI.Natives.Windows;

/// <summary>
/// Win32 系统通知：Shell_NotifyIcon 的 NIF_INFO 气泡（不显示托盘图标），点击经隐藏消息窗口
/// 的 NIN_BALLOONUSERCLICK 回调触发 <see cref="Clicked"/>。单例，进程内共享。
/// </summary>
public class Win32Notification : INotification
{
    /// <summary>
    /// 单例。
    /// </summary>
    public static readonly Win32Notification Instance = new();

    private const string WindowClass = "WebWindowUINotifyWindow";
    private const uint NotifyId = 0x1002;

    private static WndProcDelegate _wndProc; // 保活：native 只持有函数指针，委托须被强引用

    private readonly IntPtr _hwnd;
    private bool _added;

    /// <summary>
    /// 通知被点击时触发（在隐藏窗口线程回调）。
    /// </summary>
    public event Action? Clicked;

    /// <summary>
    /// 注册通知窗口类并创建隐藏消息窗口（HWND_MESSAGE，不显示）。
    /// </summary>
    public Win32Notification()
    {
        _wndProc = WndProc;
        var wc = new WNDCLASSEXW
        {
            cbSize = Marshal.SizeOf<WNDCLASSEXW>(),
            lpfnWndProc = _wndProc,
            hInstance = Win32.GetModuleHandleW(null),
            lpszClassName = WindowClass,
        };
        Win32.RegisterClassExW(ref wc);

        _hwnd = Win32.CreateWindowExW(
            0, WindowClass, "", 0,
            0, 0, 0, 0,
            (IntPtr)Win32.HWND_MESSAGE, IntPtr.Zero, Win32.GetModuleHandleW(null), IntPtr.Zero);
    }

    /// <summary>
    /// 显示气泡通知（首次 NIM_ADD + 设版本 4，之后 NIM_MODIFY 刷新内容）。
    /// </summary>
    /// <param name="title">标题（最多 63 字符）。</param>
    /// <param name="text">内容（最多 255 字符）。</param>
    /// <param name="type">通知样式。</param>
    public void Show(string title, string text, NotificationType type = NotificationType.Info)
    {
        uint flag = type switch
        {
            NotificationType.Warning => Win32.NIIF_WARNING,
            NotificationType.Error => Win32.NIIF_ERROR,
            _ => Win32.NIIF_INFO,
        };
        var nid = new NOTIFYICONDATA
        {
            cbSize = NOTIFYICONDATA.Size,
            hWnd = _hwnd,
            uID = NotifyId,
            uFlags = Win32.NIF_MESSAGE | Win32.NIF_INFO,
            uCallbackMessage = Win32.WM_TRAYICON,
            szInfo = text,
            szInfoTitle = title,
            dwInfoFlags = flag,
        };

        if (Win32.Shell_NotifyIcon(_added ? Win32.NIM_MODIFY : Win32.NIM_ADD, in nid) && !_added)
        {
            _added = true;
            nid.uVersion = Win32.NOTIFYICON_VERSION_4;
            Win32.Shell_NotifyIcon(Win32.NIM_SETVERSION, in nid);
        }
    }

    /// <summary>
    /// 关闭当前通知（未显示时无操作）。
    /// </summary>
    public void Close()
    {
        if (!_added)
            return;
        _added = false;

        var nid = new NOTIFYICONDATA
        {
            cbSize = NOTIFYICONDATA.Size,
            hWnd = _hwnd,
            uID = NotifyId,
        };
        Win32.Shell_NotifyIcon(Win32.NIM_DELETE, in nid);
    }

    /// <summary>
    /// 通知窗口过程：WM_TRAYICON（wParam=通知 id）下，lParam 为 NIN_BALLOONUSERCLICK（点击气泡）
    /// 或 WM_LBUTTONUP（点击图标）时触发 <see cref="Clicked"/>。
    /// </summary>
    private IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == Win32.WM_TRAYICON && (uint)wParam == NotifyId)
        {
            uint evt = (uint)lParam;
            if (evt == Win32.NIN_BALLOONUSERCLICK || evt == Win32.WM_LBUTTONUP)
                Clicked?.Invoke();
            return IntPtr.Zero;
        }
        return Win32.DefWindowProcW(hwnd, msg, wParam, lParam);
    }
}
