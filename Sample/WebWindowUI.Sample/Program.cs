using WebWindowUI.Core;
using WebWindowUI.Platforms.Cef;
using Xilium.CefGlue.BrowserProcess;

namespace WebWindowUI.Sample;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        StackDebug.Log(args, "WebWindowUI");
        CefSubProcess.Run(args, true);
        StackDebug.Log(["UI Run"], "WebWindowUI");
        WebWindowUIPlatform.Init();

        WebWindowResource.RegisterCustomRoute("bin", new DataProvider());

        LauncherWindow launcher = new();
        launcher.Show();

        WebWindowUIPlatform.Run();
    }
}
