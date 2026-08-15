using Xilium.CefGlue;
using Xilium.CefGlue.Platform.Windows;

namespace CefDemo;

/// <summary>
/// simple_app.c 的 browser_process_handler 移植：OnContextInitialized 创建浏览器。
/// </summary>
internal sealed class SimpleBrowserProcessHandler : CefBrowserProcessHandler
{
    /// <summary>
    /// 客户端单例（simple_handler_create 后经 get_instance 取回）。
    /// </summary>
    private SimpleClient? _client;

    /// <summary>
    /// CEF 上下文初始化后创建浏览器（browser_process_handler_on_context_initialized）。
    /// </summary>
    protected override void OnContextInitialized()
    {
        var commandLine = CefCommandLine.Global;

        // --use-alloy-style 切换 Alloy 风格。
        var useAlloyStyle = commandLine.HasSwitch("use-alloy-style");

        // 创建客户端处理器（设全局单例，GetDefaultClient 取回）。
        _client = SimpleClient.Create(useAlloyStyle);

        // 取 URL（--url）或默认 chrome://gpu（GPU 诊断页，验证图形加速状态）。
        var url = commandLine.GetSwitchValue("url");
        if (string.IsNullOrEmpty(url))
            url = "chrome://gpu";

        // 运行时风格：默认 Default；--use-alloy-style 为 Alloy。
        var runtimeStyle = useAlloyStyle ? CefRuntimeStyle.Alloy : CefRuntimeStyle.Default;

        // 原生窗口路径：CEF 用 window_info 自建顶层窗口。raw CefGlue 无 Views API，恒走此路径。
        var windowInfo = CefWindowInfo.Create();
        windowInfo.Name = "cefsimple_capi";
        windowInfo.Style = WindowStyle.WS_OVERLAPPEDWINDOW
                         | WindowStyle.WS_CLIPCHILDREN
                         | WindowStyle.WS_CLIPSIBLINGS
                         | WindowStyle.WS_VISIBLE;
        windowInfo.Bounds = new CefRectangle(int.MinValue, int.MinValue, int.MinValue, int.MinValue); // CW_USEDEFAULT
        windowInfo.RuntimeStyle = runtimeStyle;

        // 创建浏览器窗口（CEF 持有客户端引用，浏览器关闭时释放）。
        CefBrowserHost.CreateBrowser(windowInfo, _client, new CefBrowserSettings(), url);
    }

    /// <summary>
    /// 返回默认客户端（browser_process_handler_get_default_client，Chrome 风格 UI 用）。
    /// </summary>
    /// <returns>全局客户端实例。</returns>
    protected override CefClient GetDefaultClient() => _client!;
}
