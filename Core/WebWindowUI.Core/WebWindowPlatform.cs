namespace WebWindowUI.Core;

/// <summary>
/// 平台入口（纯注册表，平台无关）：平台实现已拆分为 WebWindowUI.Platforms.{Windows,Linux,MacOS}
/// 三个独立包，由入口包 WebWindowUI 按操作系统静态选择并触发注册（AOT 安全：编译期 #if 静态引用，
/// 无运行时反射——见入口包 <c>WebWindowUI.Platform.EnsureRegistered</c>）。消费方在程序入口 Main 首行调用
/// EnsureRegistered 后，本类型 <see cref="Current"/> 即返回已注册的平台实现。
/// 新增平台只需新建 Platforms.* 包 + 注册模块初始化器，核心包零改动。
/// </summary>
public static class WebWindowPlatform
{
    private static IWebWindowPlatform? _implementation;

    /// <summary>当前平台的 WebView 实现（首注册者生效，幂等）。</summary>
    public static IWebWindowPlatform Current => _implementation
        ?? throw new PlatformNotSupportedException("未注册平台实现：请在程序入口 Main 首行调用 WebWindowUI.Platform.EnsureRegistered()。");

    /// <summary>平台程序集模块初始化器调用本方法注册自身实现（首注册者生效）。
    /// 直接写本类型的静态字段即可：本类型无静态字段初始化器、无 cctor，模块初始化器调用不会触发类型初始化死锁。</summary>
    internal static void Register(IWebWindowPlatform impl) => _implementation ??= impl;
}
