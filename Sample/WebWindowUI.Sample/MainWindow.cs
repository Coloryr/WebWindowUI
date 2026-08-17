using WebWindowUI.Core;

namespace WebWindowUI.Sample;

/// <summary>
/// 主窗口控制器：经 <see cref="WebWindowPlatform.Current.CreateWindow"/> 建窗（不再继承 WebWindow），
/// 绑定模型与推送定时器。对应前端 src/window/main/。
/// 定时器每秒递增 Count、每 5 秒改写 Message，演示 .NET → 前端实时推送；
/// 前端输入框回写 Name/Extra 写回模型（双向绑定）。
/// </summary>
internal sealed class MainWindow
{
    private readonly Timer _timer;

    /// <summary>
    /// 框架窗口（构造即创建；Model 绑定与 Show 由宿主负责）。
    /// </summary>
    public WebWindow Window { get; }

    public MainWindow()
    {
        Window = WebWindowPlatform.Current.CreateWindow(new WebWindowOptions("main")
        {
            Title = "主窗口",
            Width = 800,
            Height = 640
        });

        var model = new MainWindowModel();
        Window.Model = model;

        _timer = new Timer(_ =>
        {
            model.Count++;
            if (model.Count % 5 == 0)
                model.Message = $"已运行 {model.Count} 秒";
        }, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));

        Window.Closed += (_, _) => _timer.Dispose();
    }
}
