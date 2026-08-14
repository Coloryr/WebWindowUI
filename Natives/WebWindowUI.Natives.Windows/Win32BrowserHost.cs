namespace WebWindowUI.Natives.Windows;

/// <summary>
/// CEF 浏览器宿主辅助：隐藏宿主窗口 + 子窗口重挂载（浏览器先作为隐藏窗口子窗口创建，
/// 再重挂载进可见窗口——对齐 CefGlue.Avalonia，避免 DevTools 弹窗即开即关）。Win32 保持 internal，公开所需操作。
/// </summary>
public static class Win32BrowserHost
{
    /// <summary>
    /// 创建隐藏宿主窗口（普通隐藏顶层窗口，浏览器 SetAsChild 到这里）。
    /// </summary>
    /// <returns>隐藏宿主窗口句柄。</returns>
    public static IntPtr CreateHiddenHost()
        => Win32.CreateWindowExW(0, "STATIC", "", unchecked((int)0x80000000) /*WS_POPUP*/, 0, 0, 1, 1, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);

    /// <summary>
    /// 把子窗口重挂载到新父窗口并铺满其客户区。
    /// </summary>
    /// <param name="child">要重挂载的子窗口句柄。</param>
    /// <param name="newParent">新父窗口句柄。</param>
    /// <param name="width">铺满宽度。</param>
    /// <param name="height">铺满高度。</param>
    public static void Reparent(IntPtr child, IntPtr newParent, int width, int height)
    {
        Win32.SetParent(child, newParent);
        Win32.MoveWindow(child, 0, 0, width, height, true);
    }
}
