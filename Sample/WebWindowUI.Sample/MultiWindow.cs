using WebWindowUI.Core;

namespace WebWindowUI.Sample;

/// <summary>
/// 多窗口演示窗口控制器：经 <see cref="WebWindowPlatform.Current.CreateWindow"/> 建窗。
/// 演示「一个 model 给多个窗口用，互不干扰」。对应前端 src/window/multi/。
/// 同一 MultiWindowModel 实例可绑多个窗口（共享A/B），任一窗口改动全广播、其余跟随；
/// 同类不同实例（独立窗口）各走各的。ownTimer=true 时每秒递增 Count（仅共享A / 独立窗口驱动）。
/// </summary>
internal sealed class MultiWindow
{
    private readonly Timer _timer;

    /// <summary>
    /// 框架窗口（构造即创建；Model 绑定与 Show 由宿主负责）。
    /// </summary>
    public WebWindow Window { get; }

    public MultiWindow(MultiWindowModel model, string title, bool ownTimer)
    {
        Window = WebWindowPlatform.Current.CreateWindow(new WebWindowOptions("multi")
        {
            Title = title,
            Width = 780,
            Height = 640
        });

        Window.Model = model;
        if (!ownTimer)
            return;

        _timer = new Timer(_ => model.Count++, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));

        Window.Closed += (_, _) => _timer.Dispose();
    }
}
