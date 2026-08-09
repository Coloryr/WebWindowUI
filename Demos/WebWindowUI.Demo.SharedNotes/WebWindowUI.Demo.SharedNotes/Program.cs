namespace WebWindowUI.Demo.SharedNotes;

/// <summary>
/// 便签编辑窗口：输入 + 发送 + 完整列表。与「监看」窗口共享同一个 NotesModel 实例。
/// 对应前端 src/window/main/。
/// </summary>
internal sealed class MainWindow : WebWindow
{
    public MainWindow(NotesModel model)
        : base("main", "共享便签 · 编辑", width: 820, height: 620)
    {
        Model = model;
    }
}

/// <summary>
/// 监看窗口：只读实时便签墙。绑定与「编辑」窗口相同的模型实例 → 编辑窗口的改动全广播，这里实时跟随。
/// 对应前端 src/window/monitor/。
/// </summary>
internal sealed class MonitorWindow : WebWindow
{
    public MonitorWindow(NotesModel model)
        : base("monitor", "共享便签 · 监看", width: 640, height: 560)
    {
        Model = model;
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
        new MainWindow(model).Show();
        new MonitorWindow(model).Show();

        // 运行当前平台的消息循环（Windows 上是 Win32），直到最后一个窗口关闭
        WebWindow.RunMessageLoop();
    }
}
