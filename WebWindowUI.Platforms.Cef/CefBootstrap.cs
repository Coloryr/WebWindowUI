using System.Runtime.InteropServices;

namespace WebWindowUI.Cef;

/// <summary>
/// CEF 平台引导（显式，本程序集不写 [ModuleInitializer]——CEF 渲染/GPU 子进程复进主 exe 时会加载本
/// 程序集，ModuleInitializer 会在子进程里也执行，而子进程需要短路退出而非注册）。由入口
/// <c>Platform.EnsureRegistered()</c> 的 <c>#if WWUI_CEF</c> 分支调用。幂等。
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
        //   ② cef_execute_process(&args, app, null)——子进程（渲染/GPU）返回 ≥0 → Environment.Exit(code)，浏览器进程返回 -1 继续；
        //   ③ 零值 cef_settings_t（size 手填）+ no_sandbox=1 + 单线程消息循环 + framework/resources/locales/cache 指向缓存 → cef_initialize；
        //   ④ WebWindowPlatform.Register(new CefPlatform())——此后 WebWindow 构造可取 Current。
        // 自定义 scheme 注册（app/appbin）在 Phase 4 经 cef_app_t 接入，此处 app 传空。

        CefRuntimeManager.EnsureRuntime();

        var args = new CefMainArgs { Instance = CefNative.GetModuleHandle(null) };
        IntPtr app = IntPtr.Zero; // Phase 4: CefSchemes.CreateApp()

        int exitCode = CefNative.cef_execute_process(ref args, app, IntPtr.Zero);
        if (exitCode >= 0) // 子进程：CEF 在 cef_execute_process 内部跑完子进程消息循环，返回退出码
            Environment.Exit(exitCode);

        var settings = new CefSettings { Size = (ulong)Marshal.SizeOf<CefSettings>() };
        settings.NoSandbox = 1;              // 不链 cef_sandbox，Chromium 沙箱须关掉
        settings.MultiThreadedMessageLoop = 0; // 单线程环：cef_do_message_loop_work 与 Win32 GetMessage 同线程
        settings.ExternalMessagePump = 0;
        // 框架目录自包含（Release 内含合并进来的 Resources/*）：CEF 151 on Windows 的 DIR_ASSETS（ICU 数据）
        // 从 libcef.dll 所在目录解析、不理会 resources_dir_path，故全部指向 ReleaseDir。
        settings.ResourcesDirPath = CefNative.CreateString(CefRuntimeManager.ReleaseDir);
        settings.LocalesDirPath = CefNative.CreateString(Path.Combine(CefRuntimeManager.ReleaseDir, "locales"));
        settings.CachePath = CefNative.CreateString(CefRuntimeManager.CacheDir);
        // TEMP DIAG: 详细日志
        settings.LogFile = CefNative.CreateString(Path.Combine(CefRuntimeManager.CacheRoot, "cef_diag.log"));
        settings.LogSeverity = -1; // LOGSEVERITY_VERBOSE
        // browser_subprocess_path 留空 = 自宿主：CEF 子进程复进主 exe（同一条 EnsureRegistered 路径短路退出）

        try
        {
            if (CefNative.cef_initialize(ref args, ref settings, app, IntPtr.Zero) == 0)
                throw new InvalidOperationException("cef_initialize 失败：CEF 运行时缺失或与版本不匹配。");
        }
        finally
        {
            // cef_initialize 已拷贝 settings 字符串（内部语义），此处释放 CreateString(copy=1) 的 CEF 自持缓冲
            CefNative.FreeString(ref settings.ResourcesDirPath);
            CefNative.FreeString(ref settings.LocalesDirPath);
            CefNative.FreeString(ref settings.CachePath);
            CefNative.FreeString(ref settings.LogFile);
        }

        WebWindowPlatform.Register(new CefPlatform());
    }
}
