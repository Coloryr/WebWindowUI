using CommunityToolkit.Mvvm.ComponentModel;
using WebWindowUI.Core;

namespace WebWindowUI.Demo.Monitor;

/// <summary>
/// 一条进程：名称 + PID + CPU 占用% + 内存 MB。作为 MonitorModel.Processes 的元素（typed repeated，列表元素双向）。
/// </summary>
public partial class ProcessModel : WebWindowModel
{
    [ObservableProperty]
    public partial string Name { get; set; } = "";

    [ObservableProperty]
    public partial int Pid { get; set; }

    [ObservableProperty]
    public partial double Cpu { get; set; }

    [ObservableProperty]
    public partial double Memory { get; set; }
}
