using WebWindowUI.Core;

namespace WebWindowUI.Platforms.Linux;

/// <summary>
/// Linux 平台实现：GTK3 宿主 + libwebkit2gtk-4.1（webkit2gtk-4.1 是 GTK3 端口；WebKit/GTK 均为手写
/// P/Invoke，见 Native/WebKit2Native.cs + Native/GtkNative.cs）。用 GLib.MainLoop 跑主循环（不用
/// Gtk.Application），契合本框架「创建窗口 → Show → 再 RunMessageLoop」的模型，也避开 Gtk.Application
/// 的 D-Bus 唯一实例限制。
/// </summary>
public sealed class LinuxPlatform : IWebWindowPlatform
{
    private static readonly Dictionary<IntPtr, LinuxWindow> _windows = [];
    private static readonly WebKit2Native.WebKitUriSchemeRequestCallback _schemeCallback = OnUriSchemeRequest;

    private static MainLoop? _mainLoop;

    public LinuxPlatform()
    {
        Module.Initialize();

        GtkNative.Initialize();
        WebKit2Native.Initialize();

        RegisterUriScheme(WebWindowResource.Scheme);
        RegisterUriScheme(WebWindowResource.SchemeData);

        LinuxMessageLoopSynchronizationContext.Initialize();
        SynchronizationContext.SetSynchronizationContext(LinuxMessageLoopSynchronizationContext.Instance);
    }

    public IWindowBackend CreateWindow(WebWindowOptions options)
        => LinuxWindow.Create(options);

    public void RunMessageLoop()
    {
        LinuxMessageLoopSynchronizationContext.Initialize();
        SynchronizationContext.SetSynchronizationContext(LinuxMessageLoopSynchronizationContext.Instance);

        var loop = MainLoop.New(null, false);
        _mainLoop = loop;
        loop.RunWithSynchronizationContext();
        _mainLoop = null;
    }

    /// <summary>
    /// 注册单个自定义 scheme（镜像 Windows 的 CoreWebView2CustomSchemeRegistration 项）。
    /// 平台构造时对 app 与 appdata 各调用一次；默认 WebContext 跨窗口共享，register_uri_scheme
    /// 是 context 级，进程内注册一次即可。
    /// </summary>
    private static void RegisterUriScheme(string scheme)
        => WebKit2Native.RegisterUriScheme(scheme, _schemeCallback);

    /// <summary>
    /// 窗口注册：按 WebView 指针登记，供 scheme 请求回调分派（镜像 Windows 平台 WindowOpen 的窗口表登记）。
    /// </summary>
    internal static void WindowOpen(LinuxWindow window)
    {
        _windows[window.WebView] = window;
    }

    /// <summary>
    /// 窗口注销：移除登记、通知框架关闭；最后一个窗口关闭时退出主循环（镜像 Windows 平台 WindowClose：
    /// 含 NotifyWindowClosed + PostQuitMessage）。
    /// </summary>
    internal static void WindowClose(LinuxWindow window)
    {
        _windows.Remove(window.WebView);
        if (_windows.Count == 0)
        {
            QuitMainLoop();
        }
    }

    /// <summary>
    /// 共享默认 WebContext 的 scheme 请求回调：按发起 WebView 指针经窗口表分派回对应窗口
    /// （镜像 Windows 的 WndProc 经 <see cref="_windows"/> 查 HWND）。
    /// </summary>
    private static void OnUriSchemeRequest(IntPtr request, IntPtr userData)
    {
        if (!_windows.TryGetValue(WebKit2Native.GetSchemeRequestWebView(request), out LinuxWindow? _))
        {
            FinishNotFound(request);
            return;
        }
        HandleUriSchemeRequest(request);
    }

    /// <summary>
    /// 处理单个 scheme 请求（镜像 Windows 的 OnWebResourceRequested）。
    /// </summary>
    private static void HandleUriSchemeRequest(IntPtr request)
    {
        try
        {
            var uri = WebKit2Native.GetSchemeRequestUri(request);
            if (WebWindowResource.TryResolvePath(uri, out string? relative, out string? mimeType) is { } stream)
            {
                var bytes = new byte[stream.Length];
                stream.ReadExactly(bytes);
                WebKit2Native.FinishSchemeRequest(request, bytes, mimeType!, ResourceHeaders.CacheControl(relative!));
                stream.Dispose();
                return;
            }
        }
        catch
        {
            // 读取或构造响应失败时回退 404
        }
        FinishNotFound(request);
    }

    private static void FinishNotFound(IntPtr request)
    {
        try
        {
            var bytes = Encoding.UTF8.GetBytes("404 Not Found");
            WebKit2Native.FinishSchemeRequest(request, bytes, "text/plain; charset=utf-8", "no-store");
        }
        catch
        {
            // 请求已被取消等，忽略
        }
    }

    /// <summary>
    /// 最后一个窗口销毁时调用，退出主循环。
    /// </summary>
    internal static void QuitMainLoop() => _mainLoop?.Quit();
}
