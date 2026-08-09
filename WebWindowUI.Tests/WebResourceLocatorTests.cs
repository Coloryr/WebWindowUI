using Xunit;

namespace WebWindowUI.Tests;

/// <summary>WebResourceLocator 的纯逻辑测试（路径解析 + MIME，不访问文件系统）。</summary>
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
        bool ok = WebResourceLocator.TryResolvePath(uri, "app", out string? relative, out string? mimeType);

        Assert.True(ok);
        Assert.Equal(expectedRelative, relative);
        Assert.Equal(expectedMime, mimeType);
    }

    [Theory]
    [InlineData("https://example.com/index.html")]
    [InlineData("file:///C:/x.html")]
    [InlineData("app://localhost/..%5Csecret.txt")]
    [InlineData("app://localhost/%2e%2e%2Fsecret.txt")]
    [InlineData("app://localhost/a/..%2Fsecret.txt")]
    public void EscapingUrls_AreRejected(string uri)
    {
        bool ok = WebResourceLocator.TryResolvePath(uri, "app", out _, out _);
        Assert.False(ok);
    }

    /// <summary>
    /// 普通 ".." 段会被 Uri 在解析时规范化折叠到根目录内，
    /// 解析出的相对路径永远不会包含 ".." 段，即不会逃逸 wwwroot。
    /// </summary>
    [Theory]
    [InlineData("app://localhost/../secret.txt")]
    [InlineData("app://localhost/a/../secret.txt")]
    [InlineData("app://localhost/%2e%2e/secret.txt")]
    public void DotSegments_NeverEscapeRoot(string uri)
    {
        bool ok = WebResourceLocator.TryResolvePath(uri, "app", out string? relative, out _);

        Assert.True(ok);
        Assert.Equal("secret.txt", relative);
        Assert.DoesNotContain("..", relative!.Split('/'));
    }

    /// <summary>数据通道 scheme（appbin://）与 UI scheme 一样按路径解析，只是走独立的 resolver。</summary>
    [Theory]
    [InlineData("appbin://localhost/bin/blob.bin", "bin/blob.bin", "application/octet-stream")]
    [InlineData("appbin://localhost/bin/hello.txt", "bin/hello.txt", "text/plain; charset=utf-8")]
    public void DataScheme_ResolvesToRelativePathAndMime(string uri, string expectedRelative, string expectedMime)
    {
        bool ok = WebResourceLocator.TryResolvePath(uri, "appbin", out string? relative, out string? mimeType);

        Assert.True(ok);
        Assert.Equal(expectedRelative, relative);
        Assert.Equal(expectedMime, mimeType);
    }

    /// <summary>IsScheme 用于多 scheme 按请求分发：大小写不敏感，不误判其它 scheme。</summary>
    [Theory]
    [InlineData("appbin://localhost/x.bin", "appbin", true)]
    [InlineData("APP://localhost/x", "app", true)]
    [InlineData("app://localhost/x", "appbin", false)]
    [InlineData("http://localhost/x", "app", false)]
    [InlineData("https://localhost/x", "appbin", false)]
    public void IsScheme_MatchesByScheme(string uri, string scheme, bool expected)
    {
        Assert.Equal(expected, WebResourceLocator.IsScheme(uri, scheme));
    }
}
