using WebWindowUI.Core;
using WebWindowUI.Sample.Items;

namespace WebWindowUI.Sample;

/// <summary>
/// 待办列表窗口：演示「List&lt;Model&gt; 在 Vue 层一一对应」。对应前端 src/window/todos/。
/// 定时器每 8 秒直接 model.Todos.Add()：ObservableCollection 增删触发 CollectionChanged，
/// 框架订阅后自动整列表推送（无需整体替换列表属性）。
/// </summary>
internal sealed class TodosWindow : WebWindow
{
    private readonly Timer _timer;
    private int _autoTodo;

    public TodosWindow() : base(new WebWindowOptions("todos")
    { 
        Title = "待办列表",
        Width = 820,
        Height = 640
    })
    {
        TodoListModel model = new();
        Model = model;

        _timer = new Timer(_ =>
        {
            model.Todos.Add(new TodoItemModel { Title = $"自动任务 {++_autoTodo}", Done = _autoTodo % 2 == 0 });
        }, null, TimeSpan.FromSeconds(8), TimeSpan.FromSeconds(8));

        Closed += TodosWindow_Closed;
    }

    private void TodosWindow_Closed()
    {
        _timer.Dispose();
    }
}
