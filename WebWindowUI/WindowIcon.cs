namespace WebWindowUI;

/// <summary>
/// 窗口图标的来源：一个 .ico 文件路径，或内存中的图标数据流。
/// 平台实现负责加载并应用到窗口（标题栏 + 任务栏）。
/// </summary>
public sealed class WindowIcon
{
    private WindowIcon(string? filePath, Stream? stream)
    {
        FilePath = filePath;
        Stream = stream;
    }

    /// <summary>图标文件路径（Windows 上为 .ico）。</summary>
    public string? FilePath { get; }

    /// <summary>图标数据流（如 .ico 文件的字节）。</summary>
    public Stream? Stream { get; }

    /// <summary>从 .ico 文件创建窗口图标。</summary>
    public static WindowIcon FromFile(string path) => new(path, null);

    /// <summary>从图标数据流创建窗口图标。</summary>
    public static WindowIcon FromStream(Stream stream) => new(null, stream);
}
