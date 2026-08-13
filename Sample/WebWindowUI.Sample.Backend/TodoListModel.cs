using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;
using WebWindowUI.Core;
using WebWindowUI.Sample.Items;

namespace WebWindowUI.Sample;

/// <summary>
/// 待办列表数据模型：Todos 是 typed repeated（ObservableCollection&lt;TodoItemModel&gt;），前端强类型
/// TodoItemModel[] 逐元素绑定。元素字段级修改只回写该项（按 ModelInstanceId 寻址、保实例）；
/// .NET 侧 .Add()/.Remove() 自动差量补丁推送，直接改元素属性逐元素 ElementSet 推送。
/// </summary>
public partial class TodoListModel : WebWindowModel
{
    /// <summary>
    /// TodoItemModel 列表：前端强类型 TodoItemModel[]；勾选/改名只回写该项（元素级），增删整列回写。
    /// </summary>
    [ObservableProperty]
    public partial ObservableCollection<TodoItemModel> Todos { get; set; } = new();
}
