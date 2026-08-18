using WebWindowUI;
using WebWindowUI.Core;

namespace WebWindowUI.Sample;

/// <summary>
/// 主窗口控制器：经 CreateWindow 建窗，绑定 MainModel（模型双向绑定 + MVVM 命令）。
/// 对应前端 src/window/main/。
/// </summary>
internal sealed class MainWindow
{
    /// <summary>
    /// 框架窗口（构造即创建，Show 由宿主负责）。
    /// </summary>
    public WebWindow Window { get; }

    public MainWindow(MainModel model)
    {
        Window = WebWindowPlatform.Current.CreateWindow(new WebWindowOptions("main")
        {
            Title = "WebWindowUI 应用",
            Width = 800,
            Height = 600
        });
        Window.Model = model;
    }
}

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // 前端页面 src/window/main/（Vue + Vite 产物经 BuildFrontend 直产本工程 wwwroot）。
        new MainWindow(new MainModel()).Window.Show();

        WebWindowUIPlatform.Run();
    }
}
