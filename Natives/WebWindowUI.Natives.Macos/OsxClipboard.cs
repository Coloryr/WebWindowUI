using System.Text;
using WebWindowUI.Core.Platform;

namespace WebWindowUI.Natives.Macos;

/// <summary>
/// macOS 剪贴板实现（NSPasteboard，主线程调用）。文件写 NSUrl 对象 / 读 public.file-url；
/// 位图按 PNG/JPEG/TIFF 数据格式 best-effort；自定义用注册 key 作 pasteboard type。
/// </summary>
public class OsxClipboard : IClipboard
{
    /// <summary>
    /// 单例。
    /// </summary>
    public static readonly OsxClipboard Clipboard = new();

    /// <summary>
    /// 已注册的自定义 pasteboard type（key → UTI 字符串）。
    /// </summary>
    private readonly List<string> _customTypes = [];

    /// <summary>
    /// 注册自定义剪贴板类型（key 即 pasteboard type，双方进程须同 key）。
    /// </summary>
    /// <param name="key">格式名。</param>
    public void RegisterCustomData(string key)
    {
        lock (_customTypes)
        {
            if (!_customTypes.Contains(key))
                _customTypes.Add(key);
        }
    }

    /// <summary>
    /// 写入剪贴板（覆盖现有内容）。
    /// </summary>
    /// <param name="data">剪贴板数据。</param>
    public void SetClipboardData(ClipboardData data)
    {
        var pb = NSPasteboard.GeneralPasteboard;
        pb.ClearContents();
        switch (data)
        {
            case ClipboardTextData d:
                pb.SetStringForType(d.Text, NSPasteboard.NSPasteboardTypeString);
                break;
            case ClipboardUrlData d:
                var url = d.Url.ToString();
                pb.SetStringForType(url, NSPasteboard.NSPasteboardTypeString);
                pb.SetStringForType(url, NSPasteboard.NSPasteboardTypeURL);
                break;
            case ClipboardHtmlData d:
                pb.SetStringForType(d.Html, NSPasteboard.NSPasteboardTypeHTML);
                break;
            case ClipboardFilesData d:
                var urls = d.Files.Select(NSUrl.FromFilename).ToArray();
                pb.WriteObjects(urls);
                break;
            case ClipboardBitmapData d:
                SetBitmap(pb, d.Bitmap);
                break;
            case ClipboardCustomData d:
                SetCustom(pb, d);
                break;
        }
    }

    /// <summary>
    /// 读取剪贴板：按可用类型优先序（文件/HTML/文本/URL/自定义/位图）；均不可用返回 null。
    /// </summary>
    /// <returns>剪贴板数据；空剪贴板为 null。</returns>
    public ClipboardData? GetClipboardData()
    {
        var pb = NSPasteboard.GeneralPasteboard;

        var files = ReadFileUrls(pb);
        if (files.Count > 0)
            return new ClipboardFilesData { Files = files, Type = ClipboardDataType.Files };

        var html = pb.GetStringForType(NSPasteboard.NSPasteboardTypeHTML);
        if (html is not null)
            return new ClipboardHtmlData { Html = html, Type = ClipboardDataType.Html };

        var text = pb.GetStringForType(NSPasteboard.NSPasteboardTypeString);
        if (text is not null)
        {
            if (Uri.TryCreate(text, UriKind.Absolute, out var u)
                && u.Scheme is "http" or "https" or "ftp" or "file")
                return new ClipboardUrlData { Url = u, Type = ClipboardDataType.Url };
            return new ClipboardTextData { Text = text, Type = ClipboardDataType.Text };
        }

        // 自定义（仅已注册 key）
        lock (_customTypes)
        {
            if (_customTypes.Count > 0)
            {
                var types = pb.GetTypes();
                foreach (var key in _customTypes)
                {
                    if (types.Contains(key))
                        return new ClipboardCustomData
                        {
                            Custom = ReadCustom(pb, key),
                            Type = ClipboardDataType.Custom,
                        };
                }
            }
        }

        var bmp = ReadBitmap(pb);
        if (bmp is not null)
            return new ClipboardBitmapData { Bitmap = bmp, Type = ClipboardDataType.Bitmap };

        return null;
    }

