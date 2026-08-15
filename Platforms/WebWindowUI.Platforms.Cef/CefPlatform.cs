using System.Collections.Concurrent;
using System.Text;
using CefSharp;
using CefSharp.WinForms;
using WebWindowUI.Core;
using WebWindowUI.Natives.Windows;

namespace WebWindowUI.Platforms.Cef;

/// <summary>
/// CEF 平台实现（Windows）：浏览器托管层用 CefSharp（ChromiumWebBrowser + CefSharp.BrowserSubprocess），
/// 承载于裸 Win32 顶层窗口（ChromiumWebBrowser 控件 SetParent 重挂载进客户区）。初始化走 Cef.Initialize，
/// 自定义 scheme（app/appdata）经 CefCustomSchemes + 处理器工厂注册。
/// </summary>
public sealed class CefPlatform : IWebWindowPlatform
{
    /// <summary>
    /// Win32 消息循环（隐藏消息窗口调度，供跨线程 marshal 回 UI 线程）。
    /// </summary>
    private static readonly IMessageLoop _message = new Win32MessageLoop();

    /// <summary>
    /// 浏览器 id → 窗口映射，供 scheme 请求回调分派回对应窗口。
    /// </summary>
    private static readonly ConcurrentDictionary<long, CefWindow> _browsers = new();

    /// <summary>
    /// 是否已初始化。
    /// </summary>
    private static bool _initialized;

    /// <summary>
    /// 是否已调过 Cef.Shutdown（防 ProcessExit 与 RunMessageLoop 双关）。
    /// </summary>
    private static bool _shutdownDone;

    /// <summary>
    /// 初始化 CEF 运行时（Cef.Initialize + Win32 消息循环）。须在 UI 线程调用一次。
    /// </summary>
    public void Init()
    {
        if (_initialized)
            return;
        _initialized = true;

        var cachePath = Path.Combine(Path.GetTempPath(), "WebWindowUI-Cef", Environment.ProcessId.ToString());
        Directory.CreateDirectory(cachePath);

        var settings = new CefSettings
        {
            RootCachePath = cachePath,
            LogSeverity = LogSeverity.Verbose,
            LogFile = Path.Combine(cachePath, "cef.log"),
            BrowserSubprocessPath = Path.Combine(AppContext.BaseDirectory, "CefSharp.BrowserSubprocess.exe"),
        };
        settings.CefCommandLineArgs["no-sandbox"] = "1";

        // 自定义 scheme：app（页面资源）+ appdata（数据路由），同一工厂处理 GET 资源与 POST 消息。
        var factory = new AppSchemeHandlerFactory();
        settings.CefCustomSchemes.Add(new CefCustomScheme
        {
            SchemeName = WebWindowResource.Scheme,
            SchemeHandlerFactory = factory,
            IsSecure = true,
            IsCorsEnabled = true,
            IsFetchEnabled = true,
            IsLocal = true,
            IsDisplayIsolated = true,
        });
        settings.CefCustomSchemes.Add(new CefCustomScheme
        {
            SchemeName = WebWindowResource.SchemeData,
            SchemeHandlerFactory = factory,
            IsSecure = true,
            IsCorsEnabled = true,
            IsFetchEnabled = true,
            IsLocal = true,
            IsDisplayIsolated = true,
        });

        Cef.Initialize(settings, performDependencyCheck: true, browserProcessHandler: null);
        AppDomain.CurrentDomain.ProcessExit += (_, _) => Shutdown();

        _message.InitMessageLoop();
    }

    /// <summary>
    /// 关闭 CEF 运行时（幂等）。
    /// </summary>
    internal static void Shutdown()
    {
        if (_shutdownDone)
            return;
        _shutdownDone = true;
        if (Cef.IsInitialized)
            Cef.Shutdown();
    }

    /// <summary>
    /// 注册浏览器 id → 窗口映射。
    /// </summary>
    /// <param name="browserId">浏览器 id。</param>
    /// <param name="window">承载窗口。</param>
    internal static void RegisterBrowser(long browserId, CefWindow window)
        => _browsers[browserId] = window;

