using System.Runtime.InteropServices;
using System.Text;
using WebWindowUI.Core.Platform;

namespace WebWindowUI.Natives.Windows;

/// <summary>
/// Win32 剪贴板实现：文本/URL（CF_UNICODETEXT）、HTML（CF_HTML）、文件列表（CF_HDROP）、
/// 位图（CF_DIB）、自定义（RegisterClipboardFormatW）。
/// </summary>
public class Win32Clipboard : IClipboard
{
    /// <summary>
    /// 单例。
    /// </summary>
    public static readonly Win32Clipboard Instance = new();

    /// <summary>
    /// 已注册的自定义格式（key → 格式号）。
    /// </summary>
    private readonly Dictionary<string, uint> _customFormats = [];

    /// <summary>
    /// 注册自定义剪贴板格式（供 Custom 类型读写）。
    /// </summary>
    /// <param name="key">格式名。</param>
    public void RegisterCustomData(string key)
    {
        lock (_customFormats)
        {
            if (!_customFormats.ContainsKey(key))
                _customFormats[key] = Win32.RegisterClipboardFormatW(key);
        }
    }

    /// <summary>
    /// 写入剪贴板：先清空再按数据类型写对应格式。
    /// </summary>
    /// <param name="data">剪贴板数据。</param>
    public void SetClipboardData(ClipboardData data)
    {
        if (!Win32.OpenClipboard(IntPtr.Zero))
            throw new InvalidOperationException("打开剪贴板失败。");
        try
        {
            Win32.EmptyClipboard();
            switch (data)
            {
                case ClipboardTextData d:
                    SetUnicodeText(d.Text);
                    break;
                case ClipboardUrlData d:
                    SetUnicodeText(d.Url.ToString());
                    break;
                case ClipboardHtmlData d:
                    SetHtml(d.Html);
                    break;
                case ClipboardFilesData d:
                    SetFiles(d.Files);
                    break;
                case ClipboardBitmapData d:
                    SetBitmap(d.Bitmap);
                    break;
                case ClipboardCustomData d:
                    SetCustom(d);
                    break;
            }
        }
        finally
        {
            Win32.CloseClipboard();
        }
    }

    /// <summary>
    /// 读取剪贴板：按可用格式优先序（文件/HTML/文本/自定义/位图）返回数据。
    /// 剪贴板为空时返回 null。
    /// </summary>
    /// <returns>剪贴板数据；空剪贴板为 null。</returns>
    public ClipboardData? GetClipboardData()
    {
        if (!Win32.OpenClipboard(IntPtr.Zero))
            return null;
        try
        {
            if (Win32.IsClipboardFormatAvailable(Win32.CF_HDROP))
            {
                return GetFiles();
            }
            if (Win32.IsClipboardFormatAvailable(_htmlFormat))
            {
                return new ClipboardHtmlData { Html = GetHtml(), Type = ClipboardDataType.Html };
            }
            if (Win32.IsClipboardFormatAvailable(Win32.CF_UNICODETEXT))
            {
                var text = GetUnicodeText();
                if (Uri.TryCreate(text, UriKind.Absolute, out var url)
                    && url.Scheme is "http" or "https" or "ftp" or "file")
                    return new ClipboardUrlData { Url = url, Type = ClipboardDataType.Url };
                return new ClipboardTextData { Text = text, Type = ClipboardDataType.Text };
            }
            foreach (var (key, format) in _customFormats)
            {
                if (Win32.IsClipboardFormatAvailable(format))
                    return new ClipboardCustomData { Custom = GetCustom(format), Type = ClipboardDataType.Custom };
            }
            if (Win32.IsClipboardFormatAvailable(Win32.CF_DIB))
            {
                return new ClipboardBitmapData { Bitmap = GetDib(), Type = ClipboardDataType.Bitmap };
            }
            return null;
        }
        finally
        {
            Win32.CloseClipboard();
        }
    }

    /// <summary>
    /// CF_HTML 格式号（"HTML Format"，首次使用时注册）。
    /// </summary>
    private uint _htmlFormat;

    /// <summary>
    /// CF_HTML 格式号（惰性注册）。
    /// </summary>
    private uint HtmlFormat => _htmlFormat != 0 ? _htmlFormat : (_htmlFormat = Win32.RegisterClipboardFormatW("HTML Format"));

    /// <summary>
    /// 以 CF_UNICODETEXT 写入字符串。
    /// </summary>
    /// <param name="text">文本。</param>
    private static void SetUnicodeText(string text)
    {
        var bytes = Encoding.Unicode.GetBytes(text + "\0");
        SetHGlobal(Win32.CF_UNICODETEXT, bytes);
    }

