using WebWindowUI.Core;
using Xunit;

namespace WebWindowUI.Tests;

/// <summary>
/// 首页地址推导的纯逻辑测试（不创建窗口）。旧 <c>WebWindow.BuildHomeUrl(scheme, windowPath)</c>
/// 重构为 <see cref="WebWindowResource.GetWindowIndexUrl(windowPath)"/>（scheme 固定 app://localhost），
/// 归一化（trim/去尾斜杠）职责随路径解析一并移入 TryResolvePath。
/// </summary>
public class WebWindowTests
{
    [Theory]
    [InlineData("main", "app://localhost/window/main/index.html")]
    [InlineData("todos", "app://localhost/window/todos/index.html")]
    [InlineData("deep/path", "app://localhost/window/deep/path/index.html")]
    [InlineData("about", "app://localhost/window/about/index.html")]
    [InlineData("settings", "app://localhost/window/settings/index.html")]
    public void GetWindowIndexUrl_DerivesHomeUrlFromWindowPath(string windowPath, string expected)
    {
        Assert.Equal(expected, WebWindowResource.GetWindowIndexUrl(windowPath));
    }
}
