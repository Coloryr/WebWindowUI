using CommunityToolkit.Mvvm.ComponentModel;
using WebWindowUI.Core;

namespace WebWindowUI.Sample.Items;

/// <summary>
/// 列表元素模型：作为 TodoListModel.Todos 的元素（typed repeated）。元素带 ModelInstanceId，
/// 前端按 id 只回写该项、.NET 侧原地写保实例；.NET 侧改元素属性逐元素推送。
/// 文件名以 Model.cs 结尾，被 targets 自动发现并生成快照/增量/TS 镜像。
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
}
