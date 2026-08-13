using WebWindowUI.Core;

namespace WebWindowUI.Sample;

/// <summary>
/// 主窗口：演示「模型双向绑定」。对应前端 src/window/main/。
/// ownTimer=true 时每秒递增 Count、每 5 秒改写 Message，演示 .NET → 前端实时推送；
/// 前端输入框回写 Name/Extra 写回模型（双向绑定）。
/// </summary>
internal sealed class MainWindow : WebWindow
{
    private readonly Timer _timer;

    public MainWindow() : base(new WebWindowOptions("main")
    {
        Title = "主窗口",
        Width = 800,
        Height = 640
    })
    {
        var model = new MainWindowModel();
        Model = model;

        _timer = new Timer(_ =>
        {
            model.Count++;
            if (model.Count % 5 == 0)
                model.Message = $"已运行 {model.Count} 秒";
        }, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));

        Closed += MainWindow_Closed;
    }

    private void MainWindow_Closed()
    {
        _timer.Dispose();
    }
}
