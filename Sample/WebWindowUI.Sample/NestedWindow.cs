using WebWindowUI.Core;

namespace WebWindowUI.Sample;

/// <summary>
/// 模型嵌套窗口：对应前端 src/window/nested/。演示「模型里嵌套模型 + 嵌套详情子窗口」。
/// 父窗口绑定 NestedParentModel，其 Detail 是另一个 WebWindowModel 实例（单 POCO 属性 →
/// ModelValue 兜底/序数键，父窗口只读展示）；「打开嵌套详情」命令触发 OpenDetailRequested 事件，
/// 这里打开绑定同一个 Detail 实例的 NestedDetailWindow（master-detail）。子窗口编辑 Detail 内部字段
/// 时，NestedParentModel 重推整个 Detail，父窗口展示实时跟随（见 NestedParentModel.OnDetailChangedInner）。
/// </summary>
internal sealed class NestedWindow : WebWindow
{
    private readonly NestedParentModel _model;
    private NestedDetailWindow? _detailWindow;

    public NestedWindow() : base(new WebWindowOptions("nested")
    {
        Title = "模型嵌套窗口",
        Width = 820,
        Height = 640
    })
    {
        _model = new NestedParentModel
        {
            Title = "模型嵌套示例",
            Detail = new NestedDetailModel { Name = "初始嵌套模型", Level = 1 },
        };
        Model = _model;
        _model.OpenDetailRequested += OnOpenDetail;
        Closed += () => _model.OpenDetailRequested -= OnOpenDetail;
    }

    private void OnOpenDetail()
    {
        if (_model.Detail is null)
            return;

        // 复用未关闭的详情窗口；关闭后置空，下次点击重建（同一 Detail 实例，多窗口共享广播）。
        if (_detailWindow is null)
        {
            _detailWindow = new NestedDetailWindow(_model.Detail);
            _detailWindow.Closed += () => _detailWindow = null;
        }
        _detailWindow.Show();
        _detailWindow.Activate();
    }
}
