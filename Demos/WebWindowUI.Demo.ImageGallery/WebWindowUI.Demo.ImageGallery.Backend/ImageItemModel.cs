using CommunityToolkit.Mvvm.ComponentModel;

namespace WebWindowUI.Demo.ImageGallery;

/// <summary>
/// 图库条目：图片元数据 + 完整字节。作为 <see cref="ImageGalleryModel.Items"/> 的元素
/// （typed repeated List&lt;ImageItemModel&gt;）。Data 承载图片字节，由 .NET 侧从磁盘读出
/// 推给前端——「后端发送图片」的载体，前端把它转成 blob URL 渲染缩略图/大图。
/// </summary>
public partial class ImageItemModel : WebWindowModel
{
    /// <summary>磁盘文件名（含扩展名）。</summary>
    [ObservableProperty]
    public partial string Name { get; set; } = "";

    /// <summary>文件大小（字节）。</summary>
    [ObservableProperty]
    public partial long Size { get; set; }

    /// <summary>修改时间（格式化字符串，与 .NET 时间戳对应）。</summary>
    [ObservableProperty]
    public partial string Modified { get; set; } = "";

    /// <summary>文件地址（磁盘完整路径，如 C:\Users\…\images\sunset.png）。</summary>
    [ObservableProperty]
    public partial string Path { get; set; } = "";

    /// <summary>图片字节（png/jpg/gif/webp/…），前端按扩展名推断 MIME 后转 blob URL 渲染。</summary>
    [ObservableProperty]
    public partial byte[]? Data { get; set; }
}
