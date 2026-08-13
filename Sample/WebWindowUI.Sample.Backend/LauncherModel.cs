using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WebWindowUI.Core;

namespace WebWindowUI.Sample;

/// <summary>
/// 入口（启动器）窗口数据模型，演示「前端按钮 → .NET 命令（MVVM Command）」。[RelayCommand] 方法
/// 源生成 ICommand，前端 TS 镜像调用命令方法 → 桥发 ModelInvoke → .NET 执行 → OpenRequested 事件开窗。
/// CommandWithArg 用 CanExecute = "ButtonEnable" 门控：为 false 时命令拒绝执行（前端按钮同步禁用）。
/// </summary>
public partial class LauncherModel : WebWindowModel
{
    /// <summary>
    /// 回写通道演示：值为要打开的窗口路径（"main"/"todos"/"multi"/…）；.NET 处理后清空。
    /// 与命令通道并存——前端 ModelSet 直接写属性（不经过命令）。
    /// </summary>
    [ObservableProperty]
    public partial string? Request { get; set; }

    /// <summary>
    /// CanExecute 门控源：为 false 时 CommandWithArg 命令拒绝执行，前端按钮禁用。
    /// </summary>
    [ObservableProperty]
    public partial bool ButtonEnable { get; set; }

    /// <summary>
    /// 命令执行结果事件：入口窗口（LauncherWindow）订阅，命令触发时携带窗口路径。
    /// 事件非 [ObservableProperty]、不被生成器收集，也不出现在快照/update——纯 .NET 侧命令逻辑出口。
    /// </summary>
    public event Action<string>? OpenRequested;

    /// <summary>
    /// 无参命令：打开主窗口。
    /// </summary>
    [RelayCommand]
    public void OpenWindow() => OpenRequested?.Invoke("main");

    /// <summary>
    /// 带参命令：打开指定路径的窗口（CanExecute = ButtonEnable 门控）。
    /// </summary>
    [RelayCommand(CanExecute = "ButtonEnable")]
    public void CommandWithArg(string arg) => OpenRequested?.Invoke(arg);
}
