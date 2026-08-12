using System.Reflection;

namespace WebWindowUI.Core;

/// <summary>
/// 把自定义 scheme 的请求 URL 定位成相对路径与 MIME 类型。纯逻辑、不依赖平台 API，
/// 各平台实现复用它接管自己 webview 的资源请求。不接触文件系统。
/// </summary>
public static class WebWindowResource
{
    public const string Scheme = "app";
    public const string SchemeData = "appdata";
    public const string DefaultDocument = "index.html";

    /// <summary>
    /// 自定义路由。键 = DataScheme 的 host 段（RegisterCustomRoute("bin") ↔ appdata://bin/...），host 大小写不敏感。
    /// </summary>
    private readonly static Dictionary<string, IDataRoute> _customRoute = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 注册自定义路由，注册在 {SchemeData}://{url}/（url 即 URL 的 host 段）。
    /// </summary>
    /// <param name="url">路由 host 段，如 "bin" 后 appdata://bin/... 的请求交给 <paramref name="route"/></param>
    /// <param name="route">请求返回</param>
    public static void RegisterCustomRoute(string url, IDataRoute route)
    {
        _customRoute[url] = route;
    }

    /// <summary>
    /// 获取窗口页面路径
    /// </summary>
    /// <param name="path">窗口路径</param>
    /// <returns>页面路径</returns>
    public static string GetWindowIndexUrl(string path)
    {
        return $"{Scheme}://localhost/window/{path}/{DefaultDocument}";
    }

    /// <summary>
    /// 从scheme://host/路径中获取资源
    /// </summary>
    public static Stream? TryResolvePath(string uri, out string? relative, out string? mimeType)
    {
        relative = null;
        mimeType = null;

        var url = new Uri(uri);

        var path = Uri.UnescapeDataString(url.AbsolutePath).TrimStart('/');
        if (string.IsNullOrEmpty(path))
            path = DefaultDocument;

        // 目录请求（以 / 结尾）回退到默认文档
        if (path.EndsWith('/'))
            path += DefaultDocument;

        // 防止目录穿越：禁止任何 ".." 路径段
        var normalized = path.Replace('\\', '/');
        if (normalized.Split('/').Contains(".."))
            return null;

        relative = normalized;
        mimeType = GetMimeType(normalized);

        if (url.Scheme == Scheme)
        {
            return Resolve(relative);
        }
        else if (url.Scheme == SchemeData
            && _customRoute.TryGetValue(url.Host, out var route1))
        {
            return route1.ResolveBytes(path);
        }
        else
        {
            return null;
        }
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

    private static readonly Lock Sync = new();
    private static Assembly[]? _embeddedCandidates;

    /// <summary>
    /// 从wwwroot中获取资源
    /// </summary>
    /// <param name="relativePath"></param>
    /// <returns></returns>
    public static Stream? Resolve(string relativePath)
    {
        // 内嵌资源（Release）。查找名 = wwwroot\ + 相对路径（/ 转 \）。
        var embeddedName = "wwwroot\\" + relativePath.Replace('/', '\\');
        foreach (var asm in GetEmbeddedCandidates())
        {
            Stream? stream = asm.GetManifestResourceStream(embeddedName);
            if (stream is not null)
                return stream;
        }

        // 磁盘回退（Debug：wwwroot 直产产物目录）
        var root = Path.Combine(AppContext.BaseDirectory, "wwwroot");
        var full = Path.GetFullPath(Path.Combine(root, relativePath));
        // 防止目录穿越：解析结果必须落在 wwwroot 内
        if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            return null;
        return File.Exists(full) ? File.OpenRead(full) : null;
    }

    /// <summary>
    /// 含 wwwroot 嵌入资源的已加载程序集，结果缓存。Release 下前端 dll 已被强制加载，扫已加载程序集即命中。
    /// </summary>
    private static Assembly[] GetEmbeddedCandidates()
    {
        var candidates = _embeddedCandidates;
        if (candidates is not null)
            return candidates;

        lock (Sync)
        {
            if (_embeddedCandidates is not null)
                return _embeddedCandidates;

            var found = new List<Assembly>();
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                if (HasWwwrootResources(asm))
                    found.Add(asm);

            _embeddedCandidates = [.. found];
            return _embeddedCandidates;
        }
    }

    private static bool HasWwwrootResources(Assembly asm)
    {
        try
        {
            foreach (var name in asm.GetManifestResourceNames())
                if (name.StartsWith("wwwroot\\", StringComparison.Ordinal))
                    return true;
        }
        catch
        {
            // 反射失败（无托管资源表等）忽略
        }
        return false;
    }
}
