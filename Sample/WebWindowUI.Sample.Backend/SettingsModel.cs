using CommunityToolkit.Mvvm.ComponentModel;
using WebWindowUI.Core;

namespace WebWindowUI.Sample;

/// <summary>
/// 同步模式（生成器把它当非标量类型 → ModelValue 兜底，前端以 number 呈现）。
/// </summary>
public enum SyncMode { Auto, Manual }

/// <summary>
/// 设置窗口的数据模型，覆盖生成器支持的多类类型（string/bool/int/double/long/Guid/DateTime/TimeSpan/
/// 枚举/List&lt;string&gt;/object）。属性用 [ObservableProperty] 生成，任何变化自动推送给前端 Vue。
/// </summary>
public partial class SettingsModel : WebWindowModel
{
    /// <summary>
    /// string：主题（浅色/深色）。
    /// </summary>
    [ObservableProperty]
    public partial string Theme { get; set; } = "light";

    /// <summary>
    /// bool：自动保存。
    /// </summary>
    [ObservableProperty]
    public partial bool AutoSave { get; set; } = true;

    /// <summary>
    /// int：每页最大条目数。
    /// </summary>
    [ObservableProperty]
    public partial int MaxItems { get; set; } = 50;

    /// <summary>
    /// double：同步进度。
    /// </summary>
    [ObservableProperty]
    public partial double Progress { get; set; } = 0.75;

    /// <summary>
    /// long（int64）：已同步的字节数。
    /// </summary>
    [ObservableProperty]
    public partial long TotalBytes { get; set; } = 1_048_576L;

    /// <summary>
    /// Guid → string：实例标识。
    /// </summary>
    [ObservableProperty]
    public partial Guid InstanceId { get; set; } = Guid.NewGuid();

    /// <summary>
    /// DateTime → string：上次备份时间（示例定时器每 3 秒推送一次）。
    /// </summary>
    [ObservableProperty]
    public partial DateTime LastBackup { get; set; } = DateTime.Now;

    /// <summary>
    /// TimeSpan → string：保留历史时长。
    /// </summary>
    [ObservableProperty]
    public partial TimeSpan KeepHistory { get; set; } = TimeSpan.FromDays(7);

    /// <summary>
    /// 枚举 → ModelValue 兜底：同步模式。
    /// </summary>
    [ObservableProperty]
    public partial SyncMode SyncMode { get; set; } = SyncMode.Auto;

    /// <summary>
    /// List&lt;string&gt; → repeated string：标签集合。
    /// </summary>
    [ObservableProperty]
    public partial List<string> Tags { get; set; } = new() { "bridge", "protobuf" };

    /// <summary>
    /// object（Dictionary）→ ModelValue：扩展配置。
    /// </summary>
    [ObservableProperty]
    public partial object? Config { get; set; } = new Dictionary<string, object>
    {
        ["proxy"] = "auto",
        ["timeout"] = 30,
    };
}
