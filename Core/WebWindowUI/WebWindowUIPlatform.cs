using WebWindowUI.Core;

namespace WebWindowUI;

/// <summary>
/// 平台引导（入口包）：AOT 安全的平台实现加载。NativeAOT 无运行时按名加载，故编译期静态引用
/// 当前平台类型——JIT 下 <c>typeof</c> 强制加载平台程序集，[ModuleInitializer] 完成注册；
/// NativeAOT 下类型被静态链接、初始化器按依赖序执行。消费方在 Main 首行调用一次
/// <see cref="Init"/>。
///
/// 平台选择不在本入口程序集内烘焙：本程序集是共享 dll，<c>UseCEF</c> 只在消费方应用工程可见
/// （MSBuild 属性不跨 ProjectReference 传播），旧实现把分派烤进本程序集导致 CEF 永远选不中。
/// 改由构建期 targets 给应用工程注入 <c>PlatformBootstrap.g.cs</c>（[ModuleInitializer]）调用
/// <see cref="RegisterPlatformLoader"/> 登记惰性加载委托，真正的加载在 <see cref="Init"/>
/// 触发——应用进程启动、测试进程加载应用 dll 都不会提前加载平台（泵线程注册语义不受干扰）。
/// </summary>
public static class WebWindowUIPlatform
{
    /// <summary>
    /// 注册平台加载委托（构建期由注入进应用工程的 PlatformBootstrap.g.cs 调用，消费方勿手写）。
    /// 惰性：只登记，不触发任何平台程序集加载；首次注册生效（幂等）。
    /// AOT 安全：委托体里的 <c>typeof(平台类型)</c> 在 NativeAOT 下把平台程序集根进应用链接闭包。
    /// </summary>
    public static void RegisterPlatformLoader(IWebWindowPlatform platform)
    {
        WebWindowPlatform.Register(platform);
    }

    /// <summary>
    /// 运行当前平台的消息循环，直到所有窗口关闭后返回。
    /// </summary>
    public static void Run() => WebWindowPlatform.Current.RunMessageLoop();
}
