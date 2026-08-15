using WebWindowUI.Core;
using WebWindowUI.Platforms.Cef;

namespace WebWindowUI.Sample;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        WebWindowUIPlatform.Init();

        WebWindowResource.RegisterCustomRoute("bin", new DataProvider());

        LauncherWindow launcher = new();
        launcher.Show();

        WebWindowUIPlatform.Run();
    }
}
