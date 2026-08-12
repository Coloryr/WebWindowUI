using WebWindowUI.Core;

namespace WebWindowUI.Sample;

/// <summary>关于窗口：对应前端 src/window/about/，绑定 AboutModel。</summary>
internal sealed class AboutWindow : WebWindow
{
    public AboutWindow() : base(new WebWindowOptions("about")
    { 
        Title = "关于",
        Width = 700,
        Height = 500
    })
    {
        Model = new AboutModel();
    }
}
