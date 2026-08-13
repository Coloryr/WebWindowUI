using CommunityToolkit.Mvvm.ComponentModel;
using WebWindowUI.Core;

namespace WebWindowUI.Sample.Items;

/// <summary>
/// 列表元素模型：作为 <see cref="global::WebWindowUI.Sample.TodoListModel.Todos"/> 的元素（List&lt;TodoItemModel&gt;），
/// 前端以强类型 <c>TodoItemModel[]</c> 逐元素 v-for 一一对应。元素带进程内唯一 ModelInstanceId
/// （生成器合成的 modelInstanceId 字段 / ToModelValue 注入），前端勾选/改名按 id 只回写该项，
/// .NET 侧按 id 定位元素原地写（保实例）；.NET 侧直接改元素属性由元素订阅逐元素推送。
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
