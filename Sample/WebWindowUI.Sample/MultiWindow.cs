using WebWindowUI.Core;

namespace WebWindowUI.Sample;

/// <summary>
/// 多窗口演示窗口：演示「一个 model 给多个窗口用，互不干扰」。对应前端 src/window/multi/。
/// 同一 MultiWindowModel 实例可绑多个窗口（共享A/B），任一窗口改动全广播、其余跟随；
/// 同类不同实例（独立窗口）各走各的。ownTimer=true 时每秒递增 Count（仅共享A / 独立窗口驱动）。
/// </summary>
internal sealed class MultiWindow : WebWindow
{
    private readonly Timer _timer;

    public MultiWindow(MultiWindowModel model, string title, bool ownTimer)
        : base(new WebWindowOptions("multi")
        {
            Title = title,
            Width = 780,
            Height = 640
        })
    {
        Model = model;
        if (!ownTimer)
            return;

        _timer = new Timer(_ => model.Count++, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));

        Closed += MultiWindow_Closed;
    }

    private void MultiWindow_Closed()
    {
        _timer.Dispose();
    }
}