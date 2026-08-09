using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using WebWindowUI.Sample.Items;

namespace WebWindowUI.Sample;

/// <summary>
/// 待办列表窗口的数据模型，演示「List&lt;Model&gt; 在 Vue 层一一对应」这一个功能。
/// todos 是 ObservableCollection&lt;TodoItemModel&gt;：生成器产出 typed repeated 消息，前端强类型
/// TodoItemModel[] 逐元素绑定（v-for）；前端增删改整列表回写 .NET，.NET 侧定时器
/// 直接 .Add()/.Remove()——框架订阅了 CollectionChanged，原地增删自动整列表推送（无需整体替换属性）。
/// </summary>
public partial class TodoListModel : WebWindowModel
{
    /// <summary>TodoItemModel 列表：前端强类型 TodoItemModel[]，勾选/改名/增删即整列表回写。</summary>
    [ObservableProperty]
    public partial ObservableCollection<TodoItemModel> Todos { get; set; } = new();
}
