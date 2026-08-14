using Xilium.CefGlue;
using Xilium.CefGlue.Platform.Windows;

namespace WebWindowUI.Platforms.Cef;

/// <summary>
/// CEF 处理器回调统一路由口（镜像上游 Xilium.CefGlue.Common.ICefBrowserHost；按本平台需要裁剪）。
/// </summary>
internal interface ICefBrowserHost
{
    /// <summary>
    /// 主浏览器创建（on_after_created）。
    /// </summary>
    /// <param name="browser">已创建的浏览器。</param>
    void HandleBrowserCreated(CefBrowser browser);

    /// <summary>
    /// 浏览器销毁（on_before_close）。
    /// </summary>
    /// <param name="browser">正在销毁的浏览器。</param>
    void HandleBrowserDestroyed(CefBrowser browser);

    /// <summary>
    /// DoClose 回调：返回 false 走 CEF 完整关闭（→ on_before_close）。
    /// </summary>
    /// <param name="browser">正在关闭的浏览器。</param>
    /// <returns>是否接管关闭（本平台恒 false）。</returns>
    bool HandleBrowserClose(CefBrowser browser);

    /// <summary>
    /// 主 frame 加载完成（on_load_end）。
    /// </summary>
    /// <param name="browser">浏览器。</param>
    /// <param name="frame">加载完成的 frame。</param>
    /// <param name="httpStatusCode">HTTP 状态码。</param>
    void HandleLoadEnd(CefBrowser browser, CefFrame frame, int httpStatusCode);
}

/// <summary>
/// 宿主控件抽象（镜像上游 Xilium.CefGlue.Common.Platform.IControl）：浏览器子窗口挂载点 + 尺寸通知。
/// </summary>
internal interface IControl
{
    /// <summary>
    /// 浏览器子窗口的宿主视图句柄；返回 null 则浏览器创建中止。
    /// </summary>
    /// <param name="initialWidth">初始宽度。</param>
    /// <param name="initialHeight">初始高度。</param>
    /// <returns>宿主视图句柄。</returns>
    IntPtr? GetHostViewHandle(int initialWidth, int initialHeight);

    /// <summary>
    /// 控件尺寸变化（首个尺寸触发浏览器创建）。
    /// </summary>
    event Action<CefSize>? SizeChanged;

    /// <summary>
    /// 渲染挂载通知（窗口模式空实现，供无头渲染扩展）。
    /// </summary>
    /// <param name="browserHandle">浏览器窗口句柄。</param>
    void InitializeRender(IntPtr browserHandle);

    /// <summary>
    /// 渲染销毁通知（窗口模式空实现）。
    /// </summary>
    void DestroyRender();
}

/// <summary>
/// load_end 事件参数。
/// </summary>
internal sealed class LoadEndEventArgs(CefFrame frame, int httpStatusCode)
{
    /// <summary>
    /// 加载完成的 frame。
    /// </summary>
    public CefFrame Frame { get; } = frame;

    /// <summary>
    /// HTTP 状态码。
    /// </summary>
    public int HttpStatusCode { get; } = httpStatusCode;
}

/// <summary>
/// load_end 事件委托。
/// </summary>
internal delegate void LoadEndEventHandler(object sender, LoadEndEventArgs args);

/// <summary>
/// 浏览器生命周期与事件引擎（镜像上游 Xilium.CefGlue.Common.CommonBrowserAdapter）：
/// 持有 <see cref="CommonCefClient"/> 与主浏览器，建浏览器、路由 CEF 回调、执行 JS、DevTools、关闭。
/// 裁剪了上游的渲染进程 IPC/对象绑定/崩溃管道子系统（本平台经 scheme 通道走 protobuf，不需要）。
/// </summary>
internal sealed class CommonBrowserAdapter : ICefBrowserHost
{
    /// <summary>
    /// 事件源（上游语义：事件首参即发起对象，传父级 BaseCefBrowser）。
    /// </summary>
    private readonly object _eventsEmitter;
    /// <summary>
    /// 宿主控件（浏览器子窗口挂载点）。
    /// </summary>
    private readonly IControl _control;
    /// <summary>
    /// 起始 URL（浏览器创建前经 Address 设置时暂存，on_after_created 加载）。
    /// </summary>
    private string _initialUrl = "";
    /// <summary>
    /// 主浏览器；on_after_created 记录，on_before_close 置空。
    /// </summary>
    private CefBrowser? _browser;
    /// <summary>
    /// CefClient 及处理器。
    /// </summary>
    private CommonCefClient? _cefClient;
    /// <summary>
    /// 是否已发起创建（防重复创建）。
    /// </summary>
    private bool _isBrowserCreated;

