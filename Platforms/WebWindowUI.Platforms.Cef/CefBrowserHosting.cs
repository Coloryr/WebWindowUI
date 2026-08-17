using WebWindowUI.Natives.Windows;
using Xilium.CefGlue;
using Xilium.CefGlue.Common.Events;

namespace WebWindowUI.Platforms.Cef;

/// <summary>
/// CEF 浏览器托管（纯公共 API 自建，替代 CefGlue.Common 内部 CommonBrowserAdapter/IControl）：
/// 浏览器先作为隐藏宿主窗口子窗口创建（SetAsChild），初始化完成后把浏览器 HWND 重挂载进
/// 目标可见窗口并铺满（复用 Natives.Windows.Win32BrowserHost，对齐 CefGlue.Avalonia）。
/// 浏览器创建/生命周期回调都在 CEF UI 线程。
/// </summary>
internal sealed class CefBrowserHosting
{
    /// <summary>
    /// 浏览器实例（OnAfterCreated 后有效）。
    /// </summary>
    private CefBrowser? _browser;

    /// <summary>
    /// 浏览器宿主（OnAfterCreated 后有效）。
    /// </summary>
    private CefBrowserHost? _browserHost;

    /// <summary>
    /// CEF 客户端（含生命周期/加载处理器）。
    /// </summary>
    private HostingClient? _client;

    /// <summary>
    /// 隐藏宿主窗口（浏览器初始父窗口）。
    /// </summary>
    private IntPtr? _hiddenHost;

    /// <summary>
    /// 目标可见窗口（重挂载目标）。
    /// </summary>
    private IntPtr _targetWindow;

    /// <summary>
    /// 当前宽度。
    /// </summary>
    private int _width;

    /// <summary>
    /// 当前高度。
    /// </summary>
    private int _height;

    /// <summary>
    /// 初始 URL（OnAfterCreated 后导航）。
    /// </summary>
    private readonly string _initialUrl;

    /// <summary>
    /// 浏览器初始化完成（CEF UI 线程回调）。
    /// </summary>
    public event Action? Initialized;

    /// <summary>
    /// 浏览器销毁（CEF UI 线程回调；DevTools 等弹窗也触发，调用方按主浏览器引用过滤）。
    /// </summary>
    public event Action<CefBrowser>? BrowserClosed;

    /// <summary>
    /// 加载结束（CEF UI 线程回调）。
    /// </summary>
    public event EventHandler<LoadEndEventArgs>? LoadEnd;

    /// <summary>
    /// 浏览器是否已初始化。
    /// </summary>
    public bool IsInitialized => _browser is not null;

    /// <summary>
    /// 浏览器实例（OnAfterCreated 后有效，否则 null）。
    /// </summary>
    public CefBrowser? Browser => _browser;

    /// <summary>
    /// 构造托管：记录初始 URL，浏览器创建走 Create。
    /// </summary>
    /// <param name="initialUrl">初始导航 URL。</param>
    public CefBrowserHosting(string initialUrl) => _initialUrl = initialUrl;

    /// <summary>
    /// 创建浏览器：建隐藏宿主窗口，SetAsChild 到其上，异步创建（OnAfterCreated 完成重挂载与导航）。
    /// </summary>
    /// <param name="targetWindow">目标可见窗口句柄。</param>
    /// <param name="width">初始宽度。</param>
    /// <param name="height">初始高度。</param>
    public void Create(IntPtr targetWindow, int width, int height)
    {
        if (_client is not null)
            return;
        _targetWindow = targetWindow;
        _width = width;
        _height = height;

        _hiddenHost = Win32BrowserHost.CreateHiddenHost();
        var windowInfo = CefWindowInfo.Create();
        windowInfo.SetAsChild(_hiddenHost.Value, new CefRectangle(0, 0, width, height));
        _client = new HostingClient(this);
        // extraInfo/requestContext 传空，对齐上游 CommonBrowserAdapter.CreateBrowser。
        CefBrowserHost.CreateBrowser(windowInfo, _client, new CefBrowserSettings(), "", null, null);
    }

