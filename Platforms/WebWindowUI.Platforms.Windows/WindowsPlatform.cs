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
    private static CoreWebView2Environment _coreWebView2Environment;
    private static readonly Win32MessageLoop _message = new();

    public WindowsPlatform()
    {
        _message.InitMessageLoop();
        CreateEnvironment();
    }

    /// <summary>
    /// 把动作 marshal 到 UI 线程同步执行：UI 线程直接运行；非 UI 线程经
    /// <see cref="MessageLoopSynchronizationContext.Send"/>（回 UI 线程并阻塞等待）。
    /// Win32 窗口 API（DestroyWindow/SetForegroundWindow/SetWindowTextW/SendMessage）都要求 UI 线程。
    /// </summary>
    public void RunOnUiThread(Action action)
    {
        _message.RunOnUiThread(action);
    }

    public bool IsUiThread()
    {
        return _message.IsUiThread();
    }

    internal static async Task<CoreWebView2Controller> CreateCoreWebView2ControllerAsync(IntPtr hwnd)
    {
        while (_coreWebView2Environment == null)
        {
            await Task.Delay(100);
        }
        var controller = await _coreWebView2Environment.CreateCoreWebView2ControllerAsync(hwnd);
        var core = controller.CoreWebView2;
        core.WebResourceRequested += OnWebResourceRequested;
        core.Settings.IsStatusBarEnabled = false;
        core.AddWebResourceRequestedFilter($"{WebWindowResource.Scheme}://*/*", CoreWebView2WebResourceContext.All);
        core.AddWebResourceRequestedFilter($"{WebWindowResource.SchemeData}://*/*", CoreWebView2WebResourceContext.All);

        return controller;
    }

    /// <summary>
    /// WebView2 环境工厂。CreateAsync 在调用线程（UI 线程，有泵）上执行、await 等待完成；
    /// 完成后回填 <c>_coreWebView2Environment</c>（OnWebResourceRequested 同步回调要用）。
    /// </summary>
    private static async void CreateEnvironment()
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
        var environment = await CoreWebView2Environment.CreateAsync(null, null, options);
        _coreWebView2Environment = environment;
    }

    /// <summary>
    /// 网页内容请求
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="args"></param>
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

    /// <summary>
    /// 创建窗口
    /// </summary>
    /// <param name="options">创建参数</param>
    /// <returns>WebView窗口</returns>
    public IWindowBackend CreateWindow(WebWindowOptions options)
    {
        return new WindowsWindow(options);
    }

    public void RunMessageLoop()
    {
        _message.MessageLoop();
    }

    public void ShowMessageBox(string title, string message, bool error)
        => Win32Native.ShowMessage(title, message, error);

    public string[]? OpenFileDialog(string title, string filter, string? initialDirectory = null, bool fileMustExist = true, bool allowMultiSelect = true)
        => Win32Native.OpenFileDialog(title, filter, initialDirectory, fileMustExist, allowMultiSelect)?.ToArray();

    public string? SaveFileDialog(string title, string filter, string? defaultFileName = null, string? defaultExt = null)
        => Win32Native.SaveFileDialog(title, filter, defaultFileName, defaultExt);
}
