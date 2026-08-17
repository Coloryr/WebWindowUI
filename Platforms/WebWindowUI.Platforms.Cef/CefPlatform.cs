using System.Collections.Concurrent;
using WebWindowUI.Core;
using WebWindowUI.Natives.Windows;
using Xilium.CefGlue;
using Xilium.CefGlue.BrowserProcess;
using Xilium.CefGlue.Common;
using Xilium.CefGlue.Common.Shared;

namespace WebWindowUI.Platforms.Cef;

/// <summary>
/// CEF 平台实现（Windows）：浏览器托管层用上游 CefGlue.Common（BaseCefBrowser + Win32CefControl
/// 隐藏宿主重挂载），初始化走 CefRuntimeLoader（子进程分发 CefSubProcess 由应用 Main 负责），
/// 自定义 scheme（app/appdata）经 CustomScheme 注册。
/// </summary>
public sealed class CefPlatform : IPlatform
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
    /// 是否已调过 CefRuntime.Shutdown（防 ProcessExit 与 RunMessageLoop 双关）。
    /// </summary>
    private static bool _shutdownDone;

    public IPlatformDialog Dialog => NativePlatform.Dialog;

    /// <summary>
    /// 初始化 CEF 运行时（CefRuntimeLoader.Initialize 延迟到首个 BaseCefBrowser 构造时 Load）。
    /// 须在 UI 线程调用一次。
    /// </summary>
    public void Init(string[] args)
    {
        CefSubProcess.Run(args, true);

        if (_initialized)
            return;
        _initialized = true;

        var cachePath = Path.Combine(Path.GetTempPath(), "WebWindowUI-Cef", Environment.ProcessId.ToString());
        Directory.CreateDirectory(cachePath);

        var settings = new CefSettings
        {
            RootCachePath = cachePath,
            NoSandbox = true,
            LogSeverity = CefLogSeverity.Verbose,
            LogFile = Path.Combine(cachePath, "cef.log"),
        };

        // 自定义 scheme：app（页面资源）+ appdata（数据路由），同一工厂处理 GET 资源与 POST 消息。
        var factory = new AppSchemeHandlerFactory();
        CustomScheme[] schemes =
        [
            new CustomScheme { SchemeName = WebWindowResource.Scheme, SchemeHandlerFactory = factory },
            new CustomScheme { SchemeName = WebWindowResource.SchemeData, SchemeHandlerFactory = factory },
        ];

        CefRuntimeLoader.Initialize(settings, customSchemes: schemes);
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
        if (CefRuntime.IsInitialized)
        {
            CefRuntime.Shutdown();
        }
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
    /// 把动作 marshal 到 CEF UI 线程同步执行。
    /// </summary>
    /// <param name="action">要执行的动作。</param>
    internal static void RunOnCefUiThread(Action action)
    {
        if (CefRuntime.CurrentlyOn(CefThreadId.UI))
        {
            action();
            return;
        }
        using var done = new ManualResetEventSlim();
        Exception? error = null;
        CefRuntime.PostTask(CefThreadId.UI, new ActionCefTask(() =>
        {
            try { action(); }
            catch (Exception ex) { error = ex; }
            finally { done.Set(); }
        }));
        done.Wait();
        if (error is not null)
            throw error;
    }

    /// <summary>
    /// 把动作投递到 CEF UI 线程异步执行（fire-and-forget）。
    /// </summary>
    /// <param name="action">要执行的动作。</param>
    internal static void PostToCefUiThread(Action action)
        => CefRuntime.PostTask(CefThreadId.UI, new ActionCefTask(action));

    /// <summary>
    /// 创建 CEF 窗口。Win32 窗口必须由 UI（主）线程创建：命令路径（scheme POST）在
    /// CEF IO 线程，直接建窗会把 HWND 绑到 IO 线程消息队列 → 主线程 SetWindowTextW 等
    /// SendMessage 跨线程等待 → 双窗口互锁死锁。非 UI 线程 marshal 到主线程同步创建。
    /// </summary>
    /// <param name="options">窗口选项。</param>
    /// <returns>窗口后端。</returns>
    public IWindowBackend CreateWindow(WebWindowOptions options)
    {
        CefWindow? window = null;
        _message.RunOnUiThread(() => window = new CefWindow(options));
        return window!;
    }

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
}

/// <summary>
/// CefTask 包装 Action（CEF UI 线程任务）。
/// </summary>
internal sealed class ActionCefTask : CefTask
{
    /// <summary>
    /// 要执行的动作。
    /// </summary>
    private readonly Action _action;

    /// <summary>
    /// 构造任务。
    /// </summary>
    /// <param name="action">要执行的动作。</param>
    public ActionCefTask(Action action) => _action = action;

    /// <summary>
    /// 执行动作。
    /// </summary>
    protected override void Execute() => _action();
}
