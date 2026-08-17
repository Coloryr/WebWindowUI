namespace WebWindowUI.Core;

/// <summary>
/// 程序日志
/// </summary>
public static class WebWindowLog
{
    /// <summary>
    /// 输出调试日志。
    /// </summary>
    /// <param name="message">日志内容。</param>
    public static void Debug(string message)
    {
        Console.WriteLine($"[WebWindowUI] {message}");
    }

    /// <summary>
    /// 输出错误日志并弹系统消息框。
    /// </summary>
    /// <param name="message">错误内容。</param>
    public static void Error(string message)
    {
        Console.WriteLine($"[WebWindowUI] {message}");

        WebWindowPlatform.Current.Dialog.ShowMessageBox("", message, true);
    }
}
