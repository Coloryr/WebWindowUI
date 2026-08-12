using System.Runtime.CompilerServices;

namespace WebWindowUI;

/// <summary>
/// 应用侧前端 dll 加载器（由 targets 注入编译进应用工程，Release）。模块初始化器用 <c>typeof</c>
/// 静态引用 <see cref="FrontendHost"/>——JIT 下强制加载前端 dll，NativeAOT 下把它根进链接闭包
/// （内嵌 wwwroot 随之保留）。
/// </summary>
internal static class FrontendLoad
{
    [ModuleInitializer]
    internal static void EnsureFrontendLoaded() => GC.KeepAlive(typeof(FrontendHost));
}
