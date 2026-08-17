using WebWindowUI.Core;

namespace WebWindowUI.Sample;

/// <summary>
/// 模型嵌套窗口控制器：经 <see cref="WebWindowPlatform.Current.CreateWindow"/> 建窗。
/// 对应前端 src/window/nested/：演示「模型里嵌套模型 + 嵌套详情子窗口」。
/// 「打开嵌套详情」命令 → 打开绑定同一个 Detail 实例的 NestedDetailWindow（master-detail），
/// 子窗口编辑后父窗口展示实时跟随。
/// </summary>
internal sealed class NestedWindow
{
    private readonly NestedParentModel _model;
    private NestedDetailWindow? _detailWindow;

    /// <summary>
    /// 框架窗口（构造即创建；Model 绑定与 Show 由宿主负责）。
    /// </summary>
    public WebWindow Window { get; }

    public NestedWindow()
    {
        Window = WebWindowPlatform.Current.CreateWindow(new WebWindowOptions("nested")
        {
            Title = "模型嵌套窗口",
            Width = 820,
            Height = 640
        });

        _model = new NestedParentModel
        {
            Title = "模型嵌套示例",
            Detail = new NestedDetailModel { Name = "初始嵌套模型", Level = 1 },
        };
        Window.Model = _model;
        _model.OpenDetailRequested += OnOpenDetail;
        Window.Closed += (_, _) => _model.OpenDetailRequested -= OnOpenDetail;
    }

    private void OnOpenDetail()
    {
        if (_model.Detail is null)
            return;

        // 复用未关闭的详情窗口；关闭后置空，下次点击重建（同一 Detail 实例，多窗口共享广播）。
        if (_detailWindow is null)
        {
            _detailWindow = new NestedDetailWindow(_model.Detail);
            _detailWindow.Window.Closed += (_, _) => _detailWindow = null;
        }
        _detailWindow.Window.Show();
        _detailWindow.Window.Activate();
    }
}