    /// <summary>
    /// 重挂载浏览器进目标窗口并铺满（窗口显示/恢复时调用）。
    /// </summary>
    public void Reapply()
    {
        if (_browserHost is null || _targetWindow == IntPtr.Zero)
            return;
        Win32BrowserHost.Reparent(_browserHost.GetWindowHandle(), _targetWindow, _width, _height);
    }

    /// <summary>
    /// 更新尺寸：已初始化则铺满浏览器，未初始化只记录尺寸。
    /// </summary>
    /// <param name="width">宽度。</param>
    /// <param name="height">高度。</param>
    public void SetSize(int width, int height)
    {
        _width = width;
        _height = height;
        if (_browserHost is not null)
        {
            Win32BrowserHost.Resize(_browserHost.GetWindowHandle(), width, height);
        }
    }

    /// <summary>
    /// 导航主帧到指定 URL。
    /// </summary>
    /// <param name="url">目标 URL。</param>
    public void Navigate(string url)
    {
        _browser?.GetMainFrame().LoadUrl(url);
    }

    /// <summary>
    /// 在主帧执行一段 JS（无返回值）。
    /// </summary>
    /// <param name="code">JS 代码。</param>
    /// <param name="url">脚本 URL。</param>
    /// <param name="line">起始行号。</param>
    public void ExecuteJavaScript(string code, string url, int line)
        => _browser?.GetMainFrame().ExecuteJavaScript(code, url, line);

    /// <summary>
    /// 在主帧执行 JS 并返回结果字符串（best-effort；失败回退空串）。
    /// </summary>
    /// <param name="code">JS 代码。</param>
    /// <param name="url">脚本 URL。</param>
    /// <param name="line">起始行号。</param>
    /// <returns>执行结果字符串。</returns>
    public Task<string> EvaluateJavaScript(string code, string url, int line)
    {
        if (_browser is null)
            return Task.FromResult(string.Empty);
        string result = string.Empty;
        try
        {
            CefPlatform.RunOnCefUiThread(() =>
            {
                var context = _browser!.GetMainFrame().V8Context;
                if (context is null || !context.IsValid || !context.Enter())
                    return;
                try
                {
                    if (context.TryEval(code, url, line, out var value, out _))
                        result = value?.GetStringValue() ?? string.Empty;
                }
                finally
                {
                    context.Exit();
                }
            });
        }
        catch
        {
            // 窗口关闭后浏览器已销毁，忽略
        }
        return Task.FromResult(result);
    }

    /// <summary>
    /// 关闭浏览器（触发 OnBeforeClose → BrowserClosed；仅主浏览器）。
    /// </summary>
    /// <param name="forceClose">是否强制关闭。</param>
    public void Close(bool forceClose) => _browserHost?.CloseBrowser(forceClose);

    /// <summary>
    /// 浏览器初始化完成（CEF UI 线程回调）：记录浏览器、重挂载进目标窗口、导航初始 URL。
    /// </summary>
    /// <param name="browser">已创建的浏览器。</param>
    private void OnAfterCreated(CefBrowser browser)
    {
        if (_browser is not null)
            return;
        _browser = browser;
        _browserHost = browser.GetHost();
        Reapply();
        if (!string.IsNullOrEmpty(_initialUrl))
        {
            browser.GetMainFrame().LoadUrl(_initialUrl);
        }
        Initialized?.Invoke();
    }

    /// <summary>
    /// 浏览器销毁前（CEF UI 线程回调）：销毁隐藏宿主、触发 BrowserClosed。
    /// </summary>
    /// <param name="browser">即将销毁的浏览器。</param>
    private void OnBeforeClose(CefBrowser browser)
    {
        if (_hiddenHost is { } host)
        {
            Win32BrowserHost.Destroy(host);
            _hiddenHost = null;
        }
        BrowserClosed?.Invoke(browser);
    }

