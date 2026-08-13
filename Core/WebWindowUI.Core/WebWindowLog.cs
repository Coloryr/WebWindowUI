namespace WebWindowUI.Core;

/// <summary>
/// 程序日志
/// </summary>
internal static class WebWindowLog
{
    public static void Debug(string message)
    {
        Console.WriteLine($"[WebWindowUI] {message}");
    }

    public static void Error(string message)
    {
        Console.WriteLine($"[WebWindowUI] {message}");

        WebWindowPlatform.Current.ShowMessageBox("", message, true);
    }
}
