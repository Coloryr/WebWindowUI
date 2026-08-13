using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;
using WebWindowUI.Core;
using WebWindowUI.Sample.Items;

namespace WebWindowUI.Sample;

/// <summary>
/// 待办列表窗口的数据模型，演示「List&lt;Model&gt; 在 Vue 层一一对应」这一个功能。
/// todos 是 ObservableCollection&lt;TodoItemModel&gt;：生成器产出 typed repeated 消息，前端强类型
/// TodoItemModel[] 逐元素绑定（v-for）。元素字段级修改（改标题/勾选）只回写该项（按
/// ModelInstanceId 寻址、原地写，保实例）；增删仍整列回写。.NET 侧定时器直接 .Add()/.Remove()——
/// 框架订阅了 CollectionChanged，原地增删自动差量补丁推送；直接改元素属性（如 todos[0].Done=true）
/// 由元素订阅逐元素 ElementSet 推送，无需整体替换属性。
/// </summary>
public partial class TodoListModel : WebWindowModel
{
    /// <summary>
    /// TodoItemModel 列表：前端强类型 TodoItemModel[]；勾选/改名只回写该项（元素级），增删整列回写。
    /// </summary>
    [ObservableProperty]
    public partial ObservableCollection<TodoItemModel> Todos { get; set; } = new();
}
