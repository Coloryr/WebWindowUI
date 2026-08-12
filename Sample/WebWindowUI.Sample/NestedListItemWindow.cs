using WebWindowUI.Core;
using WebWindowUI.Sample.Items;

namespace WebWindowUI.Sample;

/// <summary>
/// 列表项详情子窗口：对应前端 src/window/nested-list-item/。由 NestedListWindow 打开，绑定父列表的
/// 同一个 NestedListItemModel 元素实例——元素既是父列表的 typed repeated 元素、又是本窗口的根模型，
/// 其内层 Tags（List&lt;NestedItemTagModel&gt;）在子窗口是根层 typed repeated → 增删改全部双向。
/// </summary>
internal sealed class NestedListItemWindow : WebWindow
{
    public NestedListItemWindow(NestedListItemModel model) : base(new WebWindowOptions("nested-list-item")
    {
        Title = "列表项详情",
        Width = 660,
        Height = 560
    })
    {
        Model = model;
    }
}
