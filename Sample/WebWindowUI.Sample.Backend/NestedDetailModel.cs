using CommunityToolkit.Mvvm.ComponentModel;
using WebWindowUI.Core;

namespace WebWindowUI.Sample;

/// <summary>
/// 嵌套模型（示例「模型嵌套窗口」）：既是 <see cref="NestedParentModel.Detail"/> 的嵌套属性值，
/// 又作为嵌套详情子窗口（src/window/nested-detail/）的窗口模型——同一个实例被父窗口以
/// ModelValue 兜底（单 POCO 属性 → 序数键）展示、被子窗口强类型绑定编辑。
/// </summary>
public partial class NestedDetailModel : WebWindowModel
{
    /// <summary>名称（proto 字段号 1）。</summary>
    [ObservableProperty]
    public partial string Name { get; set; } = "";

    /// <summary>层级（proto 字段号 2）。</summary>
    [ObservableProperty]
    public partial int Level { get; set; }
}
