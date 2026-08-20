using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WebWindowUI.Core;

namespace WebWindowUI.Sample;

/// <summary>
/// 平台特性窗口的数据模型（托盘/通知/剪贴板/对话框的宿主 API 演示）。
/// 前端按钮统一调 <c>platformAction(action)</c> 命令 → 触发 <see cref="PlatformRequested"/> 事件，
/// 由窗口控制器执行 IPlatform 调用并回写状态属性（增量推送前端）。
/// </summary>
public partial class PlatformModel : WebWindowModel
{
    /// <summary>
    /// 托盘提示文本（创建托盘时同步 SetTip）。
    /// </summary>
    [ObservableProperty]
    public partial string TrayTip { get; set; } = "WebWindowUI 示例托盘";

    /// <summary>
    /// 气泡标题。
    /// </summary>
    [ObservableProperty]
    public partial string BalloonTitle { get; set; } = "WebWindowUI";

    /// <summary>
    /// 气泡正文。
    /// </summary>
    [ObservableProperty]
    public partial string BalloonText { get; set; } = "托盘气泡通知（Windows 用 Shell_NotifyIcon，Linux 走 libnotify）";

    /// <summary>
    /// 通知正文。
    /// </summary>
    [ObservableProperty]
    public partial string NotificationText { get; set; } = "来自 WebWindowUI 的系统通知";

    /// <summary>
    /// 剪贴板文本（复制源 / 粘贴读回）。
    /// </summary>
    [ObservableProperty]
    public partial string ClipboardText { get; set; } = "";

    /// <summary>
    /// 最近事件文本（托盘点击/通知点击/对话框结果/剪贴板读回）。
    /// </summary>
    [ObservableProperty]
    public partial string LastEvent { get; set; } = "等待操作…";

    /// <summary>
    /// 托盘当前可见性（创建托盘后 true；窗口关闭时托盘自动移除）。
    /// </summary>
    [ObservableProperty]
    public partial bool TrayVisible { get; set; }

    /// <summary>
    /// 平台动作请求事件（命令统一出口）：参数为动作名（create-tray / delete-tray / toggle-tray /
    /// balloon / notify / message-box / open-file / save-file / copy / paste），窗口控制器订阅执行。
    /// 事件非 [ObservableProperty]、不被生成器收集——纯 .NET 侧命令逻辑出口。
    /// </summary>
    public event Action<string>? PlatformRequested;

    /// <summary>
    /// 带参命令：请求指定平台动作（动作名见 <see cref="PlatformRequested"/>）。
    /// </summary>
    [RelayCommand]
    public void PlatformAction(string action) => PlatformRequested?.Invoke(action);
}
