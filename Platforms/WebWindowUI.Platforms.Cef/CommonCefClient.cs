using Xilium.CefGlue;

namespace WebWindowUI.Platforms.Cef;

/// <summary>
/// CEF 客户端（镜像上游 Xilium.CefGlue.Common.CommonCefClient）：安装生命期/加载处理器，CEF 回调路由回宿主。
/// </summary>
internal sealed class CommonCefClient(ICefBrowserHost owner) : CefClient
{
    /// <summary>
    /// 生命期处理器。
    /// </summary>
    private readonly CefLifeSpanHandler _lifeSpanHandler = new CommonCefLifeSpanHandler(owner);
    /// <summary>
    /// 加载处理器。
    /// </summary>
    private readonly CefLoadHandler _loadHandler = new CommonCefLoadHandler(owner);

    protected override CefLifeSpanHandler? GetLifeSpanHandler() => _lifeSpanHandler;
    protected override CefLoadHandler? GetLoadHandler() => _loadHandler;
}

/// <summary>
/// 生命期处理器：创建/关闭浏览器回调路由回宿主（镜像上游 CommonCefLifeSpanHandler）。
/// </summary>
internal sealed class CommonCefLifeSpanHandler(ICefBrowserHost owner) : CefLifeSpanHandler
{
    protected override void OnAfterCreated(CefBrowser browser) => owner.HandleBrowserCreated(browser);

    protected override bool DoClose(CefBrowser browser) => owner.HandleBrowserClose(browser);

    protected override void OnBeforeClose(CefBrowser browser) => owner.HandleBrowserDestroyed(browser);
}

/// <summary>
/// 加载处理器：主 frame 加载完成路由回宿主（镜像上游 CommonCefLoadHandler）。
/// </summary>
internal sealed class CommonCefLoadHandler(ICefBrowserHost owner) : CefLoadHandler
{
    protected override void OnLoadEnd(CefBrowser browser, CefFrame frame, int httpStatusCode)
        => owner.HandleLoadEnd(browser, frame, httpStatusCode);
}
