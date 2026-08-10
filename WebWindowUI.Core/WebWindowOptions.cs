namespace WebWindowUI;

/// <summary>跨平台窗口配置：自定义 scheme、首页地址和静态资源提供者。</summary>
public sealed class WebWindowOptions
{
    /// <summary>自定义协议 scheme，如 "app"。</summary>
    public string Scheme { get; set; } = "app";

    /// <summary>首页地址。经 <see cref="WebWindow"/> 创建窗口时自动重写为 scheme://localhost/window/&lt;窗口路径&gt;/index.html。</summary>
    public string HomeUrl { get; set; } = "app://localhost/index.html";

    /// <summary>
    /// 静态资源提供者：给定相对路径（正斜杠分隔），返回资源流；找不到返回 null。
    /// 默认使用 <see cref="WebResourceResolver.Resolve"/>（类库内置的 wwwroot 资源）；
    /// 设为 null 可禁用自定义 scheme 的资源路由。
    /// </summary>
    public Func<string, Stream?>? ResourceResolver { get; set; } = WebResourceResolver.Resolve;

    /// <summary>
    /// 数据通道 scheme，如 "appbin"。与 UI 静态资源（<see cref="Scheme"/>）分开，
    /// 专门托管大块/二进制数据（图片、视频、blob 等），避免混入 UI 资源的命名空间。
    /// 为空时表示不启用数据通道。
    /// </summary>
    public string? DataScheme { get; set; } = "appbin";

    /// <summary>
    /// 数据通道资源提供者：给定相对路径返回流，找不到返回 null。
    /// 与 <see cref="ResourceResolver"/> 一样按 <c>DataScheme://host/路径</c> 请求调用。
    /// 默认 null（不提供数据通道资源，请求返回 404）。
    /// </summary>
    public Func<string, Stream?>? DataResolver { get; set; }

    /// <summary>
    /// 无头模式：窗口永不显示（不出现在屏幕/任务栏），但 WebView 完全可用——导航、DOM、
    /// ExecuteScriptAsync、postMessage 双向通道全部照常。用于自动化测试等不需要可见窗口的场景。
    /// Chromium 侧页面仍认为可见（控制器 IsVisible 保持默认 true），定时器/rAF 正常运转。
    /// </summary>
    public bool Headless { get; set; }
}
