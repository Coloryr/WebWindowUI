using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;
using WebWindowUI.Core;

namespace WebWindowUI.Natives.Windows;

public class Win32NativeWindow : INativeWindow
{
    public const string WindowClass = "WebView2Window";

    private IntPtr _hIcon;

    private readonly IntPtr _hwnd;

    public IntPtr WindowHandle => _hwnd;

    public event Action? Destory;
    public event Action? Resize;

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

    public void Show()
    {
        Win32.ShowWindow(_hwnd, Win32.SW_SHOW);
    }

    public void Hide()
    {
        Win32.ShowWindow(_hwnd, Win32.SW_HIDE);
    }

    public void Close()
    {
        Win32.DestroyWindow(_hwnd);
    }

    public void Activate()
    {
        if (Win32.IsIconic(_hwnd))
            Win32.ShowWindow(_hwnd, Win32.SW_RESTORE);
        Win32.SetForegroundWindow(_hwnd);
        Win32.SetFocus(_hwnd);
    }

    public void SetTitle(string title)
    {
        Win32.SetWindowTextW(_hwnd, title);
    }

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

    public Rectangle GetSize()
    {
        Win32.GetClientRect(_hwnd, out Win32.RECT rc);
        return new Rectangle(0, 0, rc.Right, rc.Bottom);
    }

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
