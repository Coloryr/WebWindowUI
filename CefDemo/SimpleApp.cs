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
}
