#if WINDOWS
using System;
using System.IO;

namespace WebWindowUI.Tests.Platform.Support;

/// <summary>
/// 文件轨迹（诊断平台注册/泵初始化的挂起点）。写到 %TEMP%\wwui_trace.txt，测试宿主无控制台
/// 时也能落地。
/// </summary>
internal static class Trace
{
    private static readonly string TraceFile =
        System.IO.Path.Combine(System.IO.Path.GetTempPath(), "wwui_trace.txt");

    public static void Log(string message)
    {
        try
        {
            File.AppendAllText(TraceFile, $"{DateTime.Now:HH:mm:ss.fff} T{Environment.CurrentManagedThreadId} {message}\r\n");
        }
        catch
        {
            // 轨迹失败不影响测试
        }
    }
}
#endif
