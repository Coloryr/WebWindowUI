namespace WebWindowUI.Demo.Todo;

/// <summary>
/// 待办事项主窗口：绑定 TodoListModel（typed List&lt;Model&gt; 双向 + MVVM 命令 + 磁盘持久化）。
/// 对应前端 src/window/main/。
/// </summary>
internal sealed class MainWindow : WebWindow
{
    public MainWindow(TodoListModel model)
        : base("main", "待办事项", width: 860, height: 640)
    {
        Model = model;
    }
}

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        WebWindowUI.Platform.EnsureRegistered();

        // 启动即从 %LocalAppData%\WebWindowUI.Demo.Todo\todos.json 加载历史任务。
        TodoListModel model = new();
        MainWindow window = new(model);
        window.Show();

        WebWindow.RunMessageLoop();
    }
}
