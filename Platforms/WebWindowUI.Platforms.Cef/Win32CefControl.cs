using WebWindowUI.Core;
using WebWindowUI.Natives.Windows;
using Xilium.CefGlue;
using Xilium.CefGlue.Common.Helpers;
using Xilium.CefGlue.Common.Platform;

namespace WebWindowUI.Platforms.Cef;

/// <summary>
/// 窗口模式 IControl：**隐藏宿主 + 重挂载**（对齐 CefGlue.Avalonia）——浏览器先作为隐藏窗口的子窗口创建，
/// InitializeRender 时 SetParent 重挂载进可见顶层窗口并铺满客户区。这样 GetWindowHandle() 返回的浏览器句柄
/// 不被直接嵌入可见窗口，SetAsPopup 的 DevTools 弹窗才能成为独立顶层窗口（直接嵌可见窗口会即开即关/顶替内容）。
/// </summary>
internal sealed class Win32CefControl : IControl
{
    /// <summary>
    /// 被适配的 Win32 顶层窗口（可见，Attach 后可用）。
    /// </summary>
    private INativeWindow? _nativeWindow;

    /// <summary>
    /// 隐藏宿主窗口（浏览器先挂这里，InitializeRender 时重挂载到可见窗口）。
    /// </summary>
    private IntPtr _hiddenHost;

    /// <summary>
    /// 控件获得焦点（浏览器子窗口随顶层窗口一起聚焦，无独立焦点事件）。
    /// </summary>
    public event Action? GotFocus;

    /// <summary>
    /// 控件尺寸变化（首个尺寸触发浏览器创建）。
    /// </summary>
    public event Action<CefSize>? SizeChanged;

    /// <summary>
    /// 绑定可见窗口、创建隐藏宿主窗口并订阅尺寸变化（派生 ctor 里调用，基类 ctor 后）。
    /// </summary>
    /// <param name="nativeWindow">可见宿主 Win32 窗口。</param>
    public void Attach(INativeWindow nativeWindow)
    {
        _nativeWindow = nativeWindow;
        nativeWindow.Resize += NotifySize;
        // 隐藏宿主：普通隐藏顶层窗口（浏览器 SetAsChild 到这里，避免直接嵌入可见窗口）
        _hiddenHost = Win32BrowserHost.CreateHiddenHost();
    }

    /// <summary>
    /// 触发尺寸通知（窗口显示/调整后调用，首个尺寸创建浏览器）。
    /// </summary>
    public void NotifySize()
    {
        if (_nativeWindow is null)
            return;
        var rc = _nativeWindow.GetSize();
        SizeChanged?.Invoke(new CefSize(rc.Width, rc.Height));
    }

    /// <summary>
    /// 浏览器子窗口的宿主视图句柄：返回**隐藏宿主窗口**（浏览器先在隐藏窗口里创建）。
    /// </summary>
    /// <param name="initialWidth">初始宽度。</param>
    /// <param name="initialHeight">初始高度。</param>
    /// <returns>隐藏宿主窗口句柄。</returns>
    public IntPtr? GetHostViewHandle(int initialWidth, int initialHeight) => _hiddenHost;

    /// <summary>
    /// 渲染挂载：把浏览器 HWND 重挂载（SetParent）进可见顶层窗口并铺满客户区（marshal 到主线程）。
    /// </summary>
    /// <param name="browserHandle">浏览器窗口句柄。</param>
    public void InitializeRender(IntPtr browserHandle)
    {
        WebWindowPlatform.Current.RunOnUiThread(() =>
        {
            if (_nativeWindow is not { } native)
                return;
            var rc = native.GetSize();
            Win32BrowserHost.Reparent(browserHandle, native.WindowHandle, rc.Width, rc.Height);
        });
    }

    /// <summary>
    /// 渲染销毁通知（浏览器 HWND 由 CEF 销毁，无需处理）。
    /// </summary>
    public void DestroyRender() { }

    /// <summary>
    /// 上下文菜单：暂不实现（回调取消）。
    /// </summary>
    /// <param name="menuEntries">菜单项。</param>
    /// <param name="x">X 坐标。</param>
    /// <param name="y">Y 坐标。</param>
    /// <param name="callback">菜单回调。</param>
    public void OpenContextMenu(IEnumerable<MenuEntry> menuEntries, int x, int y, CefRunContextMenuCallback callback)
        => callback?.Cancel();

    /// <summary>
    /// 关闭上下文菜单（无实现）。
    /// </summary>
    public void CloseContextMenu() { }

    /// <summary>
    /// 设置工具提示（无实现）。
    /// </summary>
    /// <param name="text">提示文本。</param>
    public void SetTooltip(string text) { }

    /// <summary>
    /// 设置光标（不接管，返回 false 让 CEF 用默认）。
    /// </summary>
    /// <param name="cursorHandle">光标句柄。</param>
    /// <param name="cursorType">光标类型。</param>
    /// <returns>是否接管。</returns>
    public bool SetCursor(IntPtr cursorHandle, CefCursorType cursorType) => false;

    /// <summary>
    /// 是否持有键盘焦点（浏览器子窗口聚焦即视为有焦点）。
    /// </summary>
    /// <returns>恒 true。</returns>
    public bool HasKeyboardFocus() => true;
}
