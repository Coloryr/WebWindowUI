using Xilium.CefGlue;

namespace CefDemo;

/// <summary>
/// cefsimple_win.c 的 C# 移植：入口点 + 生命周期。
/// </summary>
internal static class Program
{
    /// <summary>
    /// 应用实例（CEF 持有原生引用，静态保留防 GC 提前回收）。
    /// </summary>
    private static SimpleApp _app;

    /// <summary>
    /// 入口：同 exe 子进程模型，先 ExecuteProcess 分发子进程。
    /// </summary>
    /// <param name="args">命令行参数。</param>
    /// <returns>退出码。</returns>
    [STAThread]
    private static int Main(string[] args)
    {
        var mainArgs = new CefMainArgs(args);

        // 创建应用实例（simple_app_create）。
        _app = new SimpleApp();

        // 子进程（render/GPU 等）共享同一 exe；浏览器进程返回 -1（cef_execute_process）。
        var exitCode = CefRuntime.ExecuteProcess(mainArgs, _app, IntPtr.Zero);
        if (exitCode >= 0)
            return exitCode;

        // 全局设置。sandbox_info 为 NULL → no_sandbox=1（同 C 示例 wWinMain）。
        var settings = new CefSettings
        {
            NoSandbox = true,
            RootCachePath = @"E:\code\WebWindowUI\CefDemo\cef-cache",
            LogFile = @"E:\code\WebWindowUI\CefDemo\cef-demo.log",
            LogSeverity = CefLogSeverity.Verbose,
        };

        // 初始化浏览器进程（cef_initialize）。
        CefRuntime.Initialize(mainArgs, settings, _app, IntPtr.Zero);

        // 运行 CEF 消息循环，阻塞至 QuitMessageLoop（cef_run_message_loop）。
        CefRuntime.RunMessageLoop();

        // 关闭 CEF（cef_shutdown）。
        CefRuntime.Shutdown();

        return 0;
    }
}
