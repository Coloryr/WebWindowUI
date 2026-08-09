namespace WebWindowUI.Demo.Monitor;

/// <summary>
/// 监控主窗口：绑定 MonitorModel，实时 CPU/内存/时长 + 进程表。对应前端 src/window/main/。
/// 采样定时器由本窗口持有：线程池线程回调 model.SampleOnce()（跨线程推送）；设置窗口改 PollIntervalMs
/// 时这里订阅到 Settings 变化 → 重建定时器，间隔立即生效。
/// </summary>
internal sealed class MainWindow : WebWindow
{
    private Timer? _timer;

    public MainWindow(MonitorModel model)
        : base("main", "系统监控", width: 900, height: 680)
    {
        Model = model;
        model.Settings.PropertyChanged += OnSettingsChanged;
        RestartTimer(model);
    }

    private void OnSettingsChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MonitorSettingsModel.PollIntervalMs) && Model is MonitorModel model)
            RestartTimer(model);
    }

    private void RestartTimer(MonitorModel model)
    {
        _timer?.Dispose();
        _timer = new Timer(_ => model.SampleOnce(), null,
            TimeSpan.Zero, TimeSpan.FromMilliseconds(model.Settings.PollIntervalMs));
    }
}

/// <summary>
/// 设置窗口：绑定 MonitorModel.Settings 子模型实例（master-detail）——它既是主窗口的嵌套属性值、
/// 又是本窗口的根模型，强类型双向编辑。改 PollIntervalMs 后主窗口收到 Settings 重推 → 重建定时器。
/// 对应前端 src/window/settings/。
/// </summary>
internal sealed class SettingsWindow : WebWindow
{
    public SettingsWindow(MonitorSettingsModel settings)
        : base("settings", "监控设置", width: 560, height: 520)
    {
        Model = settings;
    }
}

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // 同一个 MonitorModel 实例给两个窗口用（master-detail）：
        //   main     绑定 MonitorModel（实时监控；Settings 是嵌套模型，主窗口 ordinal 翻译展示）
        //   settings 绑定 model.Settings 同一子实例（强类型编辑，改间隔即时生效）
        MonitorModel model = new();
        new MainWindow(model).Show();
        new SettingsWindow(model.Settings).Show();

        // 运行当前平台的消息循环（Windows 上是 Win32），直到最后一个窗口关闭
        WebWindow.RunMessageLoop();
    }
}
