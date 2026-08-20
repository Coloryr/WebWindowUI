using WebWindowUI.Core;

namespace WebWindowUI.Sample;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // 平台初始化：CefSubProcess.Run 子进程分发（CEF 平台在 Init 内部处理）→ CEF 初始化。
        WebWindowUIPlatform.Init(args);

        WebWindowResource.RegisterCustomRoute("bin", new DataProvider());

        // 主窗口 = 综合演示窗口（无独立入口，应用启动直接进 demo）
        DemoWindow demo = new();
        demo.Window.Show();

        WebWindowUIPlatform.Run();
    }
}
