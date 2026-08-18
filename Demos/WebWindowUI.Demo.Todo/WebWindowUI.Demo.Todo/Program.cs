using WebWindowUI.Core;

namespace WebWindowUI.Demo.Todo;

/// <summary>
/// 待办事项主窗口控制器：经 CreateWindow 建窗，绑定 TodoListModel
/// （typed List&lt;Model&gt; 双向 + MVVM 命令 + 磁盘持久化）。对应前端 src/window/main/。
/// </summary>
internal sealed class MainWindow
{
    /// <summary>
    /// 框架窗口（构造即创建，Show 由宿主负责）。
    /// </summary>
    public WebWindow Window { get; }

    public MainWindow(TodoListModel model)
    {
        Window = WebWindowPlatform.Current.CreateWindow(new WebWindowOptions("main")
        {
            Title = "待办事项",
            Width = 860,
            Height = 640
        });
        Window.Model = model;
    }
}

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // 启动即从 %LocalAppData%\WebWindowUI.Demo.Todo\todos.json 加载历史任务。
        TodoListModel model = new();
        new MainWindow(model).Window.Show();

        WebWindowUIPlatform.Run();
    }
}
