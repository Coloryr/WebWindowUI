using System.Diagnostics;
using Xilium.CefGlue;
using Xilium.CefGlue.BrowserProcess;

namespace CefDemo;

/// <summary>
/// CefDemo 入口（同 exe 子进程模型）：CefSubProcess.Run 分发子进程 → CEF 初始化 → 消息循环。
/// 对齐 CefGlue.Avalonia demo（temp_code）与 C 实例的 chrome://gpu 验证场景。
/// </summary>
internal static class Program
{
    /// <summary>
    /// 入口：子进程（render/GPU/utility）经 CefSubProcess.Run 分发并退出；浏览器进程初始化 CEF。
    /// </summary>
    /// <param name="args">命令行参数。</param>
    /// <returns>退出码。</returns>
    private static int Main(string[] args)
    {
        // 子进程分发（RendererCefApp，不带 GetBrowserProcessHandler；同 Avalonia demo）。
        CefSubProcess.Run(args, true);

        // 全局设置最小化（同 C 实例零值 settings）：仅 no_sandbox。app.manifest 必须存在
        //（缺清单时 chrome://gpu 渲染进程确定性 0xC0000409 崩溃）。
        var settings = new CefSettings
        {
            NoSandbox = true,
        };

        var mainArgs = new CefMainArgs(args);

        var _app = new SimpleApp();
        CefRuntime.Initialize(mainArgs, settings, _app, IntPtr.Zero);
        CefRuntime.RunMessageLoop();
        CefRuntime.Shutdown();

        return 0;
    }
}
