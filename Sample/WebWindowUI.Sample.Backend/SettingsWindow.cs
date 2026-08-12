using WebWindowUI.Core;

namespace WebWindowUI.Sample.Backend;

/// <summary>
/// 设置窗口：对应前端 src/window/settings/，绑定 SettingsModel（多类型模型）。
/// </summary>
internal sealed class SettingsWindow : WebWindow
{
    private readonly Timer _timer;

    public SettingsWindow() : base(new WebWindowOptions("settings")
    {
        Title = "设置",
        Width = 900,
        Height = 600
    })
    {
        SettingsModel model = new();
        Model = model;

        _timer = new Timer(_ =>
        {
            model.LastBackup = DateTime.Now;
        }, null, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(3));

        Closed += SettingsWindow_Closed;
    }

    private void SettingsWindow_Closed()
    {
        _timer.Dispose();
    }
}