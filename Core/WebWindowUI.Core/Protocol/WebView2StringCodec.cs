using System.Text;

namespace WebWindowUI.Core.Protocol;

/// <summary>
/// WebView2 字符串通道字节编解码：0x00 → "\0"、0x5C → "\\"、其余 1:1（该通道在首个 NUL 截断）；前端桥同算法。
/// </summary>
internal static class WebView2StringCodec
{
    /// <summary>
    /// 字节 → 不含 NUL 的 Latin-1 字符串。
    /// </summary>
    /// <param name="bytes">protobuf 字节。</param>
    /// <returns>编码字符串。</returns>
    public static string Encode(byte[] bytes)
    {
        var sb = new StringBuilder(bytes.Length);
        foreach (var b in bytes)
        {
            if (b == 0x00)
            {
                sb.Append('\\');
                sb.Append('0');
            }
            else if (b == 0x5C)
            {
                sb.Append('\\');
                sb.Append('\\');
            }
            else
            {
                sb.Append((char)b);
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// Encode 的逆操作：还原回字节；结尾孤立转义符静默丢弃。
    /// </summary>
    /// <param name="s">编码字符串。</param>
    /// <returns>还原的字节。</returns>
    public static byte[] Decode(string s)
    {
        var bytes = new List<byte>(s.Length);
        for (var i = 0; i < s.Length; i++)
        {
            var c = s[i];
            if (c == '\\')
            {
                if (i + 1 >= s.Length)
                    break; // 结尾孤立转义符：畸形，丢弃
                var n = s[++i];
                bytes.Add(n == '0' ? (byte)0x00 : (byte)n); // "\\0"→0x00，"\\\"→0x5C
            }
            else
            {
                bytes.Add((byte)c);
            }
        }
        return [.. bytes];
    }
}
