using System.Collections.Concurrent;
using System.Linq;
using WebWindowUI.Core;
using WebWindowUI.Core.Platform;
using WebWindowUI.Natives.Windows;
using Xilium.CefGlue;
using Xilium.CefGlue.BrowserProcess;
using Xilium.CefGlue.Common.Shared;

namespace WebWindowUI.Platforms.Cef;

/// <summary>
/// CEF 平台实现（Windows）：浏览器托管层用纯 CefGlue 公共 API 自建（CefBrowserHosting +
/// 隐藏宿主重挂载，替代 CefGlue.Common 内部 CommonBrowserAdapter/BaseCefBrowser），
/// 初始化走 CefRuntime.Load/Initialize + 自建 AppCefApp（注册自定义 scheme + 子进程
/// --custom-scheme 传播），自定义 scheme（app/appdata）经 RegisterSchemeHandlerFactory 注册。
/// </summary>
public sealed class CefPlatform : IPlatform
{
    /// <summary>
    /// Win32 消息循环（隐藏消息窗口调度，供跨线程 marshal 回 UI 线程）。
    /// </summary>
    private static readonly Win32MessageLoop _message = new();

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
    /// 平台剪贴板（CEF 宿主在 Windows，用 Win32 实现）。
    /// </summary>
    public IClipboard Clipboard => Win32Clipboard.Instance;

    /// <summary>
    /// 平台系统通知（CEF 宿主在 Windows，复用 Win32 气泡实现）。
    /// </summary>
    public INotification Notification => Win32Notification.Instance;

    /// <summary>
    /// 初始化 CEF 运行时：子进程分发（CefSubProcess）→ CefRuntime.Load/Initialize
    /// （AppCefApp 注册自定义 scheme）→ 逐 scheme 注册处理器工厂 → Win32 消息循环。
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

        // 公共初始化链（对齐上游 CefRuntimeLoader.InternalInitialize）：AppCefApp 经 OnRegisterCustomSchemes
        // 注册 scheme（标准/secure/CORS 语义镜像 Windows WebView2 的 TreatAsSecure + AllowedOrigins="*"）；
        // 处理器工厂在 Initialize 后立即注册（factory 是浏览器进程侧，经 IPC 服务渲染进程请求）。
        CefRuntime.Load();
        CefRuntime.Initialize(new CefMainArgs(args), settings, new AppCefApp(schemes), IntPtr.Zero);
        foreach (var s in schemes)
            CefRuntime.RegisterSchemeHandlerFactory(s.SchemeName, s.DomainName, s.SchemeHandlerFactory);
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
    /// 运行模态消息循环直到窗口关闭（ShowDialog 用；Win32 内部不可被平台层直引，经此处暴露）。
    /// </summary>
    /// <param name="isDone">窗口是否已关闭。</param>
    internal static void RunModalLoop(Func<bool> isDone) => _message.RunModalLoop(isDone);

    /// <summary>
    /// 创建 CEF 窗口（尚未显示）。Win32 窗口必须由 UI（主）线程创建：命令路径（scheme POST）在
    /// CEF IO 线程，直接建窗会把 HWND 绑到 IO 线程消息队列 → 主线程 SetWindowTextW 等
    /// SendMessage 跨线程等待 → 双窗口互锁死锁。非 UI 线程 marshal 到主线程同步创建。
    /// </summary>
    /// <param name="options">窗口选项。</param>
    /// <returns>平台窗口。</returns>
    public WebWindow CreateWindow(WebWindowOptions options)
    {
        CefWindow? window = null;
        _message.RunOnUiThread(() => window = new CefWindow(options));
        return window!;
    }

    /// <summary>
    /// 创建窗口系统托盘（CEF 宿主在 Windows，复用 Win32 托盘实现）。
    /// </summary>
    /// <param name="window">所属窗口。</param>
    public ITrayIcon CreateTrayIcon(WebWindow window)
        => window.NativeWindow.CreateTrayIcon(window.Title);

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
    /// CEF App：浏览器进程注册自定义 scheme（OnRegisterCustomSchemes）+ 经
    /// --custom-scheme 命令行向子进程传播 scheme 定义（渲染进程据此再注册，对齐上游
    /// CefGlue.Common 的 BrowserCefApp + CommonBrowserProcessHandler）。
    /// </summary>
    private sealed class AppCefApp : CefApp
    {
        /// <summary>
        /// 自定义 scheme 列表。
        /// </summary>
        private readonly CustomScheme[] _schemes;

        /// <summary>
        /// 浏览器进程处理器（子进程 scheme 传播）。
        /// </summary>
        private readonly AppBrowserProcessHandler _browserProcessHandler;

        /// <summary>
        /// 构造 App：登记 scheme 列表与子进程传播器。
        /// </summary>
        /// <param name="schemes">自定义 scheme 列表。</param>
        public AppCefApp(CustomScheme[] schemes)
        {
            _schemes = schemes;
            _browserProcessHandler = new AppBrowserProcessHandler(schemes);
        }

        /// <summary>
        /// 注册自定义 scheme（浏览器进程初始化期回调；子进程启动时由 CEF 从命令行恢复）。
        /// </summary>
        /// <param name="registrar">scheme 注册器。</param>
        protected override void OnRegisterCustomSchemes(CefSchemeRegistrar registrar)
        {
            foreach (var s in _schemes)
                registrar.AddCustomScheme(s.SchemeName, s.Options);
        }

        /// <summary>
        /// 返回浏览器进程处理器。
        /// </summary>
        /// <returns>处理器实例。</returns>
        protected override CefBrowserProcessHandler GetBrowserProcessHandler() => _browserProcessHandler;
    }

    /// <summary>
    /// 浏览器进程处理器：子进程启动时把自定义 scheme 序列化进命令行
    /// （渲染进程经 CefSubProcess.Run 读出并注册，镜像上游 CommonBrowserProcessHandler）。
    /// </summary>
    private sealed class AppBrowserProcessHandler : CefBrowserProcessHandler
    {
        /// <summary>
        /// 序列化的 scheme 定义（格式对齐 CustomScheme.ToCommandLineValue：SchemeName|DomainName|Options 分号拼接）。
        /// </summary>
        private readonly string _schemesArg;

        /// <summary>
        /// 构造处理器：序列化 scheme 列表。
        /// </summary>
        /// <param name="schemes">自定义 scheme 列表。</param>
        public AppBrowserProcessHandler(CustomScheme[] schemes)
            => _schemesArg = string.Join(";", schemes.Select(s => $"{s.SchemeName}|{s.DomainName}|{(int)s.Options}"));

        /// <summary>
        /// 子进程启动前：注入 --custom-scheme 命令行。
        /// </summary>
        /// <param name="commandLine">子进程命令行。</param>
        protected override void OnBeforeChildProcessLaunch(CefCommandLine commandLine)
            => commandLine.AppendSwitch("custom-scheme", _schemesArg);
    }
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
