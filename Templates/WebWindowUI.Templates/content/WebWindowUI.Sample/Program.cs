using WebWindowUI.Core;

namespace WebWindowUI.Sample;

/// <summary>
/// 主窗口：绑定 MainModel（模型双向绑定 + MVVM 命令）。对应前端 src/window/main/。
/// </summary>
internal sealed class MainWindow : WebWindow
{
    public MainWindow(MainModel model)
        : base(new WebWindowOptions("main") { Title = "WebWindowUI 应用", Width = 800, Height = 600 })
    {
        Model = model;
    }
}

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // 前端页面 src/window/main/（Vue + Vite 产物经 BuildFrontend 直产本工程 wwwroot）。
        MainWindow window = new(new MainModel());
        window.Show();

        WebWindowUIPlatform.Run();
    }
}
