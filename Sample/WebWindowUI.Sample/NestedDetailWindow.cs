using WebWindowUI.Core;

namespace WebWindowUI.Sample;

/// <summary>
/// 嵌套详情子窗口：对应前端 src/window/nested-detail/。由 NestedWindow 打开，绑定父窗口的
/// 同一个 NestedDetailModel 实例——实例既是父窗口的嵌套属性值、又是本窗口的根模型，强类型双向编辑。
/// </summary>
internal sealed class NestedDetailWindow : WebWindow
{
    public NestedDetailWindow(NestedDetailModel model) : base(new WebWindowOptions("nested-detail")
    {
        Title = "嵌套详情",
        Width = 640,
        Height = 520
    })
    {
        Model = model;
    }
}
