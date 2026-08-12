namespace WebWindowUI.Core;

/// <summary>
/// 窗口图标
/// </summary>
public sealed class WindowIcon
{
    /// <summary>
    /// 图标数据流
    /// </summary>
    public MemoryStream Stream { get; } = new();

    /// <summary>
    /// 从文件创建窗口图标
    /// </summary>
    /// <param name="path">文件路径</param>
    /// <returns>窗口图标</returns>
    public static WindowIcon FromFile(string path)
    {
        using var file = File.OpenRead(path);

        return FromStream(file);
    }

    /// <summary>
    /// 从数据流创建窗口图标
    /// </summary>
    /// <param name="stream">数据流</param>
    /// <returns>窗口图标</returns>
    public static WindowIcon FromStream(Stream stream)
    {
        var icon = new WindowIcon();

        stream.CopyTo(icon.Stream);
        icon.Stream.Seek(0, SeekOrigin.Begin);

        return icon;
    }
}
