using WebWindowUI.Core;

namespace WebWindowUI.Demo.ImageGallery;

/// <summary>
/// 图片画廊主窗口控制器：经 CreateWindow 建窗，绑定 ImageGalleryModel
/// （typed repeated List&lt;ImageItemModel&gt; 元素携带 byte[] 图片字节 + 上传/删除/刷新命令 + 磁盘存储）。
/// 对应前端 src/window/main/。
/// </summary>
internal sealed class MainWindow
{
    /// <summary>
    /// 框架窗口（构造即创建，Show 由宿主负责）。
    /// </summary>
    public WebWindow Window { get; }

    public MainWindow(ImageGalleryModel model)
    {
        Window = WebWindowPlatform.Current.CreateWindow(new WebWindowOptions("main")
        {
            Title = "图片画廊",
            Width = 920,
            Height = 700
        });
        Window.Model = model;
    }
}

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // 启动即扫描 %LocalAppData%\WebWindowUI.Demo.ImageGallery\images，把每张图片字节发给前端。
        ImageGalleryModel model = new();
        new MainWindow(model).Window.Show();

        WebWindowUIPlatform.Run();
    }
}
