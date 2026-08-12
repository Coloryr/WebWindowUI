using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace WebWindowUI.Demo.Monitor;

/// <summary>
/// 系统监控模型：主窗口（main）绑定本实例；设置窗口（settings）绑定 Settings 子实例（master-detail）。
/// 采样定时器在线程池线程回调 model.SampleOnce()：跨线程设置模型属性/重建进程表（框架 PostMessage
/// 按线程 id marshal 回 UI 线程再推送前端）。设置窗口改 Settings.PollIntervalMs → 这里重推 Settings
/// 并重建定时器，间隔立即生效。
/// </summary>
public partial class MonitorModel : WebWindowModel
{
    private static readonly string[] KnownProcesses =
    {
        "WebWindowUI", "vite", "node", "msedgewebview2", "dotnet", "explorer",
        "Code", "WindowsTerminal", "svchost", "SearchHost",
    };

    private readonly DateTime _started = DateTime.Now;

    public MonitorModel()
    {
        Settings = new MonitorSettingsModel();
        // 嵌套设置子模型内部变化 → 整体重推 Settings（主窗口 ordinal 展示实时跟随）
        Settings.PropertyChanged += (_, _) => OnPropertyChanged(nameof(Settings));
        SampleOnce();
    }

    /// <summary>
    /// CPU 使用率（%）。
    /// </summary>
    [ObservableProperty]
    public partial double CpuUsage { get; set; }

    /// <summary>
    /// 内存占用（%）。
    /// </summary>
    [ObservableProperty]
    public partial double MemoryUsage { get; set; }

    /// <summary>
    /// 已运行时长（人类可读）。
    /// </summary>
    [ObservableProperty]
    public partial string Uptime { get; set; } = "0 秒";

    /// <summary>
    /// get-only ObservableCollection（免 [ObservableProperty]），原地清空重建自动推送整列表。
    /// </summary>
    public ObservableCollection<ProcessModel> Processes { get; } = new();

    /// <summary>
    /// 嵌套设置子模型（ModelValue 下发；设置窗口绑定同一实例强类型编辑）。
    /// </summary>
    [ObservableProperty]
    public partial MonitorSettingsModel Settings { get; set; }

    /// <summary>
    /// 每轮采样一次：刷新 CPU/内存/时长 + 重建进程表。可在线程池线程直接调用。
    /// </summary>
    public void SampleOnce()
    {
        TimeSpan up = DateTime.Now - _started;
        Uptime = $"{(int)up.TotalHours} 时 {(int)up.Minutes} 分 {(int)up.Seconds} 秒";

        // 模拟波动曲线（±8%），演示跨线程实时推送
        CpuUsage = Math.Clamp(CpuUsage + (Random.Shared.NextDouble() - 0.5) * 16, 0, 100);
        MemoryUsage = Math.Clamp(MemoryUsage + (Random.Shared.NextDouble() - 0.5) * 10, 0, 100);

        if (Settings.ShowProcesses)
            RefreshProcesses();
    }

    private void RefreshProcesses()
    {
        // 原地清空重建（ObservableCollection 保留订阅，增删自动推送整列表）
        Processes.Clear();
        int count = Math.Min(Settings.MaxProcesses, KnownProcesses.Length);
        for (int i = 0; i < count; i++)
        {
            Processes.Add(new ProcessModel
            {
                Name = i == 0 ? "WebWindowUI (本进程)" : KnownProcesses[i],
                Pid = 1000 + Random.Shared.Next(1, 30000),
                Cpu = Math.Round(Random.Shared.NextDouble() * 80, 1),
                Memory = Math.Round(80 + Random.Shared.NextDouble() * 1200, 1),
            });
        }
    }
}
