using System.Diagnostics;

namespace WebWindowUI.Core;

/// <summary>
/// 框架内部诊断日志，统一输出到 stderr 控制台。仅 DEBUG 构建生效：
/// 方法带 [Conditional("DEBUG")]，Release 构建下调用点连同字符串插值整体被编译器剔除，
/// 不产生任何运行时开销。Windows/Linux/macOS 平台共用（各平台子命名空间经外层命名空间直接可见）。
/// 调用约定：传插值字符串（如 <c>Log.Debug($"register = {ok}")</c>），Release 下连插值都不会发生。
/// </summary>
internal static class Log
{
    [Conditional("DEBUG")]
    public static void Debug(string message) => Console.WriteLine($"[WebWindowUI] {message}");
}
