using WebWindowUI.Core;

namespace WebWindowUI.Demo.Monitor;

/// <summary>
/// 监控主窗口控制器：经 CreateWindow 建窗，绑定 MonitorModel，实时 CPU/内存/时长 + 进程表。
/// 对应前端 src/window/main/。采样定时器由本控制器持有：线程池线程回调 model.SampleOnce()
/// （跨线程推送）；设置窗口改 PollIntervalMs 时这里订阅到 Settings 变化 → 重建定时器，间隔立即生效。
/// </summary>
internal sealed class MainWindow
{
    private readonly MonitorModel _model;
    private Timer? _timer;

    /// <summary>
    /// 框架窗口（构造即创建，Show 由宿主负责）。
    /// </summary>
    public WebWindow Window { get; }

    public MainWindow(MonitorModel model)
    {
        _model = model;
        Window = WebWindowPlatform.Current.CreateWindow(new WebWindowOptions("main")
        {
            Title = "系统监控",
            Width = 900,
            Height = 680
        });
        Window.Model = model;
        model.Settings.PropertyChanged += OnSettingsChanged;
        RestartTimer();
        Window.Closed += (_, _) => _timer?.Dispose();
    }

    private void OnSettingsChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MonitorSettingsModel.PollIntervalMs))
            RestartTimer();
    }

    private void RestartTimer()
    {
        _timer?.Dispose();
        _timer = new Timer(_ => _model.SampleOnce(), null,
            TimeSpan.Zero, TimeSpan.FromMilliseconds(_model.Settings.PollIntervalMs));
    }
}

/// <summary>
/// 设置窗口控制器：绑定 MonitorModel.Settings 子模型实例（master-detail）——它既是主窗口的嵌套属性值、
/// 又是本窗口的根模型，强类型双向编辑。改 PollIntervalMs 后主窗口收到 Settings 重推 → 重建定时器。
/// 对应前端 src/window/settings/。
/// </summary>
internal sealed class SettingsWindow
{
    /// <summary>
    /// 框架窗口（构造即创建，Show 由宿主负责）。
    /// </summary>
    public WebWindow Window { get; }

    public SettingsWindow(MonitorSettingsModel settings)
    {
        Window = WebWindowPlatform.Current.CreateWindow(new WebWindowOptions("settings")
        {
            Title = "监控设置",
            Width = 560,
            Height = 520
        });
        Window.Model = settings;
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
        new MainWindow(model).Window.Show();
        new SettingsWindow(model.Settings).Window.Show();

        WebWindowUIPlatform.Run();
    }
}
