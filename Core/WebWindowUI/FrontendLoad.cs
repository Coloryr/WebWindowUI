using System.Runtime.CompilerServices;

namespace WebWindowUI;

/// <summary>
/// 应用侧前端 dll 加载器（随 WebWindowUI 包分发，由 targets 注入编译进应用工程，Release）：
/// 模块初始化器在进程启动时用 <c>typeof</c> 静态引用 <see cref="FrontendHost"/>——
/// JIT 下强制加载前端 dll（进入已加载程序集集合）、NativeAOT 下把它根进链接闭包（内嵌 wwwroot 随之保留）。
/// NativeAOT 无运行时按名加载，故内嵌 wwwroot 的前端 dll 靠宿主类型静态引用强制加载。
/// </summary>
internal static class FrontendLoad
{
    [ModuleInitializer]
    internal static void EnsureFrontendLoaded() => GC.KeepAlive(typeof(FrontendHost));
}
