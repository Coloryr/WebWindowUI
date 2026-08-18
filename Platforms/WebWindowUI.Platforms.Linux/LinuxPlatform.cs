using WebWindowUI.Core;
using WebWindowUI.Core.Platform;
using WebWindowUI.Natives.Linux;

namespace WebWindowUI.Platforms.Linux;

/// <summary>
/// Linux 平台实现：GTK3 宿主 + libwebkit2gtk-4.1（GTK3 端口，WebKit/GTK 均手写 P/Invoke）。
/// 用 GLib.MainLoop 跑主循环（不用 Gtk.Application，避开其 D-Bus 唯一实例限制）。
/// </summary>
public sealed class LinuxPlatform : IPlatform
{
    private static readonly Dictionary<IntPtr, LinuxWindow> _windows = [];
    private static readonly WebKit2Native.WebKitUriSchemeRequestCallback _schemeCallback = OnUriSchemeRequest;

    private static MainLoop? _mainLoop;

    /// <summary>
    /// 初始化 GTK/WebKit 并注册 app/appdata 自定义 scheme。
    /// </summary>
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

    /// <summary>
    /// 平台初始化（IWebWindowPlatform 契约）：构造时已完成（GTK/WebKit + scheme），空实现。
    /// </summary>
    /// <param name="args">命令行参数（本平台不使用）。</param>
    public void Init(string[] args)
    {
    }

    /// <summary>
    /// 创建平台窗口（尚未显示）。
    /// </summary>
    /// <param name="options">窗口选项。</param>
    /// <returns>平台窗口。</returns>
    public WebWindow CreateWindow(WebWindowOptions options)
        => LinuxWindow.Create(options);

    /// <summary>
    /// 创建窗口系统托盘（GTK 未实现，抛 NotSupportedException）。
    /// </summary>
    /// <param name="window">所属窗口。</param>
    public ITrayIcon CreateTrayIcon(WebWindow window)
        => window.NativeWindow.CreateTrayIcon(window.Title);

    /// <summary>
    /// 运行 GLib 主循环，直到最后一个窗口关闭退出。
    /// </summary>
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
                WebKit2Native.FinishSchemeRequest(request, bytes, mimeType!, WebWindowResource.CacheControl(relative!));
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

    /// <summary>
    /// 以 404 完成 scheme 请求。
    /// </summary>
    /// <param name="request">scheme 请求句柄。</param>
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

    /// <summary>
    /// 把动作 marshal 到 UI（GTK 主循环）线程同步执行：UI 线程直接运行；非 UI 线程经
    /// LinuxMessageLoopSynchronizationContext.Send 回 UI 线程并阻塞等待。
    /// </summary>
    public void RunOnUiThread(Action action)
        => LinuxMessageLoopSynchronizationContext.Instance.Send(_ => action(), null);

    /// <summary>
    /// 当前线程是否 UI（GTK 主循环）线程。
    /// </summary>
    /// <returns>是否 UI 线程。</returns>
    public bool IsUiThread()
        => Environment.CurrentManagedThreadId == LinuxMessageLoopSynchronizationContext.UiThreadId;

    /// <summary>
    /// 平台对话框（GTK 实现）。
    /// </summary>
    public IPlatformDialog Dialog => GtkDialog.Dialog;

    /// <summary>
    /// 平台剪贴板（GTK 实现）。
    /// </summary>
    public IClipboard Clipboard => LinuxClipboard.Clipboard;

    /// <summary>
    /// 平台系统通知（Linux 通知服务未实现，抛异常明示）。
    /// </summary>
    public INotification Notification
        => throw new NotSupportedException("Linux 平台暂不支持系统通知（libnotify 未实现）。");
}
