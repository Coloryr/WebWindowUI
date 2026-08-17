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

        LauncherWindow launcher = new();
        launcher.Window.Show();

        WebWindowUIPlatform.Run();
    }
}
