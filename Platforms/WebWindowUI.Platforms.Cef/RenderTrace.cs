using System.Diagnostics;
using System.Runtime.InteropServices;

namespace WebWindowUI.Platforms.Cef;

/// <summary>
/// 临时调试：记录嵌入流程关键点（窗口句柄/重挂载结果）到 Desktop\logs\render.log。
/// 调试完删除。
/// </summary>
internal static class RenderTrace
{
    [Conditional("DEBUG")]
    internal static void Log(string msg)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;
        Directory.CreateDirectory(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "logs"));
        var line = $"[{DateTime.Now:HH:mm:ss.fff}] tid={Environment.CurrentManagedThreadId} pid={Environment.ProcessId} {msg}";
        File.AppendAllText(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "logs", "render.log"),
            line + Environment.NewLine);
    }
}
