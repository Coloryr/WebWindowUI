using WebWindowUI.Core;

namespace WebWindowUI;

/// <summary>
/// 平台引导（入口包）：AOT 安全的平台实现加载。NativeAOT 无运行时按名加载，故编译期静态引用
/// 当前平台类型——JIT 下 <c>typeof</c> 强制加载平台程序集，[ModuleInitializer] 完成注册；
/// NativeAOT 下类型被静态链接、初始化器按依赖序执行。消费方在 Main 首行调用一次
/// <see cref="EnsureRegistered"/>。
///
/// 平台分派实现由构建生成（<c>_WWUI_GeneratePlatformDispatch</c> 产出 <c>Platform.g.cs</c> partial），
/// partial 方法实现缺失时编译报错而非静默空操作。
/// </summary>
public static partial class WebWindowUIPlatform
{
    /// <summary>
    /// 确保当前平台实现已加载并注册。幂等，可重复调用。
    /// </summary>
    public static void Init()
    {
        EnsureRegisteredCore();
    }

    private static partial void EnsureRegisteredCore();

    /// <summary>
    /// 运行当前平台的消息循环，直到所有窗口关闭后返回。
    /// </summary>
    public static void Run() => WebWindowPlatform.Current.RunMessageLoop();
}
