using WebWindowUI.Core;
using WebWindowUI.Sample.Items;

namespace WebWindowUI.Sample;

/// <summary>
/// 待办列表窗口控制器：经 <see cref="WebWindowPlatform.Current.CreateWindow"/> 建窗，
/// 绑定 TodoListModel。对应前端 src/window/todos/。
/// 定时器每 8 秒直接 model.Todos.Add()：ObservableCollection 增删触发 CollectionChanged，
/// 框架订阅后自动整列表推送（无需整体替换列表属性）。
/// </summary>
internal sealed class TodosWindow
{
    private readonly Timer _timer;
    private int _autoTodo;

    /// <summary>
    /// 框架窗口（构造即创建；Model 绑定与 Show 由宿主负责）。
    /// </summary>
    public WebWindow Window { get; }

    public TodosWindow()
    {
        Window = WebWindowPlatform.Current.CreateWindow(new WebWindowOptions("todos")
        {
            Title = "待办列表",
            Width = 820,
            Height = 640
        });

        TodoListModel model = new();
        Window.Model = model;

        _timer = new Timer(_ =>
        {
            model.Todos.Add(new TodoItemModel { Title = $"自动任务 {++_autoTodo}", Done = _autoTodo % 2 == 0 });
        }, null, TimeSpan.FromSeconds(8), TimeSpan.FromSeconds(8));

        Window.Closed += (_, _) => _timer.Dispose();
    }
}
