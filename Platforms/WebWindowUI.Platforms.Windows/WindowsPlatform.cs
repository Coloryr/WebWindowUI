using Microsoft.Web.WebView2.Core;
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
    private static readonly Dictionary<IntPtr, WindowsWindow> _windows = [];
    private static CoreWebView2Environment _coreWebView2Environment;
    private static readonly Win32MessageLoop _message = new();

    private static readonly Lock _envLock = new();
    private static Task<CoreWebView2Environment>? _envTask;

    public WindowsPlatform()
    {
        _message.InitMessageLoop();
        // 注意：不在构造里创建 WebView2 环境。CreateAsync 需要在有消息循环的线程上等待完成
        //（后台/线程池无泵线程会挂起），且旧 async void InitWebView 在 STA 构造线程上会
        // RPC_E_CHANGED_MODE 崩溃。环境懒创建于首个 CreateCoreWebView2ControllerAsync，
        // 在 UI 线程（有泵）上直接 await——与拆分前 WindowsWindow.GetSharedEnvironmentAsync 一致。
    }

    /// <summary>
    /// 把动作 marshal 到 UI 线程同步执行：UI 线程直接运行；非 UI 线程经
    /// <see cref="MessageLoopSynchronizationContext.Send"/>（回 UI 线程并阻塞等待）。
    /// Win32 窗口 API（DestroyWindow/SetForegroundWindow/SetWindowTextW/SendMessage）都要求 UI 线程。
    /// </summary>
    public static void RunOnUiThread(Action action)
    {
        _message.RunOnUiThread(action);
    }

    public static bool IsUiThread()
    {
        return _message.IsUiThread();
    }

    public static async Task<CoreWebView2Controller> CreateCoreWebView2ControllerAsync(IntPtr hwnd)
    {
        // 环境懒创建（首个窗口触发，UI 线程有消息泵、CreateAsync 可完成）；await 就绪，
        // 杜绝 _coreWebView2Environment 空引用
        var environment = await GetEnvironmentAsync();
        var controller = await environment.CreateCoreWebView2ControllerAsync(hwnd);
        var core = controller.CoreWebView2;
        core.WebResourceRequested += OnWebResourceRequested;
        core.Settings.IsStatusBarEnabled = false;
        core.AddWebResourceRequestedFilter($"{WebWindowResource.Scheme}://*/*", CoreWebView2WebResourceContext.All);
        core.AddWebResourceRequestedFilter($"{WebWindowResource.SchemeData}://*/*", CoreWebView2WebResourceContext.All);

        return controller;
    }

    /// <summary>
    /// WebView2 环境单例（幂等）。跨窗口共享同一环境（自定义 scheme 只注册一次）。
    /// 必须在有消息循环的 UI 线程 await（CreateAsync 在无泵线程上会挂起）。
    /// </summary>
    private static Task<CoreWebView2Environment> GetEnvironmentAsync()
    {
        lock (_envLock)
        {
            return _envTask ??= CreateEnvironmentAsync();
        }
    }

    /// <summary>
    /// WebView2 环境工厂。CreateAsync 在调用线程（UI 线程，有泵）上执行、await 等待完成；
    /// 完成后回填 <c>_coreWebView2Environment</c>（OnWebResourceRequested 同步回调要用）。
    /// </summary>
    private static async Task<CoreWebView2Environment> CreateEnvironmentAsync()
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
        File.AppendAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "wwui_trace.txt"), $"{System.DateTime.Now:HH:mm:ss.fff} T{Environment.CurrentManagedThreadId} plat: CreateAsync begin\r\n");
        var environment = await CoreWebView2Environment.CreateAsync(null, null, options);
        File.AppendAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "wwui_trace.txt"), $"{System.DateTime.Now:HH:mm:ss.fff} T{Environment.CurrentManagedThreadId} plat: CreateAsync done\r\n");
        _coreWebView2Environment = environment;
        return environment;
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

    public IWindowBackend CreateWindow(WebWindowOptions options)
    {
        return new WindowsWindow(options);
    }

    public void RunMessageLoop()
    {
        _message.MessageLoop();
    }

    public static void WindowOpen(WindowsWindow window)
    {
        _windows[window.Hwnd] = window;
    }

    public static void WindowClose(WindowsWindow window)
    {
        _windows.Remove(window.Hwnd);

    }
}
