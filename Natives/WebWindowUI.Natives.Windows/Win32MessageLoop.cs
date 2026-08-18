using System.ComponentModel;
using System.Runtime.InteropServices;
using WebWindowUI.Core.Platform;

namespace WebWindowUI.Natives.Windows;

/// <summary>
/// Win32 消息循环：封装隐藏消息窗口的 WM_RUN 调度 + 窗口表 + UI 线程判断。
/// </summary>
public class Win32MessageLoop : IMessageLoop
{
    private static readonly Dictionary<IntPtr, Win32NativeWindow> _windows = [];

    /// <summary>
    /// 窗口过程入口：经 HWND 找到对应的窗口实例分派。
    /// </summary>
    private static IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        return _windows.TryGetValue(hwnd, out Win32NativeWindow? window)
                ? window.OnWndProc(msg, wParam, lParam)
                : Win32.DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    /// <summary>
    /// 注册窗口类。
    /// </summary>
    private static void InitWindowClass()
    {
        var wc = new WNDCLASSEXW
        {
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

    /// <summary>
    /// 窗口登记（按 HWND）。
    /// </summary>
    /// <param name="window">窗口实例。</param>
    internal static void WindowOpened(Win32NativeWindow window)
    {
        _windows[window.WindowHandle] = window;
    }

    /// <summary>
    /// 窗口注销；最后一个窗口关闭时投递退出消息。
    /// </summary>
    /// <param name="window">窗口实例。</param>
    internal static void WindowClose(Win32NativeWindow window)
    {
        _windows.Remove(window.WindowHandle);
        if (_windows.Count == 0)
        {
            Win32.PostQuitMessage(0);
        }
    }

    /// <summary>
    /// 初始化消息循环：建隐藏消息窗口、绑 SC、注册窗口类。
    /// </summary>
    public void InitMessageLoop()
    {
        Win32.SetMarshalMessageHandler(HandleMarshalMessage);
        var marshalHwnd = Win32.GetOrCreateMarshalWindow("WebView2MarshalWindow");
        MessageLoopSynchronizationContext.Initialize(marshalHwnd);
        SynchronizationContext.SetSynchronizationContext(MessageLoopSynchronizationContext.Instance);

        InitWindowClass();
    }

    /// <summary>
    /// 运行消息循环，直到退出。
    /// </summary>
    public void MessageLoop()
    {
        Win32.MessageLoop();
    }

    /// <summary>
    /// 运行模态消息循环：泵消息直到 <paramref name="isDone"/> 返回 true 或收到 WM_QUIT（模态对话框专用）。
    /// </summary>
    /// <param name="isDone">窗口是否已关闭（每轮消息后检查）。</param>
    public void RunModalLoop(Func<bool> isDone)
    {
        while (!isDone())
        {
            if (Win32.GetMessageW(out MSG msg, IntPtr.Zero, 0, 0) == 0)
                break; // WM_QUIT
            Win32.TranslateMessage(ref msg);
            Win32.DispatchMessageW(ref msg);
        }
    }

    /// <summary>
    /// 当前线程是否 UI 线程。
    /// </summary>
    /// <returns>是否 UI 线程。</returns>
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