    /// <summary>
    /// 读取 CF_UNICODETEXT 字符串。
    /// </summary>
    /// <returns>文本。</returns>
    private string GetUnicodeText()
    {
        IntPtr h = Win32.GetClipboardData(Win32.CF_UNICODETEXT);
        return ReadHGlobalString(h);
    }

    /// <summary>
    /// 以 CF_HTML 写入 HTML（带 Version/StartHTML 等标准头）。
    /// </summary>
    /// <param name="html">HTML 片段。</param>
    private void SetHtml(string html)
    {
        var full = $"<html><body><!--StartFragment-->{html}<!--EndFragment--></body></html>";
        string header =
            "Version:0.9\r\n" +
            "StartHTML:0000000000\r\n" +
            "EndHTML:0000000000\r\n" +
            "StartFragment:0000000000\r\n" +
            "EndFragment:0000000000\r\n";
        int startHtml = header.Length;
        int startFragment = startHtml + full.IndexOf("<!--StartFragment-->", StringComparison.Ordinal) + "<!--StartFragment-->".Length;
        int endFragment = startHtml + full.IndexOf("<!--EndFragment-->", StringComparison.Ordinal);
        int endHtml = startHtml + full.Length;
        header =
            $"Version:0.9\r\nStartHTML:{startHtml:0000000000}\r\nEndHTML:{endHtml:0000000000}\r\n" +
            $"StartFragment:{startFragment:0000000000}\r\nEndFragment:{endFragment:0000000000}\r\n";
        var bytes = Encoding.Unicode.GetBytes(header + full + "\0");
        SetHGlobal(HtmlFormat, bytes);
    }

    /// <summary>
    /// 读取 CF_HTML 并剥离标准头，返回 HTML 主体。
    /// </summary>
    /// <returns>HTML。</returns>
    private string GetHtml()
    {
        IntPtr h = Win32.GetClipboardData(_htmlFormat);
        string raw = ReadHGlobalString(h);
        int start = raw.IndexOf("<html>", StringComparison.OrdinalIgnoreCase);
        return start < 0 ? raw : raw[start..];
    }

    /// <summary>
    /// 以 CF_HDROP 写入文件列表（DROPFILES + UTF-16 双 NUL 结尾路径）。
    /// </summary>
    /// <param name="files">文件路径列表。</param>
    private static void SetFiles(List<string> files)
    {
        int offset = Marshal.SizeOf<Win32.DROPFILES>();
        int totalChars = 1; // 结束双 NUL
        foreach (var f in files)
            totalChars += f.Length + 1;
        int totalBytes = offset + totalChars * 2;
        IntPtr h = Win32.GlobalAlloc(Win32.GMEM_MOVEABLE | Win32.GMEM_ZEROINIT, (nuint)totalBytes);
        IntPtr p = Win32.GlobalLock(h);
        try
        {
            Marshal.WriteInt32(p, offset);  // pFiles
            Marshal.WriteInt32(p, 16, 1);   // fWide = true（UTF-16）
            int charPos = offset / 2;
            foreach (var f in files)
            {
                foreach (char c in f)
                    Marshal.WriteInt16(p, charPos++ * 2, (short)c);
                Marshal.WriteInt16(p, charPos++ * 2, 0);
            }
            Marshal.WriteInt16(p, charPos * 2, 0); // 结束双 NUL
        }
        finally
        {
            Win32.GlobalUnlock(p);
        }
        Win32.SetClipboardData(Win32.CF_HDROP, h); // 成功后系统接管
    }

    /// <summary>
    /// 读取 CF_HDROP 文件列表。
    /// </summary>
    /// <returns>文件路径列表。</returns>
    private static ClipboardFilesData GetFiles()
    {
        IntPtr h = Win32.GetClipboardData(Win32.CF_HDROP);
        IntPtr p = Win32.GlobalLock(h);
        try
        {
            int offset = Marshal.ReadInt32(p);
            int bytePos = offset;
            var files = new List<string>();
            var sb = new StringBuilder();
            while (true)
            {
                char c = (char)Marshal.ReadInt16(p, bytePos);
                bytePos += 2;
                if (c == '\0')
                {
                    if (sb.Length == 0)
                        break; // 双 NUL = 列表结束
                    files.Add(sb.ToString());
                    sb.Clear();
                }
                else
                {
                    sb.Append(c);
                }
            }
            return new ClipboardFilesData { Files = files, Type = ClipboardDataType.Files };
        }
        finally
        {
            Win32.GlobalUnlock(p);
        }
    }

