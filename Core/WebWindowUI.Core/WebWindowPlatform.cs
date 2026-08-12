namespace WebWindowUI.Core;

/// <summary>
/// 平台入口（纯注册表，平台无关）。平台实现拆分在 WebWindowUI.Platforms.{Windows,Linux,MacOS} 包，
/// 由入口包按 OS 静态选择并触发注册（AOT 安全，见 <c>WebWindowUI.Platform.EnsureRegistered</c>）。
/// 消费方在 Main 首行调用后，<see cref="Current"/> 即返回已注册的平台实现。
/// </summary>
public static class WebWindowPlatform
{
    private static IWebWindowPlatform? _implementation;

    /// <summary>
    /// 当前平台的 WebView 实现
    /// </summary>
    public static IWebWindowPlatform Current => _implementation
        ?? throw new PlatformNotSupportedException("未注册平台实现：请在程序入口 Main 首行调用 WebWindowUI.Platform.EnsureRegistered()。");

    /// <summary>
    /// 注册窗口平台
    /// </summary>
    /// <param name="impl">平台实现</param>
    internal static void Register(IWebWindowPlatform impl) => _implementation ??= impl;
}
