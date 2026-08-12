namespace WebWindowUI.Core;

/// <summary>
/// 静态资源的缓存策略（各平台 webview 复用同一决策）。
/// vite 的 hash 构建产物（"名字-至少8位hex.扩展名"）内容与文件名绑定，可 immutable 长缓存；
/// index.html 与未 hash 的文件须 no-store，否则 webview 一直加载旧文件。
/// </summary>
internal static class ResourceHeaders
{
    /// <summary>
    /// 按相对路径给出 Cache-Control 头值。
    /// </summary>
    public static string CacheControl(string relative)
        => IsHashedAsset(relative) ? "public, max-age=31536000, immutable" : "no-store";

    /// <summary>
    /// CORS 跨源头（含 \r\n，可直接拼进响应 header 字符串）。app:// fetch appdata:// 属跨源，
    /// 缺 ACAO 则 fetch 被拦；同源场景无副作用，故全平台无条件回 *。
    /// </summary>
    public const string AccessControlAllowOrigin = "Access-Control-Allow-Origin: *\r\n";

    /// <summary>
    /// 是否构建工具的 hash 产物：文件名形如 "名字-至少8位hex.扩展名"（vite 默认资产输出）。
    /// </summary>
    public static bool IsHashedAsset(string relative)
    {
        var dot = relative.LastIndexOf('.');
        if (dot < 0)
            return false;
        var name = Path.GetFileName(relative[..dot]);
        var dash = name.LastIndexOf('-');
        if (dash < 0)
            return false;
        var hash = name[(dash + 1)..];
        return hash.Length >= 8 && hash.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F');
    }
}
