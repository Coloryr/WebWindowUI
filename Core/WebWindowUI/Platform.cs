namespace WebWindowUI;

/// <summary>
/// 平台引导（入口包）：AOT 安全的平台实现加载。NativeAOT 没有运行时程序集加载/按名反射，
/// 故改为编译期静态引用当前平台类型——JIT 下 <c>typeof</c> 解析强制加载平台程序集，
/// 其 [ModuleInitializer] 完成实际注册；NativeAOT 下类型被静态链接、模块初始化器在进程启动时
/// 按依赖序执行。消费方在程序入口 Main 首行调用一次 <see cref="EnsureRegistered"/>
/// （平台无关，无任何平台类型在消费方代码中出现）。
///
/// 平台分派实现由构建生成（<c>_WWUI_GeneratePlatformDispatch</c> 目标按 WWUIPlatform/UseCEF
/// 产出 <c>Platform.g.cs</c> partial 实现，见 WebWindowUI.csproj）——partial 方法带 private 访问
/// 修饰符，C# 要求实现必须存在，生成缺失时编译报错而非静默空操作。
/// </summary>
public static partial class Platform
{
    /// <summary>确保当前平台实现已加载并注册。幂等，可重复调用。</summary>
    public static void EnsureRegistered() => EnsureRegisteredCore();

    private static partial void EnsureRegisteredCore();
}
