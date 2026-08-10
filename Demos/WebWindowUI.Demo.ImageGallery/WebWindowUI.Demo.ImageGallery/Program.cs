namespace WebWindowUI.Demo.ImageGallery;

/// <summary>
/// 图片画廊主窗口：绑定 ImageGalleryModel（typed repeated List&lt;ImageItemModel&gt; 元素携带
/// byte[] 图片字节 + 上传/删除/刷新命令 + 磁盘存储）。对应前端 src/window/main/。
/// </summary>
internal sealed class MainWindow : WebWindow
{
    public MainWindow(ImageGalleryModel model)
        : base("main", "图片画廊", width: 920, height: 700)
    {
        Model = model;
    }
}

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        WebWindowUI.Platform.EnsureRegistered();

        // 启动即扫描 %LocalAppData%\WebWindowUI.Demo.ImageGallery\images，把每张图片字节发给前端。
        ImageGalleryModel model = new();
        MainWindow window = new(model);
        window.Show();

        WebWindow.RunMessageLoop();
    }
}
