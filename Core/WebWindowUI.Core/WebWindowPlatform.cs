using WebWindowUI.Core.Platform;

namespace WebWindowUI.Core;

/// <summary>
/// 平台入口（纯注册表，平台无关）；平台实现由入口包按 OS 静态选择并注册，消费方 Main 首行调用后可用。
/// </summary>
public static class WebWindowPlatform
{
    /// <summary>
    /// 已注册的平台实现。
    /// </summary>
    private static IPlatform? _current;

    /// <summary>
    /// 当前平台的 WebView 实现；未注册时抛异常。
    /// </summary>
    public static IPlatform Current => _current ?? new NullPlatform();

    /// <summary>
    /// 注册窗口平台（**首个注册生效**，后续注册忽略——防 Sample bootstrap 的 CEF 平台
    /// 覆盖测试泵先注册的 Windows 平台，或库场景重复注册）。
    /// </summary>
    /// <param name="impl">平台实现。</param>
    public static void Register(IPlatform impl) => _current ??= impl;
}
