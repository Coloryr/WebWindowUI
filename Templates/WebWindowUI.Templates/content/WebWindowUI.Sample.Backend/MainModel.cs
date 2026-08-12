using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WebWindowUI;

namespace WebWindowUI.Sample;

/// <summary>
/// 主窗口数据模型：演示「模型双向绑定」+「MVVM 命令」。
/// [ObservableProperty] 属性变化自动推送给前端 Vue，前端回写也写回这里。
/// </summary>
public partial class MainModel : WebWindowModel
{
    /// <summary>
    /// 与前端输入框双向绑定。
    /// </summary>
    [ObservableProperty]
    public partial string Title { get; set; } = "WebWindowUI 应用";

    /// <summary>
    /// 由前端命令更新，推送到前端。
    /// </summary>
    [ObservableProperty]
    public partial int Count { get; set; }

    /// <summary>
    /// 前端按钮调用 model.bump() → ModelInvoke → 执行本命令 → 推送回前端。
    /// </summary>
    [RelayCommand]
    public void Bump() => Count++;
}
