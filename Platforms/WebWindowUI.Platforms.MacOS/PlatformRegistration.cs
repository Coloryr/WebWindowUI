using System.Runtime.CompilerServices;
using WebWindowUI.Core;

namespace WebWindowUI.Platforms.MacOS;

/// <summary>
/// 平台程序集自注册：程序集加载时经 InternalsVisibleTo 调 internal Register 把平台实现注册进核心。
/// </summary>
internal static class PlatformRegistration
{
    /// <summary>
    /// 模块初始化器：注册 MacOS 平台实现。
    /// </summary>
    [ModuleInitializer]
    internal static void Register()
    {
        WebWindowPlatform.Register(new MacOSPlatform());
    }
}
