using System.Runtime.InteropServices;

namespace WebWindowUI.Natives.Windows;

/// <summary>
/// CEF 浏览器宿主辅助：隐藏宿主窗口 + 子窗口重挂载（浏览器先作为隐藏窗口子窗口创建，
/// 再重挂载进可见窗口——对齐 CefGlue.Avalonia，避免 DevTools 弹窗即开即关）。
/// Win32 保持 internal，公开所需操作。
/// </summary>
public static class Win32BrowserHost
{
    /// <summary>
    /// 隐藏宿主窗口类名。
    /// </summary>
    private const string HiddenHostClass = "WebWindowUI_CefHiddenHost";

    /// <summary>
    /// 是否已注册隐藏宿主窗口类。
    /// </summary>
    private static bool _classRegistered;

    /// <summary>
    /// 隐藏宿主窗口默认 WndProc（不处理消息，交给系统）。
    /// </summary>
    /// <param name="hwnd">窗口句柄。</param>
    /// <param name="msg">消息。</param>
    /// <param name="wParam">消息参数。</param>
    /// <param name="lParam">消息参数。</param>
    /// <returns>处理结果。</returns>
    private static IntPtr HiddenHostWndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
        => Win32.DefWindowProcW(hwnd, msg, wParam, lParam);

    /// <summary>
    /// 创建隐藏宿主窗口（完整隐藏顶层窗口，浏览器 SetAsChild 到这里）。
    /// </summary>
    /// <returns>隐藏宿主窗口句柄。</returns>
    public static IntPtr CreateHiddenHost()
    {
        if (!_classRegistered)
        {
            var wc = new Win32.WNDCLASSEXW
            {
                cbSize = (uint)Marshal.SizeOf<Win32.WNDCLASSEXW>(),
                style = 0,
                lpfnWndProc = HiddenHostWndProc,
                hInstance = Win32.GetModuleHandleW(null),
                lpszClassName = HiddenHostClass,
            };
            if (Win32.RegisterClassExW(ref wc) == 0 && Marshal.GetLastWin32Error() != 1410 /*ERROR_CLASS_ALREADY_EXISTS*/)
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "注册隐藏宿主窗口类失败 (RegisterClassExW)");
            _classRegistered = true;
        }

        return Win32.CreateWindowExW(
            0, HiddenHostClass, "",
            unchecked((int)0x80000000) /*WS_POPUP*/, 0, 0, 1, 1,
            IntPtr.Zero, IntPtr.Zero, Win32.GetModuleHandleW(null), IntPtr.Zero);
    }

    /// <summary>
    /// 把子窗口重挂载到新父窗口并铺满其客户区。
    /// </summary>
    /// <param name="child">要重挂载的子窗口句柄。</param>
    /// <param name="newParent">新父窗口句柄。</param>
    /// <param name="width">铺满宽度。</param>
    /// <param name="height">铺满高度。</param>
    /// <returns>重挂载前的旧父窗口句柄。</returns>
    public static IntPtr Reparent(IntPtr child, IntPtr newParent, int width, int height)
    {
        var oldParent = Win32.SetParent(child, newParent);
        Win32.MoveWindow(child, 0, 0, width, height, true);
        return oldParent;
    }
}
