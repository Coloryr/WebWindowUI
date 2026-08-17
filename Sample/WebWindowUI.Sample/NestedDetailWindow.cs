using WebWindowUI.Core;

namespace WebWindowUI.Sample;

/// <summary>
/// 嵌套详情子窗口控制器：经 <see cref="WebWindowPlatform.Current.CreateWindow"/> 建窗。
/// 对应前端 src/window/nested-detail/。由 NestedWindow 打开，绑定父窗口的同一个
/// NestedDetailModel 实例——实例既是父窗口的嵌套属性值、又是本窗口的根模型，强类型双向编辑。
/// </summary>
internal sealed class NestedDetailWindow
{
    /// <summary>
    /// 框架窗口（构造即创建；Model 绑定与 Show 由宿主负责）。
    /// </summary>
    public WebWindow Window { get; }

    public NestedDetailWindow(NestedDetailModel model)
    {
        Window = WebWindowPlatform.Current.CreateWindow(new WebWindowOptions("nested-detail")
        {
            Title = "嵌套详情",
            Width = 640,
            Height = 520
        });

        Window.Model = model;
    }
}
