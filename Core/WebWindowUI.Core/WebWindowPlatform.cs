namespace WebWindowUI.Core;

/// <summary>
/// 平台入口（纯注册表，平台无关）；平台实现由入口包按 OS 静态选择并注册，消费方 Main 首行调用后可用。
/// </summary>
public static class WebWindowPlatform
{
    /// <summary>
    /// 当前平台的 WebView 实现；未注册时抛异常。
    /// </summary>
    public static IWebWindowPlatform Current { get; private set; }

    /// <summary>
    /// 注册窗口平台（首个注册生效）。
    /// </summary>
    /// <param name="impl">平台实现。</param>
    public static void Register(IWebWindowPlatform impl) => Current = impl;
}
