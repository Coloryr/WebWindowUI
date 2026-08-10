using Xilium.CefGlue;

namespace WebWindowUI.Cef;

/// <summary>
/// CEF 平台引导（显式，本程序集不写 [ModuleInitializer]——CEF 渲染/GPU 子进程复进主 exe 时会加载本
/// 程序集，ModuleInitializer 会在子进程里也执行，而子进程需要短路退出而非注册）。由入口
/// <c>Platform.EnsureRegistered()</c> 的 <c>#if WWUI_CEF</c> 分支调用。幂等。
/// 渲染内核用 CefGlue（Deon-Berlin 分支 150.7871.115 = CEF 150.0.11，替换原手写 cef.h C API 绑定）。
/// </summary>
public static class CefBootstrap
{
    private static int _bootstrapped;

    public static void EnsureRegistered()
    {
        if (Interlocked.Exchange(ref _bootstrapped, 1) != 0)
            return;

        // 有序引导（顺序即正确性）：
        //   ① CefRuntimeManager.EnsureRuntime()——下载 + SHA256 + 解压 + SetDllDirectory（必须先于任何 cef_* 调用）；
        //   ② CefRuntime.Load(ReleaseDir)——加载 libcef.dll + CefGlue API hash 硬校验（绑定 150.7871.115 ↔ 运行时 150.0.11）；
        //   ③ CefRuntime.ExecuteProcess(args, app, null)——子进程（渲染/GPU）返回 ≥0 → Environment.Exit(code)，浏览器进程返回 -1 继续；
        //   ④ CefSettings（no_sandbox + 单线程消息循环 + framework/resources/locales/cache 指向缓存）→ CefRuntime.Initialize；
        //   ⑤ cef_initialize 后注册 app/appbin scheme handler 工厂（CefSchemes.RegisterHandlerFactories）；
        //   ⑥ WebWindowPlatform.Register(new CefPlatform())——此后 WebWindow 构造可取 Current。
        // 自定义 scheme（app/appbin 资源 + JS 回传通道）经 CefApp.OnRegisterCustomSchemes + 工厂接入。

        CefRuntimeManager.EnsureRuntime();

        // CefGlue 加载运行时并对 API hash 硬校验（CheckVersionByApiHash 调 libcef.api_hash 比对
        // 编译期常量，不匹配抛 CefVersionMismatchException）。CefRuntimeManager 已 SetDllDirectory，
        // libcef.dll 及其依赖（chrome_elf/libEGL 等）可解析。
        CefRuntime.Load(CefRuntimeManager.ReleaseDir);

        var mainArgs = new CefMainArgs(Environment.GetCommandLineArgs());
        CefApp app = CefSchemes.CreateApp(); // 仅 on_register_custom_schemes + --disable-gpu；子进程复进时同样传入

        int exitCode = CefRuntime.ExecuteProcess(mainArgs, app, IntPtr.Zero);
        if (exitCode >= 0) // 子进程：CEF 在 ExecuteProcess 内部跑完子进程消息循环，返回退出码
            Environment.Exit(exitCode);

        var settings = new CefSettings
        {
            NoSandbox = true,               // 不链 cef_sandbox，Chromium 沙箱须关掉
            MultiThreadedMessageLoop = false, // 单线程环：CefRuntime.RunMessageLoop 泵消息 + CEF 任务
            ExternalMessagePump = false,
            // 框架目录自包含（Release 内含合并进来的 Resources/*）：CEF on Windows 的 DIR_ASSETS（ICU 数据）
            // 从 libcef.dll 所在目录解析、不理会 resources_dir_path，故全部指向 ReleaseDir。
            ResourcesDirPath = CefRuntimeManager.ReleaseDir,
            LocalesDirPath = Path.Combine(CefRuntimeManager.ReleaseDir, "locales"),
            CachePath = CefRuntimeManager.CacheDir,
            LogFile = Path.Combine(CefRuntimeManager.CacheRoot, "cef_diag.log"),
            LogSeverity = CefLogSeverity.Verbose,
            // browser_subprocess_path 留空 = 自宿主：CEF 子进程复进主 exe（同一条 EnsureRegistered 路径短路退出）
        };

        CefRuntime.Initialize(mainArgs, settings, app, IntPtr.Zero);

        // 注册 app/appbin scheme handler 工厂：须在 cef_initialize 之后、任何浏览器创建请求之前（首个窗口 Show 前完成即可）。
        // GET=资源 / POST=JS 消息回传通道。
        CefSchemes.RegisterHandlerFactories();

        WebWindowPlatform.Register(new CefPlatform());
    }
}
