using WebWindowUI.Core;
using Xunit;

namespace WebWindowUI.Tests;

/// <summary>WebWindow 窗口模式的纯逻辑测试：窗口路径 → 首页地址推导（不创建窗口）。</summary>
public class WebWindowTests
{
    [Theory]
    [InlineData("app", "main", "app://localhost/window/main/index.html")]
    [InlineData("app", "main/", "app://localhost/window/main/index.html")]
    [InlineData("app", "/main", "app://localhost/window/main/index.html")]
    [InlineData("app", " main ", "app://localhost/window/main/index.html")]
    [InlineData("app", "deep/path", "app://localhost/window/deep/path/index.html")]
    [InlineData("app", "/about/", "app://localhost/window/about/index.html")]
    [InlineData("myapp", "settings", "myapp://localhost/window/settings/index.html")]
    [InlineData("app", "", "app://localhost/window/index.html")]
    [InlineData("app", null, "app://localhost/window/index.html")]
    public void BuildHomeUrl_DerivesFromSchemeAndWindowPath(string? scheme, string? windowPath, string expected)
    {
        Assert.Equal(expected, WebWindow.BuildHomeUrl(scheme!, windowPath!));
    }
}
