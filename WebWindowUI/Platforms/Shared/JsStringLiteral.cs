using System.Text;

namespace WebWindowUI;

/// <summary>
/// 把字符串放进 JS 字符串字面量（供 eval 注入 <c>window.wwuiReceive("...")</c> 用）。
/// 转义反斜杠、双引号、换行/回车/制表符，其余控制字符转 <c>\uXXXX</c>。
///
/// 调用方传入的是 <see cref="WebView2StringCodec.Encode"/> 的 NUL 转义码串（已含 <c>\0</c> 与 <c>\\</c>），
/// 这里把它们再转一层 JS 字面量转义：eval 还原回原码串，与前端桥的 escapedToBytes 对称。
/// 例：码串 <c>a\0"b</c> → 字面量 <c>"a\\0\"b"</c> → eval 后 JS 字符串值 <c>a\0"b</c>。
/// </summary>
internal static class JsStringLiteral
{
    /// <summary>产出双引号包裹的安全 JS 字符串字面量。</summary>
    public static string Quote(string value)
    {
        var sb = new StringBuilder(value.Length + 2);
        sb.Append('"');
        foreach (char c in value)
        {
            switch (c)
            {
                case '\\': sb.Append("\\\\"); break;
                case '"': sb.Append("\\\""); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 0x20)
                        sb.Append("\\u").Append(((int)c).ToString("x4"));
                    else
                        sb.Append(c);
                    break;
            }
        }
        sb.Append('"');
        return sb.ToString();
    }
}
