using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WebWindowUI.Core;

namespace WebWindowUI.Sample;

/// <summary>
/// 综合演示窗口（demo）的目录模型：承载「切换功能 tab」与「打开多窗口共享演示」命令，
/// 事件由综合窗口控制器订阅分派（切 Window.Model / 开子窗口）。
/// </summary>
public partial class DemoModel : WebWindowModel
{
    /// <summary>
    /// 当前激活的功能 tab 名（回锚目录时展示）。
    /// </summary>
    [ObservableProperty] public partial string CurrentTab { get; set; } = "home";

    /// <summary>
    /// 请求切换功能 tab（参数 = 功能名，如 "todos"；"home" 回锚目录）。
    /// </summary>
    public event Action<string>? SwitchRequested;

    /// <summary>
    /// 请求打开多窗口共享演示（开共享 A/B + 独立共 3 个子窗口）。
    /// </summary>
    public event Action? MultiRequested;

    /// <summary>
    /// 切换功能 tab 命令（前端 tab 点击调用）。
    /// </summary>
    /// <param name="name">功能名。</param>
    [RelayCommand] public void SwitchModel(string name) => SwitchRequested?.Invoke(name);

    /// <summary>
    /// 打开多窗口共享演示命令（前端 multi tab 按钮调用）。
    /// </summary>
    [RelayCommand] public void OpenMulti() => MultiRequested?.Invoke();
}