    /// <summary>
    /// 构造适配器：绑定宿主控件，首个尺寸变化触发浏览器创建。
    /// </summary>
    /// <param name="eventsEmitter">事件源对象。</param>
    /// <param name="control">宿主控件。</param>
    public CommonBrowserAdapter(object eventsEmitter, IControl control)
    {
        _eventsEmitter = eventsEmitter;
        _control = control;
        control.SizeChanged += HandleControlSizeChanged;
    }

    /// <summary>
    /// 浏览器初始化完成（on_after_created）。
    /// </summary>
    public event Action? Initialized;

    /// <summary>
    /// 主 frame 加载完成。
    /// </summary>
    public event LoadEndEventHandler? LoadEnd;

    /// <summary>
    /// 主浏览器关闭（on_before_close）。
    /// </summary>
    public event Action<CefBrowser>? BrowserClosed;

    /// <summary>
    /// 底层浏览器是否已初始化。
    /// </summary>
    public bool IsInitialized => _browser is not null;

    /// <summary>
    /// 底层主浏览器。
    /// </summary>
    public CefBrowser? Browser => _browser;

    /// <summary>
    /// 起始/当前 URL。浏览器已建后设置须在 UI 线程（LoadUrl 要求）。
    /// </summary>
    public string Address
    {
        get => _browser?.GetMainFrame().Url ?? _initialUrl;
        set => NavigateTo(value);
    }

    /// <summary>
    /// 创建浏览器（CEF 子窗口铺满宿主客户区）。已创建或尺寸非法则忽略。
    /// 内部 marshal 到 CEF UI 线程执行（MTML=true 下浏览器创建必须在其上）。
    /// </summary>
    /// <param name="width">客户区宽度。</param>
    /// <param name="height">客户区高度。</param>
    /// <returns>是否本次实际创建。</returns>
    public bool CreateBrowser(int width, int height)
    {
        if (width < 0 || height < 0)
            return false;
        bool created = false;
        CefPlatform.RunOnCefUiThread(() => created = CreateBrowserCore(width, height));
        return created;
    }

    /// <summary>
    /// 在 CEF UI 线程创建浏览器本体。
    /// </summary>
    /// <param name="width">客户区宽度。</param>
    /// <param name="height">客户区高度。</param>
    /// <returns>是否本次实际创建。</returns>
    private bool CreateBrowserCore(int width, int height)
    {
        if (_isBrowserCreated)
            return false;

        var hostViewHandle = _control.GetHostViewHandle(width, height);
        if (hostViewHandle is not { } handle)
            return false;

        _isBrowserCreated = true;

        var windowInfo = CefWindowInfo.Create();
        SetupBrowserView(windowInfo, width, height, handle);

        _cefClient = new CommonCefClient(this);
        CefBrowserHost.CreateBrowser(windowInfo, _cefClient, new CefBrowserSettings(), "", null, null);
        return true;
    }

    /// <summary>
    /// 在主 frame 执行一段 JS。内部 marshal 到 CEF UI 线程；浏览器未就绪时静默忽略。
    /// </summary>
    /// <param name="code">要执行的 JS 脚本。</param>
    /// <param name="url">脚本所在 URL。</param>
    /// <param name="line">脚本起始行号。</param>
    public void ExecuteJavaScript(string code, string url, int line)
        => CefPlatform.RunOnCefUiThread(() => _browser?.GetMainFrame().ExecuteJavaScript(code, url, line));

    /// <summary>
    /// 打开 DevTools（独立窗口）。内部 marshal 到 CEF UI 线程。
    /// DevTools 仅支持 Chrome 样式；**不设父窗口**——主浏览器是顶层窗口的子控件，
    /// GetWindowHandle() 返回子窗口句柄，作 SetAsPopup 父句柄会被 CEF 用作 DevTools 宿主
    /// → DevTools 顶替主网页内容显示（实测）。不设父窗口则 CEF 用默认独立 DevTools 窗口。
    /// </summary>
    public void ShowDeveloperTools()
        => CefPlatform.RunOnCefUiThread(() =>
        {
            if (_browser is null || _cefClient is null)
                return;
            var host = _browser.GetHost();
            var windowInfo = CefWindowInfo.Create();
            windowInfo.RuntimeStyle = CefRuntimeStyle.Chrome; // DevTools 仅支持 Chrome 样式
            host.ShowDevTools(windowInfo, _cefClient, new CefBrowserSettings(), new CefPoint());
        });

