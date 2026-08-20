namespace WebWindowUI.Core;

/// <summary>
/// 窗口配置
/// </summary>
public record WebWindowOptions
{
    /// <summary>
    /// 无头模式
    /// </summary>
    public bool Headless;
    /// <summary>
    /// 窗口标题
    /// </summary>
    public string Title;
    /// <summary>
    /// 窗口路径
    /// </summary>
    public string WindowPath;
    /// <summary>
    /// 附加到首页 URL 的 query（如 "model=settings"；null/空则不加）
    /// </summary>
    public string? Query;
    /// <summary>
    /// 窗口启动宽度
    /// </summary>
    public int Width = 1280;
    /// <summary>
    /// 窗口启动高度
    /// </summary>
    public int Height = 800;

    /// <summary>
    /// 创建窗口选项。
    /// </summary>
    /// <param name="path">窗口路径（对应前端 src/window/&lt;窗口路径&gt;/）。</param>
    public WebWindowOptions(string path)
    {
        WindowPath = path;
    }
}
