namespace WebWindowUI;

/// <summary>
/// 平台引导（入口包）：AOT 安全的平台实现加载。NativeAOT 没有运行时程序集加载/按名反射，
/// 故改为编译期 <c>#if</c> 静态引用当前平台类型——
/// JIT 下 <c>typeof</c> 解析强制加载平台程序集，其 [ModuleInitializer] 完成实际注册；
/// NativeAOT 下类型被静态链接、模块初始化器在进程启动时按依赖序执行。
/// 消费方在程序入口 Main 首行调用一次 <see cref="EnsureRegistered"/>（平台无关，无任何平台类型在消费方代码中出现）。
/// </summary>
public static class Platform
{
    /// <summary>确保当前平台实现已加载并注册。幂等，可重复调用。</summary>
    public static void EnsureRegistered()
    {
#if WWUI_CEF
        // CEF 渲染器（UseCEF=true，仅 Windows）：显式引导——下载运行时、子进程短路、cef_initialize、注册。
        // 必须置最前（先于 #elif WINDOWS）；Cef 程序集内无 [ModuleInitializer]，靠这里显式调用。
        WebWindowUI.Cef.CefBootstrap.EnsureRegistered();
        return;
#elif WINDOWS
        GC.KeepAlive(typeof(WebWindowUI.Windows.WindowsPlatform));
#elif LINUX
        GC.KeepAlive(typeof(WebWindowUI.Linux.LinuxPlatform));
#elif MACOS
        GC.KeepAlive(typeof(WebWindowUI.MacOS.MacOSPlatform));
#else
        throw new PlatformNotSupportedException("当前操作系统不受支持（需 Windows/Linux/macOS）。");
#endif
    }
}