    /// <summary>
    /// 以 CF_DIB 写入位图（byte[] 为 BMP 文件字节，剥掉 BITMAPFILEHEADER）。
    /// </summary>
    /// <param name="bmp">BMP 文件字节。</param>
    private static void SetBitmap(byte[] bmp)
    {
        if (bmp.Length > 14)
        {
            var dib = new byte[bmp.Length - 14];
            Buffer.BlockCopy(bmp, 14, dib, 0, dib.Length);
            SetHGlobal(Win32.CF_DIB, dib);
        }
        else
        {
            SetHGlobal(Win32.CF_DIB, bmp);
        }
    }

    /// <summary>
    /// 读取 CF_DIB 位图并封装成 BMP 文件字节（补 BITMAPFILEHEADER）。
    /// </summary>
    /// <returns>BMP 文件字节。</returns>
    private static byte[] GetDib()
    {
        IntPtr h = Win32.GetClipboardData(Win32.CF_DIB);
        nuint size = Win32.GlobalSize(h);
        IntPtr p = Win32.GlobalLock(h);
        try
        {
            int dibSize = checked((int)size);
            var dib = new byte[dibSize];
            Marshal.Copy(p, dib, 0, dibSize);

            int headerSize = Marshal.ReadInt32(p);
            int offBits = 14 + headerSize; // 像素数据紧接 BITMAPINFOHEADER 后（无调色板时）
            int fileSize = 14 + dibSize;
            var bmp = new byte[fileSize];
            // BITMAPFILEHEADER（14 字节，小端）
            bmp[0] = (byte)'B'; bmp[1] = (byte)'M';
            bmp[2] = (byte)(fileSize & 0xFF); bmp[3] = (byte)((fileSize >> 8) & 0xFF);
            bmp[4] = (byte)((fileSize >> 16) & 0xFF); bmp[5] = (byte)((fileSize >> 24) & 0xFF);
            bmp[10] = (byte)(offBits & 0xFF); bmp[11] = (byte)((offBits >> 8) & 0xFF);
            bmp[12] = (byte)((offBits >> 16) & 0xFF); bmp[13] = (byte)((offBits >> 24) & 0xFF);
            Buffer.BlockCopy(dib, 0, bmp, 14, dibSize);
            return bmp;
        }
        finally
        {
            Win32.GlobalUnlock(p);
        }
    }

    /// <summary>
    /// 以自定义注册格式写入数据（Custom 对象字符串化）。
    /// </summary>
    /// <param name="data">自定义数据。</param>
    private void SetCustom(ClipboardCustomData data)
    {
        var key = _customFormats.FirstOrDefault(kv => kv.Value != 0).Key;
        uint format = key is null ? 0 : _customFormats[key];
        if (format == 0)
            return;
        string value = data.Custom?.ToString() ?? "";
        var bytes = Encoding.Unicode.GetBytes(value + "\0");
        SetHGlobal(format, bytes);
    }

    /// <summary>
    /// 读取自定义格式数据。
    /// </summary>
    /// <param name="format">格式号。</param>
    /// <returns>字符串数据。</returns>
    private static string GetCustom(uint format)
    {
        IntPtr h = Win32.GetClipboardData(format);
        return ReadHGlobalString(h);
    }

    /// <summary>
    /// 分配 HGLOBAL 并拷入字节，SetClipboardData 后交给系统（不再释放）。
    /// </summary>
    /// <param name="format">剪贴板格式。</param>
    /// <param name="bytes">数据。</param>
    private static void SetHGlobal(uint format, byte[] bytes)
    {
        IntPtr h = Win32.GlobalAlloc(Win32.GMEM_MOVEABLE, (nuint)bytes.Length);
        IntPtr p = Win32.GlobalLock(h);
        try
        {
            Marshal.Copy(bytes, 0, p, bytes.Length);
        }
        finally
        {
            Win32.GlobalUnlock(p);
        }
        Win32.SetClipboardData(format, h);
    }

    /// <summary>
    /// 读取 HGLOBAL 字符串（UTF-16，到 NUL 为止）。
    /// </summary>
    /// <param name="h">HGLOBAL 句柄。</param>
    /// <returns>字符串。</returns>
    private static string ReadHGlobalString(IntPtr h)
    {
        if (h == IntPtr.Zero)
            return "";
        IntPtr p = Win32.GlobalLock(h);
        try
        {
            nuint size = Win32.GlobalSize(h);
            int chars = checked((int)(size / 2));
            var sb = new StringBuilder(chars);
            for (int i = 0; i < chars; i++)
            {
                char c = (char)Marshal.ReadInt16(p, i * 2);
                if (c == '\0')
                    break;
                sb.Append(c);
            }
            return sb.ToString();
        }
        finally
        {
            Win32.GlobalUnlock(p);
        }
    }
}
