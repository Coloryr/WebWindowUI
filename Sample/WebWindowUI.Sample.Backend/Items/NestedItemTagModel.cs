using CommunityToolkit.Mvvm.ComponentModel;
using WebWindowUI.Core;

namespace WebWindowUI.Sample.Items;

/// <summary>
/// 列表元素的内层嵌套标签（NestedListItemModel.Tags 的元素）：typed repeated 嵌套（List&lt;Model&gt;
/// 里的 List&lt;Model&gt;），在不同层级都可强类型双向绑定。
/// </summary>
public partial class NestedItemTagModel : WebWindowModel
{
    /// <summary>
    /// 标签名（proto 字段号 1）。
    /// </summary>
    [ObservableProperty]
    public partial string Name { get; set; } = "";
}
