using WebWindowUI.Core;
using WebWindowUI.Sample.Items;

namespace WebWindowUI.Sample;

/// <summary>
/// 列表项详情子窗口控制器：经 <see cref="WebWindowPlatform.Current.CreateWindow"/> 建窗。
/// 对应前端 src/window/nested-list-item/。由 NestedListWindow 打开，绑定父列表的同一个
/// NestedListItemModel 元素实例——元素既是父列表的 typed repeated 元素、又是本窗口的根模型，
/// 其内层 Tags（List&lt;NestedItemTagModel&gt;）在子窗口是根层 typed repeated → 增删改全部双向。
/// </summary>
internal sealed class NestedListItemWindow
{
    /// <summary>
    /// 框架窗口（构造即创建；Model 绑定与 Show 由宿主负责）。
    /// </summary>
    public WebWindow Window { get; }

    public NestedListItemWindow(NestedListItemModel model)
    {
        Window = WebWindowPlatform.Current.CreateWindow(new WebWindowOptions("nested-list-item")
        {
            Title = "列表项详情",
            Width = 660,
            Height = 560
        });

        Window.Model = model;
    }
}
