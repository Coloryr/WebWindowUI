using WebWindowUI.Core;
using Xunit;

namespace WebWindowUI.Tests;

/// <summary>
/// WebWindowResource 的纯逻辑测试（路径解析 + MIME，不访问文件系统）。
/// 适配 resolver 重构：TryResolvePath 改为 3 参（scheme 由 URI 自判），返回 Stream?
/// （app:// 需命中内嵌/磁盘资源，测试 bin 无 wwwroot → 只断言 out 参，不依赖流非空）。
/// </summary>
public class WebResourceLocatorTests
{
    [Theory]
    [InlineData("app://localhost/index.html", "index.html", "text/html; charset=utf-8")]
    [InlineData("app://localhost/", "index.html", "text/html; charset=utf-8")]
    [InlineData("app://localhost", "index.html", "text/html; charset=utf-8")]
    [InlineData("app://localhost/pages/", "pages/index.html", "text/html; charset=utf-8")]
    [InlineData("app://localhost/style.css", "style.css", "text/css")]
    [InlineData("app://localhost/app.js", "app.js", "application/javascript")]
    [InlineData("app://localhost/logo.svg", "logo.svg", "image/svg+xml")]
    [InlineData("app://localhost/data.json", "data.json", "application/json")]
    [InlineData("app://localhost/font.woff2", "font.woff2", "font/woff2")]
    [InlineData("app://localhost/unknown.xyz", "unknown.xyz", "application/octet-stream")]
    public void ValidUrls_ResolveToRelativePathAndMime(string uri, string expectedRelative, string expectedMime)
    {
        WebWindowResource.TryResolvePath(uri, out string? relative, out string? mimeType);

        Assert.Equal(expectedRelative, relative);
        Assert.Equal(expectedMime, mimeType);
    }

    [Theory]
    [InlineData("https://example.com/index.html")]
    [InlineData("file:///C:/x.html")]
    public void UnknownSchemes_AreNotHandled(string uri)
    {
        // 非 app/appdata scheme：resolver 不接管，返回 null 流（relative/mime 是解析中间产物）。
        Assert.Null(WebWindowResource.TryResolvePath(uri, out _, out _));
    }

    /// <summary>
    /// 字面 ".." 段会被 Uri 在解析时规范化折叠到根目录内，
    /// 解析出的相对路径永远不会包含 ".." 段，即不会逃逸 wwwroot。
    /// </summary>
    [Theory]
    [InlineData("app://localhost/../secret.txt")]
    [InlineData("app://localhost/a/../secret.txt")]
    [InlineData("app://localhost/%2e%2e/secret.txt")]
    public void DotSegments_FoldIntoRoot(string uri)
    {
        // 字面 ".." 与纯编码 ".."（%2e%2e 后跟字面斜杠）都会被 Uri 规范化折叠到根目录内，
        // 解析出的相对路径不含 ".."，即不会逃逸 wwwroot。
        WebWindowResource.TryResolvePath(uri, out string? relative, out _);

        Assert.Equal("secret.txt", relative);
    }

    /// <summary>
    /// 编码到跨段（%2f 编码斜杠 / %5c 反斜杠）的 ".." Uri 不折叠 → 落到穿越守卫，
    /// 解析不出相对路径、不触盘。
    /// </summary>
    [Theory]
    [InlineData("app://localhost/%2e%2e%2Fsecret.txt")]
    [InlineData("app://localhost/a/..%2Fsecret.txt")]
    [InlineData("app://localhost/..%5Csecret.txt")]
    public void EscapingUrls_AreRejected(string uri)
    {
        var stream = WebWindowResource.TryResolvePath(uri, out string? relative, out _);

        Assert.Null(relative);
        Assert.Null(stream);
    }

    /// <summary>
    /// 数据通道 scheme（appdata://）与 UI scheme 一样按路径解析，只是 host 段做路由键、
    /// 相对路径与 MIME 仍由同一套解析逻辑产出。
    /// </summary>
    [Theory]
    [InlineData("appdata://bin/blob.bin", "blob.bin", "application/octet-stream")]
    [InlineData("appdata://bin/hello.txt", "hello.txt", "text/plain; charset=utf-8")]
    public void DataScheme_ResolvesToRelativePathAndMime(string uri, string expectedRelative, string expectedMime)
    {
        WebWindowResource.TryResolvePath(uri, out string? relative, out string? mimeType);

        Assert.Equal(expectedRelative, relative);
        Assert.Equal(expectedMime, mimeType);
    }
}
