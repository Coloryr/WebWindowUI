using System.ComponentModel;
using System.Runtime.InteropServices;
using WebWindowUI.Core;

namespace WebWindowUI.Natives.Windows;

public class Win32MessageLoop : IMessageLoop
{
    private static readonly Dictionary<IntPtr, Win32NativeWindow> _windows = [];

    /// <summary>
    /// 窗口过程入口：通过 HWND 找到对应的窗口实例。
    /// </summary>
    private static IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        return _windows.TryGetValue(hwnd, out Win32NativeWindow? window)
                ? window.OnWndProc(msg, wParam, lParam)
                : Win32.DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    private static void InitWindowClass()
    {
        var wc = new Win32.WNDCLASSEXW
        {
            cbSize = (uint)Marshal.SizeOf<Win32.WNDCLASSEXW>(),
            style = Win32.CS_HREDRAW | Win32.CS_VREDRAW,
            lpfnWndProc = WndProc,
            hInstance = Win32.GetModuleHandleW(null),
            hIcon = Win32.LoadIconW(IntPtr.Zero, Win32.IDI_APPLICATION),
            hCursor = Win32.LoadCursorW(IntPtr.Zero, Win32.IDC_ARROW),
            hbrBackground = Win32.COLOR_WINDOW + 1,
            lpszMenuName = null,
            lpszClassName = Win32NativeWindow.WindowClass,
        };
        if (Win32.RegisterClassExW(ref wc) == 0)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "注册窗口类失败 (RegisterClassExW)");
    }

    private static IntPtr? HandleMarshalMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == Win32.WM_RUN)
        {
            MessageLoopSynchronizationContext.Instance.RunQueued();
            return IntPtr.Zero;
        }
        return null;
    }

    internal static void WindowOpened(Win32NativeWindow window)
    {
        _windows[window.WindowHandle] = window;
    }

    internal static void WindowClose(Win32NativeWindow window)
    {
        _windows.Remove(window.WindowHandle);
        if (_windows.Count == 0)
        {
            Win32.PostQuitMessage(0);
        }
    }

    public void InitMessageLoop()
    {
        Win32.SetMarshalMessageHandler(HandleMarshalMessage);
        var marshalHwnd = Win32.GetOrCreateMarshalWindow("WebView2MarshalWindow");
        MessageLoopSynchronizationContext.Initialize(marshalHwnd);
        SynchronizationContext.SetSynchronizationContext(MessageLoopSynchronizationContext.Instance);

        InitWindowClass();
    }

    public void MessageLoop()
    {
        Win32.MessageLoop();
    }

    public bool IsUiThread()
    {
        return Environment.CurrentManagedThreadId == MessageLoopSynchronizationContext.UiThreadId;
    }

    /// <summary>
    /// 把动作 marshal 到 UI 线程同步执行：UI 线程直接运行；非 UI 线程经
    /// <see cref="MessageLoopSynchronizationContext.Send"/>（回 UI 线程并阻塞等待）。
    /// Win32 窗口 API（DestroyWindow/SetForegroundWindow/SetWindowTextW/SendMessage）都要求 UI 线程。
    /// </summary>
    public void RunOnUiThread(Action action)
    {
        if (IsUiThread())
        {
            action();
            return;
        }
        MessageLoopSynchronizationContext.Instance.Send(_ => action(), null);
    }
}