    /// <summary>
    /// 摘除浏览器映射。
    /// </summary>
    /// <param name="browserId">浏览器 id。</param>
    internal static void UnregisterBrowser(long browserId)
        => _browsers.TryRemove(browserId, out _);

    /// <summary>
    /// 按浏览器 id 取窗口。
    /// </summary>
    /// <param name="browserId">浏览器 id。</param>
    /// <param name="window">命中的窗口。</param>
    /// <returns>是否命中。</returns>
    internal static bool TryGetWindow(long browserId, out CefWindow window)
        => _browsers.TryGetValue(browserId, out window!);

    /// <summary>
    /// 把动作 marshal 到 CEF UI 线程执行。
    /// </summary>
    /// <param name="action">要执行的动作。</param>
    internal static void RunOnCefUiThread(Action action)
    {
        if (Cef.CurrentlyOnThread(CefThreadIds.TID_UI))
        {
            action();
            return;
        }
        using var done = new ManualResetEventSlim();
        Exception? error = null;
        Cef.UIThreadTaskFactory.StartNew(() =>
        {
            try { action(); }
            catch (Exception ex) { error = ex; }
            finally { done.Set(); }
        });
        done.Wait();
        if (error is not null)
            throw error;
    }

    /// <summary>
    /// 创建 CEF 窗口。
    /// </summary>
    /// <param name="options">窗口选项。</param>
    /// <returns>窗口后端。</returns>
    public IWindowBackend CreateWindow(WebWindowOptions options)
        => new CefWindow(options);

    /// <summary>
    /// 运行主消息循环（末窗关闭后返回），随后同线程关闭 CEF 运行时。
    /// </summary>
    public void RunMessageLoop()
    {
        _message.MessageLoop();
        Shutdown();
    }

    /// <summary>
    /// 把动作 marshal 到原生 UI 线程（主线程）同步执行：Win32 窗口 API 要求主线程。
    /// </summary>
    /// <param name="action">要执行的动作。</param>
    public void RunOnUiThread(Action action)
        => _message.RunOnUiThread(action);

    /// <summary>
    /// 当前线程是否是原生 UI 线程（主线程）。
    /// </summary>
    /// <returns>是否在主线程。</returns>
    public bool IsUiThread() => _message.IsUiThread();

    /// <summary>
    /// 系统消息框。
    /// </summary>
    /// <param name="title">标题。</param>
    /// <param name="message">内容。</param>
    /// <param name="error">是否错误样式。</param>
    public void ShowMessageBox(string title, string message, bool error)
        => Win32Native.ShowMessage(title, message, error);

    /// <summary>
    /// 文件打开对话框；取消返回 null。
    /// </summary>
    /// <param name="title">对话框标题。</param>
    /// <param name="filter">文件过滤器。</param>
    /// <param name="initialDirectory">初始目录；null 用默认。</param>
    /// <param name="fileMustExist">是否要求文件存在。</param>
    /// <param name="allowMultiSelect">是否允许多选。</param>
    /// <returns>选中的文件路径；取消为 null。</returns>
    public string[]? OpenFileDialog(string title, string filter, string? initialDirectory = null, bool fileMustExist = true, bool allowMultiSelect = true)
        => Win32Native.OpenFileDialog(title, filter, initialDirectory, fileMustExist, allowMultiSelect)?.ToArray();

    /// <summary>
    /// 文件保存对话框；取消返回 null。
    /// </summary>
    /// <param name="title">对话框标题。</param>
    /// <param name="filter">文件过滤器。</param>
    /// <param name="defaultFileName">默认文件名。</param>
    /// <param name="defaultExt">默认扩展名。</param>
    /// <returns>选择的保存路径；取消为 null。</returns>
    public string? SaveFileDialog(string title, string filter, string? defaultFileName = null, string? defaultExt = null)
        => Win32Native.SaveFileDialog(title, filter, defaultFileName, defaultExt);
}
