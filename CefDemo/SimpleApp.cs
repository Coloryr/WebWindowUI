using Xilium.CefGlue;

namespace CefDemo;

/// <summary>
/// simple_app.c 移植：CefApp，只提供浏览器进程处理器。
/// </summary>
internal sealed class SimpleApp : CefApp
{
    /// <summary>
    /// 浏览器进程处理器（browser_process_handler_create，app 持有单例）。
    /// </summary>
    private readonly SimpleBrowserProcessHandler _browserProcessHandler = new();

    /// <summary>
    /// 返回浏览器进程处理器（simple_app_get_browser_process_handler）。
    /// </summary>
    /// <returns>处理器实例。</returns>
    protected override CefBrowserProcessHandler GetBrowserProcessHandler() => _browserProcessHandler;

    /// <summary>
    /// 命令行处理：仅 GPU 子进程切换到原生 OpenGL 后端（对齐 C 实例 simple_app.c）。
    /// CEF 默认 ANGLE/D3D11 后端在多显卡/虚拟显示器 VM 上初始化共享上下文时
    /// IMMEDIATE_CRASH（0x80000003），--use-angle=gl 走原生 GL 驱动可规避。
    /// 注意：只注入 gpu-process 分支——若注入浏览器进程分支，开关会传播给所有
    /// 子进程（含渲染进程），渲染进程带 --use-angle=gl 会在 chrome://gpu 页面
    /// 路径触发 use-after-free（0xC0000409，寄存器 0xBEEDDEAD 毒化）。
    /// </summary>
    /// <param name="processType">进程类型，浏览器进程为空串。</param>
    /// <param name="commandLine">命令行对象。</param>
    protected override void OnBeforeCommandLineProcessing(string processType, CefCommandLine commandLine)
    {
        if (processType == "gpu-process")
        {
            if (!commandLine.HasSwitch("use-angle") && !commandLine.HasSwitch("use-gl"))
            {
                commandLine.AppendSwitch("use-angle", "gl");
            }
        }
    }
}
