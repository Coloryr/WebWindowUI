using System.Diagnostics;
using Xilium.CefGlue;
using Xilium.CefGlue.BrowserProcess;

namespace CefDemo;

internal static class Program
{
    private static int Main(string[] args)
    {
        CefSubProcess.Run(args, true);

        var settings = new CefSettings
        {
            NoSandbox = true,
        };

        var mainArgs = new CefMainArgs(args);

        var _app = new SimpleApp();
        CefRuntime.Initialize(mainArgs, settings, _app, IntPtr.Zero);
        CefRuntime.RunMessageLoop();
        CefRuntime.Shutdown();

        return 0;
    }
}