    /// <summary>
    /// 加载结束（CEF UI 线程回调）：转发 LoadEnd 事件。
    /// </summary>
    /// <param name="browser">浏览器。</param>
    /// <param name="frame">帧。</param>
    /// <param name="httpStatusCode">HTTP 状态码。</param>
    private void OnLoadEnd(CefBrowser browser, CefFrame frame, int httpStatusCode)
        => LoadEnd?.Invoke(this, new LoadEndEventArgs(frame, httpStatusCode));

    /// <summary>
    /// CEF 客户端：装配生命周期与加载处理器。
    /// </summary>
    private sealed class HostingClient : CefClient
    {
        /// <summary>
        /// 生命周期处理器。
        /// </summary>
        private readonly LifeSpanHandler _lifeSpanHandler;

        /// <summary>
        /// 加载处理器。
        /// </summary>
        private readonly LoadHandler _loadHandler;

        /// <summary>
        /// 构造客户端。
        /// </summary>
        /// <param name="owner">托管宿主。</param>
        public HostingClient(CefBrowserHosting owner)
        {
            _lifeSpanHandler = new LifeSpanHandler(owner);
            _loadHandler = new LoadHandler(owner);
        }

        /// <summary>
        /// 返回生命周期处理器。
        /// </summary>
        /// <returns>处理器。</returns>
        protected override CefLifeSpanHandler GetLifeSpanHandler() => _lifeSpanHandler;

        /// <summary>
        /// 返回加载处理器。
        /// </summary>
        /// <returns>处理器。</returns>
        protected override CefLoadHandler GetLoadHandler() => _loadHandler;
    }

    /// <summary>
    /// 生命周期处理器：OnAfterCreated 初始化、DoClose 允许关闭、OnBeforeClose 通知。
    /// </summary>
    private sealed class LifeSpanHandler : CefLifeSpanHandler
    {
        /// <summary>
        /// 托管宿主。
        /// </summary>
        private readonly CefBrowserHosting _owner;

        /// <summary>
        /// 构造处理器。
        /// </summary>
        /// <param name="owner">托管宿主。</param>
        public LifeSpanHandler(CefBrowserHosting owner) => _owner = owner;

        /// <summary>
        /// 浏览器创建完成。
        /// </summary>
        /// <param name="browser">浏览器。</param>
        protected override void OnAfterCreated(CefBrowser browser) => _owner.OnAfterCreated(browser);

        /// <summary>
        /// 关闭请求：返回 false 允许关闭（由 OnBeforeClose 完成收尾）。
        /// </summary>
        /// <param name="browser">浏览器。</param>
        /// <returns>是否取消关闭。</returns>
        protected override bool DoClose(CefBrowser browser) => false;

        /// <summary>
        /// 浏览器销毁前。
        /// </summary>
        /// <param name="browser">浏览器。</param>
        protected override void OnBeforeClose(CefBrowser browser) => _owner.OnBeforeClose(browser);
    }

    /// <summary>
    /// 加载处理器：主帧加载结束转发事件。
    /// </summary>
    private sealed class LoadHandler : CefLoadHandler
    {
        /// <summary>
        /// 托管宿主。
        /// </summary>
        private readonly CefBrowserHosting _owner;

        /// <summary>
        /// 构造处理器。
        /// </summary>
        /// <param name="owner">托管宿主。</param>
        public LoadHandler(CefBrowserHosting owner) => _owner = owner;

        /// <summary>
        /// 加载结束。
        /// </summary>
        /// <param name="browser">浏览器。</param>
        /// <param name="frame">帧。</param>
        /// <param name="httpStatusCode">HTTP 状态码。</param>
        protected override void OnLoadEnd(CefBrowser browser, CefFrame frame, int httpStatusCode)
            => _owner.OnLoadEnd(browser, frame, httpStatusCode);
    }
}
