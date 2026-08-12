using CommunityToolkit.Mvvm.ComponentModel;
using WebWindowUI.Core;

namespace WebWindowUI.Sample;

/// <summary>
/// 多窗口共享模型演示的数据模型，演示「一个 model 给多个窗口用，互不干扰」这一个功能。
/// 同一实例绑到多个窗口 → 属性变化全广播（任一窗口改动，其余跟随）；同类不同实例各自独立。
/// 窗口用 InstanceId 只读标签区分共享实例与独立实例（前端头部显示）。
/// </summary>
public partial class MultiWindowModel : WebWindowModel
{
    /// <summary>
    /// 可编辑字段：任一窗口回写后，共享实例的其余窗口经广播同步。
    /// </summary>
    [ObservableProperty]
    public partial string Name { get; set; } = "共享模型";

    /// <summary>
    /// 由共享窗口 A（ownTimer）每秒递增，广播给所有绑定窗口。
    /// </summary>
    [ObservableProperty]
    public partial int Count { get; set; }

    private readonly string _instanceId;

    public MultiWindowModel(string instanceId = "共享")
    {
        _instanceId = instanceId;
    }

    /// <summary>
    /// 只读标签属性：前端不能回写（无 setter），快照照常下发。
    /// </summary>
    public string InstanceId
    {
        get => _instanceId;
    }
}
