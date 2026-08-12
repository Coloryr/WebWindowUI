using System.Text;

namespace WebWindowUI.Core.Protocol;

/// <summary>
/// WebView2 字符串消息通道的字节编解码。该通道在第一个 NUL（char code 0）处截断字符串，
/// 而 protobuf 字节普遍含 0x00，无法无损通过。本编解码把字节转成不含 NUL 的 Latin-1 字符串：
/// 0x00 → "\0"，0x5C（转义符本身）→ "\\"，其余字节 1:1。无 NUL 零膨胀，每个 NUL 只多 1 字符。
/// 前端桥（bytesToEscaped/escapedToBytes）实现同一算法，双向互通。
/// </summary>
internal static class WebView2StringCodec
{
    /// <summary>
    /// 字节 → 不含 NUL 的 Latin-1 字符串。
    /// </summary>
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
    /// Encode 的逆操作：还原回字节。畸形输入（结尾孤立转义符）静默丢弃该字符。
    /// </summary>
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
