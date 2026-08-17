using WebWindowUI.Core.Platform;
using WebWindowUI.Natives.Windows;
using Xunit;

namespace WebWindowUI.Tests.Windows;

/// <summary>
/// Win32 剪贴板往返测试：系统剪贴板是进程共享状态，Set 后立即 Get 读回自身数据。
/// 剪贴板可能被其它进程临时占用（OpenClipboard 失败），Set 带小重试。
/// </summary>
[Collection("clipboard")]
public class Win32ClipboardTests
{
    /// <summary>
    /// 剪贴板单例。
    /// </summary>
    private static Win32Clipboard Clip => Win32Clipboard.Instance;

    /// <summary>
    /// 写剪贴板（OpenClipboard 被占用时小重试，测试环境偶尔有拖拽/复制占用）。
    /// </summary>
    /// <param name="data">剪贴板数据。</param>
    private static void SetWithRetry(ClipboardData data)
    {
        for (int i = 0; i < 10; i++)
        {
            try
            {
                Clip.SetClipboardData(data);
                return;
            }
            catch (InvalidOperationException)
            {
                Thread.Sleep(50);
            }
        }
        throw new InvalidOperationException("多次重试仍无法打开剪贴板。");
    }

    /// <summary>
    /// 清空剪贴板（OpenClipboard + EmptyClipboard，尽力而为）。
    /// </summary>
    private static void EmptyClipboard()
    {
        try
        {
            if (Win32.OpenClipboard(IntPtr.Zero))
            {
                Win32.EmptyClipboard();
                Win32.CloseClipboard();
            }
        }
        catch
        {
            // 清空失败不影响后续断言
        }
    }

    /// <summary>
    /// 文本往返。
    /// </summary>
    [Fact]
    public void Text_RoundTrip()
    {
        SetWithRetry(new ClipboardTextData { Text = "hello 剪贴板" });
        try
        {
            var data = Clip.GetClipboardData();
            var text = Assert.IsType<ClipboardTextData>(data);
            Assert.Equal("hello 剪贴板", text.Text);
            Assert.Equal(ClipboardDataType.Text, text.Type);
        }
        finally
        {
            EmptyClipboard();
        }
    }

    /// <summary>
    /// URL 往返（写入 CF_UNICODETEXT，读回按 URL 识别）。
    /// </summary>
    [Fact]
    public void Url_RoundTrip()
    {
        SetWithRetry(new ClipboardUrlData { Url = new Uri("https://example.com/a?b=1") });
        try
        {
            var data = Clip.GetClipboardData();
            var url = Assert.IsType<ClipboardUrlData>(data);
            Assert.Equal("https://example.com/a?b=1", url.Url.ToString());
            Assert.Equal(ClipboardDataType.Url, url.Type);
        }
        finally
        {
            EmptyClipboard();
        }
    }

    /// <summary>
    /// HTML 往返（CF_HTML 标准头组装 + 剥离）。
    /// </summary>
    [Fact]
    public void Html_RoundTrip()
    {
        SetWithRetry(new ClipboardHtmlData { Html = "<b>bold</b> <i>italic</i>" });
        try
        {
            var data = Clip.GetClipboardData();
            var html = Assert.IsType<ClipboardHtmlData>(data);
            Assert.Contains("<b>bold</b> <i>italic</i>", html.Html);
            Assert.StartsWith("<html>", html.Html);
            Assert.Equal(ClipboardDataType.Html, html.Type);
        }
        finally
        {
            EmptyClipboard();
        }
    }

    /// <summary>
    /// 文件列表往返（CF_HDROP，UTF-16 双 NUL 路径表）。
    /// </summary>
    [Fact]
    public void Files_RoundTrip()
    {
        var paths = new List<string>
        {
            Path.Combine(Path.GetTempPath(), "webwindowui a.txt"),
            Path.Combine(Path.GetTempPath(), "webwindowui 目录 b.txt"),
        };
        SetWithRetry(new ClipboardFilesData { Files = paths });
        try
        {
            var data = Clip.GetClipboardData();
            var files = Assert.IsType<ClipboardFilesData>(data);
            Assert.Equal(paths, files.Files);
            Assert.Equal(ClipboardDataType.Files, files.Type);
        }
        finally
        {
            EmptyClipboard();
        }
    }

    /// <summary>
    /// 位图往返：BMP 文件字节剥 BITMAPFILEHEADER 进 CF_DIB，读回补头后 DIB 部分逐字节一致。
    /// </summary>
    [Fact]
    public void Bitmap_RoundTrip()
    {
        var bmp = MakeTestBmp(2, 2);
        SetWithRetry(new ClipboardBitmapData { Bitmap = bmp });
        try
        {
            var data = Clip.GetClipboardData();
            var bitmap = Assert.IsType<ClipboardBitmapData>(data);
            Assert.Equal(ClipboardDataType.Bitmap, bitmap.Type);
            Assert.Equal((byte)'B', bitmap.Bitmap[0]);
            Assert.Equal((byte)'M', bitmap.Bitmap[1]);
            // bfOffBits=54 的标准 BMP 剥 14 字节头进 CF_DIB，读回补头后应逐字节一致
            Assert.Equal(bmp, bitmap.Bitmap);
        }
        finally
        {
            EmptyClipboard();
        }
    }

    /// <summary>
    /// 自定义格式往返（RegisterClipboardFormatW 注册 + 字符串载荷）。
    /// </summary>
    [Fact]
    public void Custom_RoundTrip()
    {
        var key = "WWUI_Test_Custom_" + Guid.NewGuid().ToString("N");
        Clip.RegisterCustomData(key);
        SetWithRetry(new ClipboardCustomData { Custom = "custom-value" });
        try
        {
            var data = Clip.GetClipboardData();
            var custom = Assert.IsType<ClipboardCustomData>(data);
            Assert.Equal("custom-value", custom.Custom as string);
            Assert.Equal(ClipboardDataType.Custom, custom.Type);
        }
        finally
        {
            EmptyClipboard();
        }
    }

    /// <summary>
    /// 空剪贴板返回 null。
    /// </summary>
    [Fact]
    public void Empty_ReturnsNull()
    {
        EmptyClipboard();
        Assert.Null(Clip.GetClipboardData());
    }

    /// <summary>
    /// 构造 1 位/像素最小 BMP 文件字节（14 字节 BITMAPFILEHEADER + 40 字节 BITMAPINFOHEADER + 像素）。
    /// </summary>
    /// <param name="width">宽度。</param>
    /// <param name="height">高度。</param>
    /// <returns>BMP 文件字节。</returns>
    private static byte[] MakeTestBmp(int width, int height)
    {
        const int headerSize = 14;
        const int infoSize = 40;
        int rowBytes = ((width * 1 + 31) / 32) * 4; // 1 位/像素按行对齐到 4 字节
        int pixelBytes = rowBytes * Math.Abs(height);
        int fileSize = headerSize + infoSize + pixelBytes;

        var bmp = new byte[fileSize];
        bmp[0] = (byte)'B';
        bmp[1] = (byte)'M';
        bmp[2] = (byte)fileSize;
        bmp[3] = (byte)(fileSize >> 8);
        bmp[4] = (byte)(fileSize >> 16);
        bmp[5] = (byte)(fileSize >> 24);
        bmp[10] = headerSize + infoSize; // bfOffBits = 54
        bmp[14] = infoSize;              // biSize
        bmp[18] = (byte)width;           // biWidth
        bmp[22] = (byte)height;          // biHeight
        bmp[26] = 1;                     // biPlanes
        bmp[28] = 1;                     // biBitCount = 1
        return bmp;
    }
}
