using CommunityToolkit.Mvvm.ComponentModel;

namespace WebWindowUI.Demo.Todo;

/// <summary>
/// 待办条目模型：作为 <see cref="WebWindowUI.Demo.Todo.TodoListModel.Items"/> 的元素
/// （typed repeated List&lt;TodoItemModel&gt;），前端以强类型 <c>TodoItemModel[]</c> 逐元素 v-for 一一对应，
/// 勾选/增删即整列表回写 .NET。
/// </summary>
public partial class TodoItemModel : WebWindowModel
{
    /// <summary>
    /// 任务标题。
    /// </summary>
    [ObservableProperty]
    public partial string Title { get; set; } = "";

    /// <summary>
    /// 是否完成。
    /// </summary>
    [ObservableProperty]
    public partial bool Done { get; set; }

    /// <summary>
    /// 优先级 1-3（1 高）。
    /// </summary>
    [ObservableProperty]
    public partial int Priority { get; set; } = 1;

    /// <summary>
    /// 创建时间（格式化字符串，与 .NET 时间戳对应）。
    /// </summary>
    [ObservableProperty]
    public partial string CreatedAt { get; set; } = DateTime.Now.ToString("MM-dd HH:mm");
}
