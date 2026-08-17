using System.Text;
using WebWindowUI.Core.Platform;

namespace WebWindowUI.Natives.Linux;

/// <summary>
/// GTK3 剪贴板实现（包装 internal <see cref="GtkNative"/>）：文本/URL 走 gtk_clipboard_set_text，
/// HTML/文件/位图/自定义走 gtk_clipboard_set_with_data 回调目标；位图按魔数探测 image/* 目标，
/// 自定义用注册 key 作目标名。全部调用须在 GTK 主线程。
/// </summary>
public class LinuxClipboard : IClipboard
{
    /// <summary>
    /// 单例。
    /// </summary>
    public static readonly LinuxClipboard Clipboard = new();

    /// <summary>
    /// 已注册的自定义目标名（key → 目标 atom 名）。
    /// </summary>
    private readonly List<string> _customKeys = [];

    /// <summary>
    /// 读侧探测的图片目标（按优先序）。
    /// </summary>
    private static readonly string[] ImageTargets = ["image/png", "image/jpeg", "image/gif", "image/bmp", "image/tiff"];

    /// <summary>
    /// 注册自定义剪贴板目标（key 即目标 atom 名，双方进程须同 key）。
    /// </summary>
    /// <param name="key">格式名。</param>
    public void RegisterCustomData(string key)
    {
        lock (_customKeys)
        {
            if (!_customKeys.Contains(key))
                _customKeys.Add(key);
        }
    }

    /// <summary>
    /// 写入剪贴板（覆盖现有内容）。
    /// </summary>
    /// <param name="data">剪贴板数据。</param>
    public void SetClipboardData(ClipboardData data)
    {
        switch (data)
        {
            case ClipboardTextData d:
                GtkNative.SetClipboardText(d.Text);
                break;
            case ClipboardUrlData d:
                GtkNative.SetClipboardText(d.Url.ToString());
                break;
            case ClipboardHtmlData d:
                GtkNative.SetClipboardTarget("text/html", Encoding.UTF8.GetBytes(d.Html));
                break;
            case ClipboardFilesData d:
                var uris = d.Files.Select(ToFileUri).ToList();
                GtkNative.SetClipboardTarget("text/uri-list", Encoding.UTF8.GetBytes(string.Join("\r\n", uris) + "\r\n"));
                break;
            case ClipboardBitmapData d:
                GtkNative.SetClipboardTarget(DetectImageTarget(d.Bitmap), d.Bitmap);
                break;
            case ClipboardCustomData d:
                SetCustom(d);
                break;
        }
    }

    /// <summary>
    /// 读取剪贴板：按可用目标优先序（文件/HTML/文本/自定义/位图）返回数据。空剪贴板返回 null。
    /// </summary>
    /// <returns>剪贴板数据；空剪贴板为 null。</returns>
    public ClipboardData? GetClipboardData()
    {
        var names = GtkNative.GetClipboardTargetNames();

        // 文件（text/uri-list）
        if (names.Contains("text/uri-list"))
        {
            var uris = GtkNative.GetClipboardUris();
            if (uris is not null)
            {
                var files = uris
                    .Select(ToLocalPath)
                    .Where(p => p is not null)
                    .Cast<string>()
                    .ToList();
                if (files.Count > 0)
                    return new ClipboardFilesData { Files = files, Type = ClipboardDataType.Files };
            }
        }

        // HTML
        if (names.Contains("text/html"))
        {
            var html = GtkNative.GetClipboardTargetBytes("text/html");
            if (html is not null)
                return new ClipboardHtmlData { Html = Encoding.UTF8.GetString(html), Type = ClipboardDataType.Html };
        }

        // 文本 / URL
        var text = GtkNative.GetClipboardText();
        if (text is not null)
        {
            if (Uri.TryCreate(text, UriKind.Absolute, out var url)
                && url.Scheme is "http" or "https" or "ftp" or "file")
                return new ClipboardUrlData { Url = url, Type = ClipboardDataType.Url };
            return new ClipboardTextData { Text = text, Type = ClipboardDataType.Text };
        }

        // 自定义（仅已注册 key）
        lock (_customKeys)
        {
            foreach (var key in _customKeys)
            {
                if (names.Contains(key))
                    return new ClipboardCustomData
                    {
                        Custom = ReadCustom(key),
                        Type = ClipboardDataType.Custom,
                    };
            }
        }

        // 位图（image/*）
        foreach (var target in ImageTargets)
        {
            if (names.Contains(target))
            {
                var bytes = GtkNative.GetClipboardTargetBytes(target);
                if (bytes is not null)
                    return new ClipboardBitmapData { Bitmap = bytes, Type = ClipboardDataType.Bitmap };
            }
        }

        return null;
    }

    /// <summary>
    /// 写自定义数据：内容字符串化后写第一个已注册 key 目标（Custom 数据不含 key，取登记序首个）。
    /// </summary>
    /// <param name="data">自定义数据。</param>
    private void SetCustom(ClipboardCustomData data)
    {
        lock (_customKeys)
        {
            if (_customKeys.Count == 0)
                return;
            string value = data.Custom?.ToString() ?? "";
            GtkNative.SetClipboardTarget(_customKeys[0], Encoding.UTF8.GetBytes(value));
        }
    }

    /// <summary>
    /// 读自定义数据（字符串）。
    /// </summary>
    /// <param name="key">目标名。</param>
    /// <returns>字符串数据。</returns>
    private static string ReadCustom(string key)
    {
        var bytes = GtkNative.GetClipboardTargetBytes(key);
        return bytes is null ? "" : Encoding.UTF8.GetString(bytes);
    }

    /// <summary>
    /// 按魔数探测图像目标名（PNG/JPEG/GIF/BMP/TIFF，未知按 PNG）。
    /// </summary>
    /// <param name="bytes">图像字节。</param>
    /// <returns>image/* 目标名。</returns>
    private static string DetectImageTarget(byte[] bytes)
    {
        if (bytes.Length >= 8 && bytes[0] == 0x89 && bytes[1] == (byte)'P' && bytes[2] == (byte)'N' && bytes[3] == (byte)'G')
            return "image/png";
        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
            return "image/jpeg";
        if (bytes.Length >= 6 && bytes[0] == (byte)'G' && bytes[1] == (byte)'I' && bytes[2] == (byte)'F')
            return "image/gif";
        if (bytes.Length >= 2 && bytes[0] == (byte)'B' && bytes[1] == (byte)'M')
            return "image/bmp";
        if (bytes.Length >= 4 && ((bytes[0] == (byte)'I' && bytes[1] == (byte)'I') || (bytes[0] == (byte)'M' && bytes[1] == (byte)'M')))
            return "image/tiff";
        return "image/png";
    }

    /// <summary>
    /// 本地路径转 file:// URI（空格等字符经 UriBuilder 转义）。
    /// </summary>
    /// <param name="path">本地路径。</param>
    /// <returns>file:// URI。</returns>
    private static string ToFileUri(string path)
        => new UriBuilder("file", "") { Path = path }.Uri.AbsoluteUri;

    /// <summary>
    /// file:// URI 转本地路径；非 file URI 原样返回。
    /// </summary>
    /// <param name="uri">URI。</param>
    /// <returns>本地路径；不可解析为 null。</returns>
    private static string? ToLocalPath(string uri)
    {
        if (!Uri.TryCreate(uri, UriKind.Absolute, out var u) || u.Scheme != "file")
            return uri;
        return u.LocalPath;
    }
}
