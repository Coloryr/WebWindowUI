using WebWindowUI.Core;

namespace WebWindowUI.Demo.SharedNotes;

/// <summary>
/// 便签编辑窗口控制器：输入 + 发送 + 完整列表。与「监看」窗口共享同一个 NotesModel 实例。
/// 对应前端 src/window/main/。
/// </summary>
internal sealed class MainWindow
{
    /// <summary>
    /// 框架窗口（构造即创建，Show 由宿主负责）。
    /// </summary>
    public WebWindow Window { get; }

    public MainWindow(NotesModel model)
    {
        Window = WebWindowPlatform.Current.CreateWindow(new WebWindowOptions("main")
        {
            Title = "共享便签 · 编辑",
            Width = 820,
            Height = 620
        });
        Window.Model = model;
    }
}

/// <summary>
/// 监看窗口控制器：只读实时便签墙。绑定与「编辑」窗口相同的模型实例 → 编辑窗口的改动全广播，这里实时跟随。
/// 对应前端 src/window/monitor/。
/// </summary>
internal sealed class MonitorWindow
{
    /// <summary>
    /// 框架窗口（构造即创建，Show 由宿主负责）。
    /// </summary>
    public WebWindow Window { get; }

    public MonitorWindow(NotesModel model)
    {
        Window = WebWindowPlatform.Current.CreateWindow(new WebWindowOptions("monitor")
        {
            Title = "共享便签 · 监看",
            Width = 640,
            Height = 560
        });
        Window.Model = model;
    }
}

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // 同一个 NotesModel 实例绑定两个窗口：任一窗口的发送/删除广播到所有订阅者，
        // 改动源窗口不重复接收（框架排除远程回写源），其它窗口实时跟随 —— 双屏共享便签本。
        NotesModel model = new();
        new MainWindow(model).Window.Show();
        new MonitorWindow(model).Window.Show();

        WebWindowUIPlatform.Run();
    }
}