    /// <summary>
    /// 关闭浏览器（forceClose=false：让 CEF 跑 beforeunload 等再关）。内部 marshal 到 CEF UI 线程。
    /// 随后 on_before_close → BrowserClosed。
    /// </summary>
    /// <param name="force">是否强制立即关闭。</param>
    public void CloseBrowser(bool force)
        => CefPlatform.RunOnCefUiThread(() => _browser?.GetHost().CloseBrowser(force));

    /// <summary>
    /// 填充浏览器视图挂载信息（镜像上游 CommonBrowserAdapter.SetupBrowserView）：
    /// SetAsChild + WS_EX_NOACTIVATE、**不设 runtime_style**（MTML=true 的 Chrome bootstrap 下解析为
    /// Chrome 样式子窗口嵌入），DevTools（Chrome-only）才能附着。
    /// </summary>
    /// <param name="windowInfo">CefWindowInfo 待填充。</param>
    /// <param name="width">客户区宽度。</param>
    /// <param name="height">客户区高度。</param>
    /// <param name="parentHandle">父窗口句柄。</param>
    private void SetupBrowserView(CefWindowInfo windowInfo, int width, int height, IntPtr parentHandle)
    {
        windowInfo.StyleEx |= WindowStyleEx.WS_EX_NOACTIVATE; // 防浏览器抢焦点（镜像上游）
        windowInfo.SetAsChild(parentHandle, new CefRectangle(0, 0, width, height));
    }

    /// <summary>
    /// 暂存起始 URL（浏览器未建时）或立即导航（已建）。内部 marshal 到 CEF UI 线程。
    /// </summary>
    /// <param name="url">目标 URL。</param>
    private void NavigateTo(string url)
        => CefPlatform.RunOnCefUiThread(() =>
        {
            url = url.TrimStart();
            if (_browser is not null)
                _browser.GetMainFrame().LoadUrl(url);
            else
                _initialUrl = url;
        });

    /// <summary>
    /// 宿主控件尺寸变化：首次触发创建浏览器（之后取消订阅）。
    /// </summary>
    /// <param name="size">新尺寸。</param>
    private void HandleControlSizeChanged(CefSize size)
    {
        if (CreateBrowser(size.Width, size.Height))
            _control.SizeChanged -= HandleControlSizeChanged;
    }

    /// <summary>
    /// on_after_created：记录主浏览器、加载暂存的起始 URL、触发 Initialized。
    /// </summary>
    /// <param name="browser">已创建的浏览器。</param>
    private void OnBrowserCreated(CefBrowser browser)
    {
        if (_browser is not null)
            return; // DevTools 等附加浏览器不覆盖主引用

        _browser = browser;
        if (!string.IsNullOrEmpty(_initialUrl))
        {
            _browser.GetMainFrame().LoadUrl(_initialUrl);
            _initialUrl = "";
        }
        Initialized?.Invoke();
    }

    /// <summary>
    /// DoClose：恒返回 false，让 CEF 走完整关闭（→ on_before_close → 本宿主清理）。
    /// </summary>
    /// <param name="browser">正在关闭的浏览器。</param>
    /// <returns>恒 false。</returns>
    private bool OnBrowserClose(CefBrowser browser) => false;

    void ICefBrowserHost.HandleBrowserCreated(CefBrowser browser) => OnBrowserCreated(browser);

    void ICefBrowserHost.HandleBrowserDestroyed(CefBrowser browser)
    {
        if (!ReferenceEquals(_browser, browser))
            return; // DevTools 等附加浏览器不触发

        _browser = null;
        _cefClient = null;
        BrowserClosed?.Invoke(browser);
    }

    bool ICefBrowserHost.HandleBrowserClose(CefBrowser browser) => OnBrowserClose(browser);

    void ICefBrowserHost.HandleLoadEnd(CefBrowser browser, CefFrame frame, int httpStatusCode)
    {
        if (frame.IsMain)
            LoadEnd?.Invoke(_eventsEmitter, new LoadEndEventArgs(frame, httpStatusCode));
    }
}
