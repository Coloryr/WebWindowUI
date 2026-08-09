namespace WebWindowUI;

/// <summary>
/// 把自定义 scheme 的请求 URL 定位成相对路径与 MIME 类型。
/// 纯逻辑、不依赖任何平台 API，各平台实现复用它来接管自己 webview 的资源请求。
/// 只做「URL → 资源定位」，不接触文件系统；拿到路径后再由资源提供者（如
/// <see cref="WebResourceResolver.Resolve"/>）读取内容。
/// </summary>
public static class WebResourceLocator
{
    private const string DefaultDocument = "index.html";

    /// <summary>判断绝对 URL 是否属于指定 scheme（大小写不敏感）。用于多 scheme 按请求分发。</summary>
    public static bool IsScheme(string uri, string? scheme)
    {
        if (string.IsNullOrEmpty(scheme))
            return false;
        return Uri.TryCreate(uri, UriKind.Absolute, out Uri? url)
            && string.Equals(url.Scheme, scheme, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 把 scheme://host/路径 解析成相对路径（正斜杠分隔）和 MIME 类型。
    /// 纯逻辑、不访问文件系统，便于单元测试。
    /// </summary>
    public static bool TryResolvePath(string uri, string scheme, out string? relative, out string? mimeType)
    {
        relative = null;
        mimeType = null;

        var url = new Uri(uri);
        if (!string.Equals(url.Scheme, scheme, StringComparison.OrdinalIgnoreCase))
            return false;

        string path = Uri.UnescapeDataString(url.AbsolutePath).TrimStart('/');
        if (string.IsNullOrEmpty(path))
            path = DefaultDocument;

        // 目录请求（以 / 结尾）回退到默认文档
        if (path.EndsWith('/'))
            path += DefaultDocument;

        // 防止目录穿越：禁止任何 ".." 路径段
        string normalized = path.Replace('\\', '/');
        if (normalized.Split('/').Contains(".."))
            return false;

        relative = normalized;
        mimeType = GetMimeType(normalized);
        return true;
    }

    public static string GetMimeType(string path)
        => Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".html" or ".htm" => "text/html; charset=utf-8",
            ".css" => "text/css",
            ".js" or ".mjs" => "application/javascript",
            ".json" => "application/json",
            ".svg" => "image/svg+xml",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".ico" => "image/x-icon",
            ".woff" => "font/woff",
            ".woff2" => "font/woff2",
            ".ttf" => "font/ttf",
            ".txt" => "text/plain; charset=utf-8",
            ".xml" => "application/xml",
            _ => "application/octet-stream",
        };
}
