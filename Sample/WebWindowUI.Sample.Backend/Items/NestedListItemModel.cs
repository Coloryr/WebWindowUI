using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;
using WebWindowUI.Core;

namespace WebWindowUI.Sample.Items;

/// <summary>
/// 列表元素模型（示例「List&lt;&gt;嵌套窗口」）：NestedListModel.Items 的 typed repeated 元素，内部又嵌套
/// List&lt;NestedItemTagModel&gt;（Tags，typed repeated）与单模型 NestedItemMetaModel（Meta，ModelValue 兜底）。
/// 元素可绑到列表项详情子窗口：Tags 根层 typed repeated 增删改全双向，Meta 单 POCO 只读展示。
/// </summary>
public partial class NestedListItemModel : WebWindowModel
{
    /// <summary>
    /// 标题（proto 字段号 1）。
    /// </summary>
    [ObservableProperty]
    public partial string Title { get; set; } = "";

    /// <summary>
    /// 是否完成（proto 字段号 2）。
    /// </summary>
    [ObservableProperty]
    public partial bool Done { get; set; }

    /// <summary>
    /// 优先级（proto 字段号 3）。
    /// </summary>
    [ObservableProperty]
    public partial int Priority { get; set; }

    /// <summary>
    /// 内层标签列表：typed repeated（嵌套 List&lt;Model&gt;，proto 字段号 4）。
    /// </summary>
    [ObservableProperty]
    public partial ObservableCollection<NestedItemTagModel> Tags { get; set; } = new();

    /// <summary>
    /// 内层单模型（ModelValue 兜底 / 序数键，proto 字段号 5，只读展示）。
    /// </summary>
    [ObservableProperty]
    public partial NestedItemMetaModel? Meta { get; set; }
}
