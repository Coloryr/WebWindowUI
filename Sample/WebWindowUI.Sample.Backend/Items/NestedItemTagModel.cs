using CommunityToolkit.Mvvm.ComponentModel;

namespace WebWindowUI.Sample.Items;

/// <summary>
/// 列表元素的内层嵌套标签（示例「List&lt;&gt;嵌套窗口」）：<see cref="NestedListItemModel.Tags"/> 的元素。
/// tags 在 NestedListItemModel 里是 typed repeated（List&lt;Model&gt; 里的 List&lt;Model&gt;）；
/// 当 NestedListItemModel 作为列表项详情子窗口的根模型时，tags 又是子窗口根层的 typed repeated——
/// 同一 List&lt;Model&gt; 在不同层级都可强类型双向绑定。
/// </summary>
public partial class NestedItemTagModel : WebWindowModel
{
    /// <summary>标签名（proto 字段号 1）。</summary>
    [ObservableProperty]
    public partial string Name { get; set; } = "";
}
