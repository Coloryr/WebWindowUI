using System;
using System.Collections.Generic;
using System.Text;
using WebWindowUI.Core;
using WebWindowUI.Natives.Windows;

namespace WebWindowUI.Platforms.Cef;

public static class NativePlatform
{
    public static IPlatformDialog Dialog = new Win32Dialog();
}
