using System.Runtime.InteropServices;
using Xilium.CefGlue;

namespace CefDemo;

/// <summary>
/// simple_display_handler.c + simple_handler_win.c 移植：Alloy 风格下把页面标题设到窗口。
/// </summary>
internal sealed class SimpleDisplayHandler : CefDisplayHandler
{
    private readonly SimpleClient _parent;

    public SimpleDisplayHandler(SimpleClient parent) => _parent = parent;

    /// <summary>
    /// 标题变化：仅 Alloy 风格写窗口标题（display_handler_on_title_change）。
    /// </summary>
    /// <param name="browser">浏览器。</param>
    /// <param name="title">标题。</param>
    protected override void OnTitleChange(CefBrowser browser, string title)
    {
        if (!_parent.IsAlloyStyle)
            return;

        var hwnd = browser.GetHost().GetWindowHandle();
        if (hwnd != IntPtr.Zero)
            SetWindowTextW(hwnd, title);
    }

    /// <summary>
    /// 设置窗口标题（simple_handler_platform_title_change）。
    /// </summary>
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool SetWindowTextW(IntPtr hWnd, string lpString);
}
