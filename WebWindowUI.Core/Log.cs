using System.Diagnostics;
#if WINDOWS
using System.Runtime.InteropServices;
#endif

namespace WebWindowUI;

/// <summary>
/// 框架内部诊断日志，统一输出到 stderr 控制台。仅 DEBUG 构建生效：
/// 方法带 [Conditional("DEBUG")]，Release 构建下调用点连同字符串插值整体被编译器剔除，
/// 不产生任何运行时开销。Windows/Linux/macOS 平台共用（各平台子命名空间经外层命名空间直接可见）。
/// 调用约定：传插值字符串（如 <c>Log.Debug($"register = {ok}")</c>），Release 下连插值都不会发生。
/// </summary>
internal static class Log
{
    [Conditional("DEBUG")]
    public static void Debug(string message) => Console.Error.WriteLine($"[WebWindowUI] {message}");

#if DEBUG && WINDOWS
    // WinExe（GUI 子系统）没有控制台，Console.Error 默认写不进任何地方 → Debug 日志不可见。
    // 首次使用日志时先附加父进程控制台（从终端 dotnet run 时日志回到终端，不弹新窗口），
    // 失败则新建一个控制台窗口（双击启动、无父控制台场景）。Debug 构建才编译本块；
    // Release 下 Log.Debug 调用点已被剔除，本块随同消失，不产生任何控制台副作用。
    static Log()
    {
        if (!AttachConsole(AttachParentProcess))
            AllocConsole();
        // Attach/AllocConsole 会重定向标准句柄；重新绑定 Console.Out/Error，否则
        // .NET 启动时缓存的是无控制台状态下的无效句柄，WriteLine 依然无输出。
        Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
        Console.SetError(new StreamWriter(Console.OpenStandardError()) { AutoFlush = true });
    }

    private const uint AttachParentProcess = 0xFFFFFFFFu; // ATTACH_PARENT_PROCESS

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachConsole(uint dwProcessId);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllocConsole();
#endif
}
