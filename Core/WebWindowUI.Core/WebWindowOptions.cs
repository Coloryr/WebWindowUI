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
    /// 窗口启动宽度
    /// </summary>
    public int Width = 1280;
    /// <summary>
    /// 窗口启动高度
    /// </summary>
    public int Height = 800;

    public WebWindowOptions(string path)
    {
        WindowPath = path;
    }
}
