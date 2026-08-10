using CommunityToolkit.Mvvm.ComponentModel;

namespace WebWindowUI.Sample;

/// <summary>
/// 关于窗口的数据模型。覆盖 string / DateTime(→string) / List&lt;string&gt; /
/// string[](→repeated string) / byte[](→bytes) / object(Dictionary)→ModelValue。
/// 静态信息，前端只读展示（前端仍可回写，这里不演示）。
/// </summary>
public partial class AboutModel : WebWindowModel
{
    /// <summary>string：应用名。</summary>
    [ObservableProperty]
    public partial string AppName { get; set; } = "WebWindowUI";

    /// <summary>string：版本号。</summary>
    [ObservableProperty]
    public partial string Version { get; set; } = "0.1.0";

    /// <summary>DateTime → string：构建日期。</summary>
    [ObservableProperty]
    public partial DateTime BuildDate { get; set; } = new(2026, 8, 7);

    /// <summary>string：仓库地址。</summary>
    [ObservableProperty]
    public partial string RepoUrl { get; set; } = "https://github.com/coloryr/webwindowui";

    /// <summary>List&lt;string&gt; → repeated string：贡献者。</summary>
    [ObservableProperty]
    public partial List<string> Contributors { get; set; } = new() { "Color_yr" };

    /// <summary>string[] → repeated string：功能特性。</summary>
    [ObservableProperty]
    public partial string[] Features { get; set; } = new[] { "多窗口", "protobuf 双向绑定", "WebView2" };

    /// <summary>byte[] → bytes：图标哈希。</summary>
    [ObservableProperty]
    public partial byte[] IconHash { get; set; } = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };

    /// <summary>object（Dictionary）→ ModelValue：附加元数据。</summary>
    [ObservableProperty]
    public partial object? Metadata { get; set; } = new Dictionary<string, object>
    {
        ["lang"] = "zh-CN",
    };
}
