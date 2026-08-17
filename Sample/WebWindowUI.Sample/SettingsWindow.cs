using WebWindowUI.Core;

namespace WebWindowUI.Sample;

/// <summary>
/// 设置窗口控制器：经 <see cref="WebWindowPlatform.Current.CreateWindow"/> 建窗，
/// 绑定 SettingsModel（多类型模型）。对应前端 src/window/settings/。
/// </summary>
internal sealed class SettingsWindow
{
    private readonly Timer _timer;

    /// <summary>
    /// 框架窗口（构造即创建；Model 绑定与 Show 由宿主负责）。
    /// </summary>
    public WebWindow Window { get; }

    public SettingsWindow()
    {
        Window = WebWindowPlatform.Current.CreateWindow(new WebWindowOptions("settings")
        {
            Title = "设置",
            Width = 900,
            Height = 600
        });

        SettingsModel model = new();
        Window.Model = model;

        _timer = new Timer(_ =>
        {
            model.LastBackup = DateTime.Now;
        }, null, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(3));

        Window.Closed += (_, _) => _timer.Dispose();
    }
}