    /// <summary>
    /// 写自定义数据：内容字符串化后写第一个已注册 key 类型（Custom 数据不含 key，取登记序首个）。
    /// </summary>
    /// <param name="pb">剪贴板。</param>
    /// <param name="data">自定义数据。</param>
    private void SetCustom(NSPasteboard pb, ClipboardCustomData data)
    {
        lock (_customTypes)
        {
            if (_customTypes.Count == 0)
                return;
            var bytes = Encoding.UTF8.GetBytes(data.Custom?.ToString() ?? "");
            pb.SetDataForType(NSData.FromArray(bytes), _customTypes[0]);
        }
    }

    /// <summary>
    /// 读自定义数据（字符串）。
    /// </summary>
    /// <param name="pb">剪贴板。</param>
    /// <param name="key">pasteboard type。</param>
    /// <returns>字符串数据。</returns>
    private static string ReadCustom(NSPasteboard pb, string key)
    {
        var data = pb.DataForType(key);
        return data is null ? "" : Encoding.UTF8.GetString(data.ToArray());
    }

    /// <summary>
    /// 写位图数据（byte[] 按 PNG/JPEG/TIFF 魔数探测，兜底 TIFF）。
    /// </summary>
    /// <param name="pb">剪贴板。</param>
    /// <param name="bytes">位图字节。</param>
    private static void SetBitmap(NSPasteboard pb, byte[] bytes)
    {
        var type = DetectImageType(bytes);
        pb.SetDataForType(NSData.FromArray(bytes), type);
    }

    /// <summary>
    /// 读位图数据（PNG/JPEG/TIFF 任一可用），返回原始字节；不可用返回 null。
    /// </summary>
    /// <param name="pb">剪贴板。</param>
    /// <returns>位图字节；不可用为 null。</returns>
    private static byte[]? ReadBitmap(NSPasteboard pb)
    {
        foreach (var type in new[] { NSPasteboard.NSPasteboardTypePNG, NSPasteboard.NSPasteboardTypeJPEG, NSPasteboard.NSPasteboardTypeTIFF })
        {
            var data = pb.DataForType(type);
            if (data is not null && data.Length > 0)
                return data.ToArray();
        }
        return null;
    }

    /// <summary>
    /// 探测图像格式并映射到对应 UTI（PNG/JPEG 魔数，其余按 TIFF）。
    /// </summary>
    /// <param name="bytes">图像字节。</param>
    /// <returns>UTI 常量。</returns>
    private static NSString DetectImageType(byte[] bytes)
    {
        if (bytes.Length >= 8
            && bytes[0] == 0x89 && bytes[1] == (byte)'P' && bytes[2] == (byte)'N' && bytes[3] == (byte)'G')
            return NSPasteboard.NSPasteboardTypePNG;
        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
            return NSPasteboard.NSPasteboardTypeJPEG;
        return NSPasteboard.NSPasteboardTypeTIFF;
    }

    /// <summary>
    /// 读文件 URL 列表（public.file-url 单个 / NSUrl 对象多文件），非文件 URL 忽略。
    /// </summary>
    /// <param name="pb">剪贴板。</param>
    /// <returns>本地路径列表。</returns>
    private static List<string> ReadFileUrls(NSPasteboard pb)
    {
        var result = new List<string>();

        var single = pb.GetStringForType(NSPasteboard.NSPasteboardTypeFileURL);
        if (single is not null && Uri.TryCreate(single, UriKind.Absolute, out var u) && u.Scheme == "file")
            result.Add(u.LocalPath);

        if (result.Count > 0)
            return result;

        try
        {
            var objs = pb.ReadObjects();
            foreach (var o in objs)
            {
                if (o is NSUrl url && url.IsFileUrl && url.Path is { } path)
                    result.Add(path);
            }
        }
        catch
        {
            // ReadObjects 绑定差异时忽略，仅保留单 URL 结果
        }
        return result;
    }
}
