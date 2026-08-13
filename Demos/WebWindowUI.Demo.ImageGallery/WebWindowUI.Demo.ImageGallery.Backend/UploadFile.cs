namespace WebWindowUI.Demo.ImageGallery;

/// <summary>
/// 上传载荷 DTO（字节上传专用）：非 WebWindowModel，不参与生成器扫描。命令参数从前端 object map 经桥
/// 转 ModelValue → TryFromModelValue 反射路径重建（参数化 ctor + 可写属性名与 camelCase 前端键忽略大小写匹配）。
/// 路径上传不走本 DTO——由后端 PickFile 命令弹系统原生对话框自读源文件。
/// </summary>
public sealed class UploadFile
{
    /// <summary>
    /// 原始文件名（含扩展名）。
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// 源文件地址（前端选中的本地文件完整路径；WebView2 经 File.path 提供，其余平台可能为空）。
    /// </summary>
    public string Path { get; set; } = "";

    /// <summary>
    /// 文件字节。
    /// </summary>
    public byte[]? Data { get; set; }
}
