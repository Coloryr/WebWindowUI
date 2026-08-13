using System.Runtime.CompilerServices;
using WebWindowUI.Core;

namespace WebWindowUI.Platforms.MacOS;

/// <summary>
/// 平台程序集自注册：入口包 <see cref="WebWindowUI.Platform.EnsureRegistered"/>（消费方 Main 首行调用）
/// 编译期 #if 静态引用本程序集类型触发加载 → 本模块初始化器把平台实现注册进核心
/// （经 InternalsVisibleTo 调用 internal Register；AOT 安全：编译期静态引用，无运行时按名加载）。
/// </summary>
internal static class PlatformRegistration
{
    [ModuleInitializer]
    internal static void Register()
    {
        WebWindowPlatform.Register(new MacOSPlatform());
    }
}
