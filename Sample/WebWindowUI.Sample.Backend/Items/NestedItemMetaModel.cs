using CommunityToolkit.Mvvm.ComponentModel;
using WebWindowUI.Core;

namespace WebWindowUI.Sample.Items;

/// <summary>
/// 列表元素的内层嵌套单模型（示例「List&lt;&gt;嵌套窗口」）：<see cref="NestedListItemModel.Meta"/>。
/// 单 POCO 属性 → ModelValue 兜底（序数键），只读展示；编辑需回到子窗口的强类型绑定字段。
/// </summary>
public partial class NestedItemMetaModel : WebWindowModel
{
    /// <summary>作者（proto 字段号 1）。</summary>
    [ObservableProperty]
    public partial string Author { get; set; } = "";

    /// <summary>备注（proto 字段号 2）。</summary>
    [ObservableProperty]
    public partial string Note { get; set; } = "";
}
