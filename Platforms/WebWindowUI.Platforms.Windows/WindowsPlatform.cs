using Microsoft.Web.WebView2.Core;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using WebWindowUI.Core;
using WebWindowUI.Natives.Windows;

namespace WebWindowUI.Platforms.Windows;

/// <summary>
/// Windows 平台的实现：WebView2 + Win32 消息循环。
/// 消息窗口、SynchronizationContext 的初始化都封装在这里，调用方无需接触 Win32。
/// </summary>
public sealed class WindowsPlatform : IWebWindowPlatform
{
    public const string WindowClass = "WebView2Window";

    private static readonly Dictionary<IntPtr, WindowsWindow> _windows = [];
    private static CoreWebView2Environment _coreWebView2Environment;

    public WindowsPlatform()
    {
        InitMessageLoopSynchronizationContext();
        InitWebView();
        InitWindowClass();
    }

    public static async Task<CoreWebView2Controller> CreateCoreWebView2ControllerAsync(IntPtr hwnd)
    {
        var controller = await _coreWebView2Environment.CreateCoreWebView2ControllerAsync(hwnd);
        var core = controller.CoreWebView2;
        core.WebResourceRequested += OnWebResourceRequested;
        core.Settings.IsStatusBarEnabled = false;
        core.AddWebResourceRequestedFilter($"{WebWindowResource.Scheme}://*/*", CoreWebView2WebResourceContext.All);
        core.AddWebResourceRequestedFilter($"{WebWindowResource.SchemeData}://*/*", CoreWebView2WebResourceContext.All);

        return controller;
    }

    private static void OnWebResourceRequested(object? sender, CoreWebView2WebResourceRequestedEventArgs args)
    {
        try
        {
            if (WebWindowResource.TryResolvePath(args.Request.Uri, out string? relative, out string? mimeType) is { } stream)
            {
                string headers =
                    $"Content-Type: {mimeType}\r\n" +
                    $"Cache-Control: {ResourceHeaders.CacheControl(relative!)}\r\n" +
                    $"{ResourceHeaders.AccessControlAllowOrigin}\r\n" +
                    $"\r\n"; 

                args.Response = _coreWebView2Environment.CreateWebResourceResponse(
                    stream,
                    200,
                    "OK",
                    headers
                );
                return;
            }
        }
        catch
        {
            
        }

        var notFound = new MemoryStream(Encoding.UTF8.GetBytes("404 Not Found"));
        args.Response = _coreWebView2Environment.CreateWebResourceResponse(
            notFound,
            404,
            "Not Found",
            $"Content-Type: text/plain\r\n" +
            $"Cache-Control: no-store\r\n" +
            $"{ResourceHeaders.AccessControlAllowOrigin}" +
            $"\r\n");
    }

    public static async void InitWebView()
    {
        var registrations = new List<CoreWebView2CustomSchemeRegistration>
        {
            new(WebWindowResource.Scheme)
            {
                HasAuthorityComponent = true,
                TreatAsSecure = true,
                AllowedOrigins = { "*" },
            },
            new(WebWindowResource.SchemeData)
            {
                HasAuthorityComponent = true,
                TreatAsSecure = true,
                AllowedOrigins = { "*" },
            },
        };

        var options = new CoreWebView2EnvironmentOptions(customSchemeRegistrations: registrations);
        _coreWebView2Environment = await CoreWebView2Environment.CreateAsync(null, null, options);
    }

    /// <summary>
    /// 窗口过程入口：通过 HWND 找到对应的窗口实例。
    /// </summary>
    private static IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        return _windows.TryGetValue(hwnd, out WindowsWindow? window)
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
            lpszClassName = WindowClass,
        };
        if (Win32.RegisterClassExW(ref wc) == 0)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "注册窗口类失败 (RegisterClassExW)");
    }

    public IWindowBackend CreateWindow(WebWindowOptions options)
    {
        var hwnd = Win32.CreateWindowExW(
            0, WindowClass, options.Title, Win32.WS_OVERLAPPEDWINDOW,
            Win32.CW_USEDEFAULT, Win32.CW_USEDEFAULT, options.Width, options.Height,
            IntPtr.Zero, IntPtr.Zero, Win32.GetModuleHandleW(null), IntPtr.Zero);
        if (hwnd == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "创建窗口失败 (CreateWindowExW)");

        return new WindowsWindow(hwnd, options);
    }

    public void RunMessageLoop()
    {
        // 隐藏消息窗口：所有 async 延续都通过它调度回 UI 线程。
        // 构造里已装过一次，这里是幂等兜底（覆盖未经过窗口创建直接调消息循环的宿主）。
        InitMessageLoopSynchronizationContext();

        // Win32 消息循环，收到 WM_QUIT（最后一个窗口关闭）后返回
        Win32.MessageLoop();
    }

    private static void InitMessageLoopSynchronizationContext()
    {
        Win32.SetMarshalMessageHandler(HandleMarshalMessage);
        var marshalHwnd = Win32.GetOrCreateMarshalWindow("WebView2MarshalWindow");
        MessageLoopSynchronizationContext.Initialize(marshalHwnd);
        SynchronizationContext.SetSynchronizationContext(MessageLoopSynchronizationContext.Instance);
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

    public static void WindowOpen(WindowsWindow window)
    {
        _windows[window.Hwnd] = window;
    }

    public static void WindowClose(WindowsWindow window)
    {
        _windows.Remove(window.Hwnd);
        WebWindow.NotifyWindowClosed();
        if (WebWindow.OpenCount == 0)
            Win32.PostQuitMessage(0); // 最后一个窗口关闭，退出消息循环

    }
}
