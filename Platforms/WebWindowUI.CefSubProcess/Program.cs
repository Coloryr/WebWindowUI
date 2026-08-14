using System.Diagnostics;
using System.Runtime.InteropServices;
using Xilium.CefGlue.BrowserProcess;
using Xilium.CefGlue.Common.Shared.Helpers;

namespace WebWindowUI.CefSubProcessClient;

/// <summary>
/// 简易子进程 client：只跑 CEF 子进程逻辑（ExecuteProcess + --custom-scheme 注册 + 父进程监控），
/// 不加载应用/桥逻辑。浏览器进程由应用主进程承担，本 client 仅作 renderer/gpu 等子进程宿主。
/// </summary>
internal static class Program
{
    internal static class StackDebug
    {
        [Conditional("DEBUG")]
        internal static void Log(string[] args, string prefix)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;

            Directory.CreateDirectory(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "logs"));

            WriteAllocatedStackSize($"{prefix} Stack [{string.Join(",", args).Replace("--type=", "")}]");
        }

        private static void WriteAllocatedStackSize(string header)
        {

            // Log to file so renderer subprocess output is also visible
            var msg = $"{header,-25}: {ThreadStack.GetSize(),6} KB  [pid={Environment.ProcessId}]";
            Debug.WriteLine(msg);
            File.AppendAllText(
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "logs", "stack.log"),
                msg + Environment.NewLine);
        }
    }

    /// <summary>
    /// 入口：透传给 CefSubProcess.Run（非 --type= 子进程时直接返回）。
    /// </summary>
    /// <param name="args">命令行参数。</param>
    private static void Main(string[] args)
    {
        StackDebug.Log(args, "WebWindowUI.CefSubProcess");

        CefSubProcess.Run(args, true);
    }
}
