namespace WebWindowUI.Sample;

/// <summary>
/// 示例「数据通道」资源提供者：通过 appbin:// 单独托管大块/二进制数据，
/// 与 UI 静态资源（app:// 下的 wwwroot）分开，演示专用通道的用途。
/// 前端用 fetch('appbin://localhost/bin/blob.bin') 即可取到字节流。
/// </summary>
public static class DataProvider
{
    /// <summary>模拟的"大块"数据：2 MB 确定性字节（用长度校验能否完整传输）。</summary>
    private const int BlobSize = 2 * 1024 * 1024;
    private static readonly byte[] Blob = BuildBlob();

    public static Stream? Resolve(string relativePath)
        => relativePath switch
        {
            "bin/blob.bin" => new MemoryStream(Blob),
            "bin/hello.txt" => new MemoryStream(System.Text.Encoding.UTF8.GetBytes(
                "Hello from appbin:// data channel.\n"
                + "这个 scheme 与 app:// 分开，专门承载大块/二进制数据。\n")),
            _ => null,
        };

    private static byte[] BuildBlob()
    {
        var buf = new byte[BlobSize];
        for (int i = 0; i < buf.Length; i++)
            buf[i] = (byte)(i % 251); // 确定性内容，前端可校验长度
        return buf;
    }
}
