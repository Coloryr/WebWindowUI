using CommunityToolkit.Mvvm.ComponentModel;
using WebWindowUI.Core;

namespace WebWindowUI.Sample;

/// <summary>
/// 主窗口的数据模型，演示「模型双向绑定」这一个功能。
/// 属性用 [ObservableProperty] 生成（CommunityToolkit.Mvvm），
/// 任何属性变化都会自动推送给前端 Vue，前端回写也会写回这里。
/// </summary>
public partial class MainWindowModel : WebWindowModel
{
    /// <summary>与前端输入框双向绑定。</summary>
    [ObservableProperty]
    public partial string Name { get; set; } = "小明";

    /// <summary>由 .NET 定时器每秒更新，实时推送到前端。</summary>
    [ObservableProperty]
    public partial int Count { get; set; }

    /// <summary>由 .NET 定时器定期改写，推送到前端。</summary>
    [ObservableProperty]
    public partial string Message { get; set; } = "来自 .NET 的消息";

    /// <summary>object 属性示例：值必须是可 JSON 序列化/反序列化的（此处用字典）。</summary>
    [ObservableProperty]
    public partial object? Extra { get; set; } = new Dictionary<string, object>
    {
        ["lang"] = "zh-CN",
        ["theme"] = "light",
    };
}
