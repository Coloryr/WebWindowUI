using System.Runtime.CompilerServices;
using WebWindowUI.Core;

namespace WebWindowUI.Platforms.Cef;

internal static class PlatformRegistration
{
    [ModuleInitializer]
    internal static void Register()
    {
        WebWindowPlatform.Register(new CefPlatform());
    }
}
