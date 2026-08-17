using WebWindowUI.Core;

namespace WebWindowUI.Sample;

/// <summary>
/// 资源与数据通道窗口控制器：经 <see cref="WebWindowPlatform.Current.CreateWindow"/> 建窗。
/// 演示 app://（UI 静态资源，WebResourceResolver）与 appbin://（专用数据通道，
/// DataProvider : DataRoute 自动注册到 appbin://bin/）。对应前端 src/window/resources/，本页不绑定模型。
/// </summary>
internal sealed class ResourcesWindow
{
    /// <summary>
    /// 框架窗口（构造即创建；Show 由宿主负责）。
    /// </summary>
    public WebWindow Window { get; }

    public ResourcesWindow()
    {
        Window = WebWindowPlatform.Current.CreateWindow(new WebWindowOptions("resources")
        {
            Title = "资源与数据通道",
            Width = 900,
            Height = 640
        });
    }
}
