using WebWindowUI.Natives.Windows;
using Xilium.CefGlue;
using Xilium.CefGlue.Common;
using Xilium.CefGlue.Common.Helpers;
using Xilium.CefGlue.Common.Platform;

namespace WebWindowUI.Platforms.Cef;

/// <summary>
/// IControl 实现：浏览器先作为隐藏宿主窗口子窗口创建（GetHostViewHandle），
/// 初始化完成后把浏览器 HWND 重挂载进目标可见窗口并铺满（对齐 CefGlue.Avalonia）。
/// 上下文菜单/光标/工具提示最小实现。
/// </summary>
internal sealed class Win32CefControl : IControl
{
    /// <summary>
    /// 隐藏宿主窗口（浏览器初始父窗口）。
    /// </summary>
    private IntPtr? _hiddenHost;

    /// <summary>
    /// 浏览器窗口句柄（InitializeRender 后有效）。
    /// </summary>
    private IntPtr? _browserHandle;

    /// <summary>
    /// 目标可见窗口（重挂载目标）。
    /// </summary>
    private IntPtr _targetWindow;

    /// <summary>
    /// 当前尺寸。
    /// </summary>
    private int _width;

    /// <summary>
    /// 当前高度。
    /// </summary>
    private int _height;

    /// <summary>
    /// 控件获得焦点事件（Win32 非 OSR，不触发）。
    /// </summary>
    public event Action? GotFocus;

    /// <summary>
    /// 控件尺寸变化事件（适配器监听，首次尺寸触发浏览器创建）。
    /// </summary>
    public event Action<CefSize>? SizeChanged;

    /// <summary>
    /// 创建隐藏宿主窗口（浏览器初始父窗口）。
    /// </summary>
    /// <param name="initialWidth">初始宽度。</param>
    /// <param name="initialHeight">初始高度。</param>
    /// <returns>隐藏宿主窗口句柄。</returns>
    public IntPtr? GetHostViewHandle(int initialWidth, int initialHeight)
    {
        _width = initialWidth;
        _height = initialHeight;
        _hiddenHost = Win32BrowserHost.CreateHiddenHost();
        return _hiddenHost;
    }

    /// <summary>
    /// 设置目标可见窗口并同步初始尺寸（尺寸变化触发 SizeChanged → 首次创建浏览器）。
    /// </summary>
    /// <param name="targetWindow">可见窗口句柄。</param>
    /// <param name="width">宽度。</param>
    /// <param name="height">高度。</param>
    public void SetTarget(IntPtr targetWindow, int width, int height)
    {
        _targetWindow = targetWindow;
        SetSize(width, height);
    }

    /// <summary>
    /// 更新尺寸：已初始化则铺满浏览器；尺寸变化触发 SizeChanged（首次创建浏览器）。
    /// </summary>
    /// <param name="width">宽度。</param>
    /// <param name="height">高度。</param>
    public void SetSize(int width, int height)
    {
        if (width == _width && height == _height)
            return;
        _width = width;
        _height = height;
        if (_browserHandle is { } handle)
        {
            Win32BrowserHost.Resize(handle, width, height);
        }
        SizeChanged?.Invoke(new CefSize(width, height));
    }

    /// <summary>
    /// 重挂载浏览器进目标窗口（窗口显示/恢复时调用）。
    /// </summary>
    public void Reapply()
    {
        if (_browserHandle is { } handle && _targetWindow != IntPtr.Zero)
        {
            Win32BrowserHost.Reparent(handle, _targetWindow, _width, _height);
        }
    }

    /// <summary>
    /// 浏览器初始化完成：把浏览器 HWND 重挂载进目标可见窗口。
    /// </summary>
    /// <param name="browserHandle">浏览器窗口句柄。</param>
    public void InitializeRender(IntPtr browserHandle)
    {
        _browserHandle = browserHandle;
        Reapply();
    }

    /// <summary>
    /// 销毁渲染：销毁隐藏宿主窗口。
    /// </summary>
    public void DestroyRender()
    {
        if (_hiddenHost is { } host)
        {
            Win32BrowserHost.Destroy(host);
            _hiddenHost = null;
        }
    }

    /// <summary>
    /// 打开上下文菜单（最小实现，取消默认菜单）。
    /// </summary>
    /// <param name="menuEntries">菜单项。</param>
    /// <param name="x">X。</param>
    /// <param name="y">Y。</param>
    /// <param name="callback">回调。</param>
    public void OpenContextMenu(IEnumerable<MenuEntry> menuEntries, int x, int y, CefRunContextMenuCallback callback)
        => callback.Cancel();

    /// <summary>
    /// 关闭上下文菜单（无操作）。
    /// </summary>
    public void CloseContextMenu()
    {
    }

    /// <summary>
    /// 设置工具提示（无操作）。
    /// </summary>
    /// <param name="text">提示文本。</param>
    public void SetTooltip(string text)
    {
    }

    /// <summary>
    /// 设置光标（返回 false 交默认处理）。
    /// </summary>
    /// <param name="cursorHandle">光标句柄。</param>
    /// <param name="cursorType">光标类型。</param>
    /// <returns>是否已处理。</returns>
    public bool SetCursor(IntPtr cursorHandle, CefCursorType cursorType) => false;

    /// <summary>
    /// 是否持有键盘焦点（Win32 非 OSR，返回 false）。
    /// </summary>
    /// <returns>是否聚焦。</returns>
    public bool HasKeyboardFocus() => false;
}
