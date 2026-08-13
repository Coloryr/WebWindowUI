using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;
using WebWindowUI.Core;

namespace WebWindowUI.Natives.Windows;

/// <summary>
/// Win32 裸窗口：封装 HWND 生命周期（创建/显示/销毁），经窗口过程路由回框架事件。
/// </summary>
public class Win32NativeWindow : INativeWindow
{
    /// <summary>
    /// 注册的窗口类名。
    /// </summary>
    public const string WindowClass = "WebView2Window";

    private IntPtr _hIcon;

    private readonly IntPtr _hwnd;

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
    /// 获取客户区尺寸。
    /// </summary>
    /// <returns>客户区矩形。</returns>
    public Rectangle GetSize()
    {
        Win32.GetClientRect(_hwnd, out Win32.RECT rc);
        return new Rectangle(0, 0, rc.Right, rc.Bottom);
    }

    /// <summary>
    /// 窗口过程：分发 WM_CLOSE/WM_DESTROY/WM_SIZE，其余走默认处理。
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
                return IntPtr.Zero;

            default:
                return Win32.DefWindowProcW(_hwnd, msg, wParam, lParam);
        }
    }
}
