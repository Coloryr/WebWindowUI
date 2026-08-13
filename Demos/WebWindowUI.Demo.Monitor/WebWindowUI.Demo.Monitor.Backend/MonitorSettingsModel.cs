using CommunityToolkit.Mvvm.ComponentModel;
using WebWindowUI.Core;

namespace WebWindowUI.Demo.Monitor;

/// <summary>
/// 监控设置：嵌套在 MonitorModel.Settings 里的子模型（单 POCO → ModelValue 下发/序数键），
/// 同时是设置窗口的根模型（master-detail 强类型双向编辑）。改 PollIntervalMs 后主窗口重建采样定时器，
/// 间隔立即生效。字段号声明序（序数键）：PollIntervalMs=1、MaxProcesses=2、ShowProcesses=3、Theme=4。
/// </summary>
public partial class MonitorSettingsModel : WebWindowModel
{
    /// <summary>
    /// 采样间隔（毫秒）。
    /// </summary>
    [ObservableProperty]
    public partial int PollIntervalMs { get; set; } = 1000;

    /// <summary>
    /// 进程表最多显示条数。
    /// </summary>
    [ObservableProperty]
    public partial int MaxProcesses { get; set; } = 8;

    /// <summary>
    /// 是否显示进程表。
    /// </summary>
    [ObservableProperty]
    public partial bool ShowProcesses { get; set; } = true;

    /// <summary>
    /// 主题（light / dark）。
    /// </summary>
    [ObservableProperty]
    public partial string Theme { get; set; } = "light";
}
