using WebWindowUI.Core;

namespace WebWindowUI.Sample;

/// <summary>
/// 关于窗口控制器：经 <see cref="WebWindowPlatform.Current.CreateWindow"/> 建窗，
/// 绑定 AboutModel。对应前端 src/window/about/。
/// </summary>
internal sealed class AboutWindow
{
    /// <summary>
    /// 框架窗口（构造即创建；Model 绑定与 Show 由宿主负责）。
    /// </summary>
    public WebWindow Window { get; }

    public AboutWindow()
    {
        Window = WebWindowPlatform.Current.CreateWindow(new WebWindowOptions("about")
        {
            Title = "关于",
            Width = 700,
            Height = 500
        });

        Window.Model = new AboutModel();
    }
}
