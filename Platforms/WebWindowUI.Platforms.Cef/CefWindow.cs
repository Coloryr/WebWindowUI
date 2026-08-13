using WebWindowUI.Core;
using WebWindowUI.Core.Protocol;
using WebWindowUI.Natives.Windows;
using Xilium.CefGlue;
using Xilium.CefGlue.Platform.Windows;

namespace WebWindowUI.Platforms.Cef;

/// <summary>
/// CEF 平台：承载 Chromium 的裸 Win32 顶层窗口（CEF 子浏览器窗口为子控件），可创建多个实例。
/// 镜像 <c>WindowsWindow</c>，渲染内核换 CEF（CefGlue 托管包装）；单线程消息循环下 CEF UI 线程 == 主线程。
/// </summary>
public sealed class CefWindow : IWindowBackend
{
    /// <summary>
    /// 承载浏览器子窗口的 Win32 顶层窗口。
    /// </summary>
    private readonly INativeWindow _nativeWindow;
    /// <summary>
    /// CefClient 及其处理器。
    /// </summary>
    private readonly WwuiCefClient _client;
    /// <summary>
    /// 生命周期处理器（创建/关闭浏览器回调）。
    /// </summary>
    private readonly WwuiCefLifeSpanHandler _lifeSpanHandler;
    /// <summary>
    /// 加载处理器（on_load_end → NavigationCompleted）。
    /// </summary>
    private readonly WwuiCefLoadHandler _loadHandler;

    /// <summary>
    /// 主浏览器；on_after_created 记录，on_before_close 置空。
    /// </summary>
    private CefBrowser? _browser; // on_after_created 记录；on_before_close 置空
    /// <summary>
    /// 是否已关闭。
    /// </summary>
    private bool _closed;

    /// <summary>
    /// 窗口销毁时触发（用户关闭或 Close()）。宿主在此清理与窗口关联的状态。
    /// </summary>
    public event Action? Closed;

    /// <summary>
    /// 窗口选项（scheme / resolver），scheme 处理器按请求分派到对应窗口时读取。
    /// </summary>
    private WebWindowOptions _options;

    /// <summary>
    /// 构造窗口：建 Win32 顶层窗口与 CEF 处理器（浏览器延后到 Show 时创建）。
    /// </summary>
    /// <param name="options">窗口选项。</param>
    public CefWindow(WebWindowOptions options)
    {
        _options = options;

        _nativeWindow = new Win32NativeWindow(options);
        _lifeSpanHandler = new WwuiCefLifeSpanHandler(this);
        _loadHandler = new WwuiCefLoadHandler(this);
        _client = new WwuiCefClient(this, _lifeSpanHandler, _loadHandler);
    }

    /// <summary>
    /// 显示窗口并创建 CEF 浏览器（CefWindowInfo 设父窗口，CEF 子窗口铺满客户区）。无头模式只建浏览器、窗口永不显示。
    /// </summary>
    public void Show()
    {
        _nativeWindow.Show();
        CreateBrowser();
    }

    /// <summary>
    /// 隐藏窗口（不关闭、不销毁）。
    /// </summary>
    public void Hide()
    {
        _nativeWindow.Hide();
    }

    /// <summary>
    /// 关闭窗口。关闭最后一个窗口后程序自动退出。
    /// 走 CEF 正常关闭（CloseBrowser(false) → DoClose → on_before_close → DestroyWindow → WM_DESTROY）。
    /// </summary>
    public void Close()
    {
        // 关窗必须在创建窗口的线程（CEF UI 线程）调用；宿主可能从任意线程关窗，marshal 回 UI 线程同步执行。
        RunOnUiThread(() =>
        {
            if (_closed)
                return;
            if (_browser is not null)
                CloseBrowserGraceful();

            _nativeWindow.Close();
        });
    }

    /// <summary>
    /// 把窗口带到前台并聚焦：先恢复最小化，再置前、设焦点。
    /// </summary>
    public void Activate()
    {
        RunOnUiThread(_nativeWindow.Activate);
    }

    /// <summary>
    /// 修改窗口标题（立即同步到标题栏）。
    /// </summary>
    /// <param name="title">新标题。</param>
    public void SetTitle(string title)
    {
        RunOnUiThread(() => _nativeWindow.SetTitle(title));
    }

    /// <summary>
    /// 设置窗口图标（标题栏 + 任务栏）。替换旧图标时释放旧的句柄。
    /// </summary>
    /// <param name="icon">图标。</param>
    public void SetIcon(WindowIcon icon)
    {
        RunOnUiThread(() =>
        {
            _nativeWindow.SetIcon(icon);
        });
    }

    /// <summary>
    /// 把动作 marshal 到 UI 线程同步执行（Win32 窗口 API 与 CEF 调用都要求 UI 线程）。
    /// </summary>
    /// <param name="action">要执行的动作。</param>
    private void RunOnUiThread(Action action)
        => WebWindowPlatform.Current.RunOnUiThread(action);

    /// <summary>
    /// 向页面 JS 发送一条消息：protobuf 字节转 NUL 转义串后嵌进 <c>window.wwuiReceive("...")</c> 注入。
    /// 窗口已关闭时静默忽略。
    /// </summary>
    /// <param name="message">protobuf 字节。</param>
    public void PostMessage(byte[] message)
    {
        try
        {
            // ExecuteJavaScript 只能在 UI 线程调用。属性变更可能发生在任意线程
            // （如示例的 System.Threading.Timer 回调），非 UI 线程调用时先投递回 UI 线程。
            // 用线程 id 判断而非 SynchronizationContext.Current：Timer 会随 ExecutionContext
            // 把 UI 线程的上下文流到线程池线程，SynchronizationContext.Current 会误判。
            if (!WebWindowPlatform.Current.IsUiThread())
            {
                WebWindowPlatform.Current.RunOnUiThread(() => PostMessage(message));
                return;
            }
            if (_closed || _browser is null)
                return;
            var js = "window.wwuiReceive(" + JsStringLiteral.Quote(WebView2StringCodec.Encode(message)) + ")";
            ExecuteJavaScriptOnBrowser(js);
        }
        catch
        {
            // 窗口关闭后浏览器已销毁，忽略（定时器仍在后台推送）
        }
    }

    /// <summary>
    /// 在页面里执行一段 JavaScript 并返回结果（与 WebView2/Linux 对齐；best-effort）。
    /// CEF 的 ExecuteJavaScript 无结果回调，返回值固定为空串；非 UI 线程先投递回 UI 线程再执行。
    /// </summary>
    /// <param name="script">要执行的 JS 脚本。</param>
    /// <returns>执行结果（JSON 编码字符串；CEF 下固定空串）。</returns>
    public async Task<string> ExecuteScriptAsync(string script)
    {
        if (!WebWindowPlatform.Current.IsUiThread())
        {
            var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            WebWindowPlatform.Current.RunOnUiThread(async () =>
            {
                try { tcs.TrySetResult(await ExecuteScriptAsync(script)); }
                catch (Exception ex) { tcs.TrySetException(ex); }
            });
            return await tcs.Task;
        }

        // 与 Windows 的 InvalidOperationException("WebView2 尚未初始化完成。") 对齐：窗口已关闭时明确报错
        if (_closed)
            throw new InvalidOperationException("窗口已关闭。");
        if (_browser is not null)
            ExecuteJavaScriptOnBrowser(script);
        return "";
    }

    /// <summary>
    /// 页面导航完成时触发（用于在页面就绪后推送 Model 初始快照）。
    /// </summary>
    public event Action? NavigationCompleted;

    /// <summary>
    /// 页面 JS 经 fetch POST（app://localhost/__wwui）回传的消息（protobuf 字节，scheme 处理器还原后投递）。
    /// </summary>
    public event Action<byte[]>? MessageReceived;

    /// <summary>
    /// scheme 处理器在 IO 线程收到 JS 回传、marshal 回 UI 线程后调用本方法。回调在 UI 线程。
    /// </summary>
    /// <param name="payload">protobuf 字节。</param>
    internal void OnMessageFromWeb(byte[] payload) => MessageReceived?.Invoke(payload);

    /// <summary>
    /// CEF on_load_end（is_main）→ 主页面导航完成。回调在 UI 线程。
    /// </summary>
    internal void OnNavigationCompleted() => NavigationCompleted?.Invoke();

    /// <summary>
    /// CEF on_after_created：记录主浏览器并注册 scheme 映射，随后自动打开 DevTools。
    /// DevTools 等附加浏览器也走本回调，用 <c>_browser is not null</c> 守卫跳过。
    /// </summary>
    /// <param name="browser">已创建的浏览器。</param>
    internal void OnBrowserCreated(CefBrowser browser)
    {
        if (_browser is not null)
            return; // 附加浏览器（如 DevTools 弹窗）不覆盖主浏览器引用

        _browser = browser;
        CefPlatform.RegisterBrowser(browser, this); // scheme 处理器按浏览器 id 分派回本窗口

        OpenDevTools(browser); // 自动打开调试工具（调试期便利，勿合入生产）
    }

    /// <summary>
    /// CEF on_before_close：主浏览器关闭才摘除映射、销毁宿主顶层窗口（→ WM_DESTROY → 末窗 PostQuitMessage）。
    /// DevTools 等附加浏览器按引用比对守卫，不误关主窗口。
    /// </summary>
    /// <param name="browser">正在关闭的浏览器。</param>
    internal void OnBrowserClosing(CefBrowser browser)
    {
        if (!ReferenceEquals(_browser, browser))
            return; // 不是主浏览器（DevTools 等附加浏览器），不摘主映射、不关主窗口

        CefPlatform.UnregisterBrowser(browser);
        _browser = null;
        if (!_closed)
            _nativeWindow.Close(); // CEF 子窗口已销毁，顶层窗口随浏览器一起消失
    }

    /// <summary>
    /// 创建 CEF 浏览器。必须在 UI 线程（Show 从 Main 的 UI 线程调用）。
    /// </summary>
    private void CreateBrowser()
    {
        if (_browser is not null)
            return;

        var rc = _nativeWindow.GetSize(); // Rectangle(0,0,客户区宽,客户区高)
        var windowInfo = CefWindowInfo.Create();
        windowInfo.ParentHandle = _nativeWindow.WindowHandle;
        windowInfo.Style = WindowStyle.WS_CHILD | WindowStyle.WS_CLIPCHILDREN
            | WindowStyle.WS_CLIPSIBLINGS | WindowStyle.WS_TABSTOP | WindowStyle.WS_VISIBLE;
        windowInfo.Bounds = new CefRectangle(rc.Left, rc.Top, rc.Width, rc.Height);

        windowInfo.RuntimeStyle = CefRuntimeStyle.Alloy;

        CefBrowserHost.CreateBrowser(
            windowInfo, _client, new CefBrowserSettings(), WebWindowResource.GetWindowIndexUrl(_options.WindowPath), null, null);
    }

    /// <summary>
    /// 打开 DevTools 调试工具（独立弹窗；Windows 用原生弹窗，其它平台用 CEF 默认窗口）。必须在 UI 线程。
    /// </summary>
    /// <param name="browser">主浏览器。</param>
    private void OpenDevTools(CefBrowser browser)
    {
        using CefBrowserHost host = browser.GetHost();
        var windowInfo = CefWindowInfo.Create();
        windowInfo.RuntimeStyle = CefRuntimeStyle.Alloy; // 与主浏览器一致，Alloy 样式
        if (CefRuntime.Platform == CefRuntimePlatform.Windows)
            windowInfo.SetAsPopup(host.GetWindowHandle(), "DevTools");
        host.ShowDevTools(windowInfo, _client, new CefBrowserSettings(), new CefPoint(0, 0));
    }

    /// <summary>
    /// 正常关闭浏览器（forceClose=false：让 CEF 跑 beforeunload 等再关）。CEF 随后调 DoClose → on_before_close。
    /// </summary>
    private void CloseBrowserGraceful()
    {
        if (_browser is null)
            return;
        using CefBrowserHost host = _browser.GetHost();
        host.CloseBrowser(false);
    }

    /// <summary>
    /// 在浏览器主 frame 里执行一段 JS。必须在 UI 线程且浏览器存活。
    /// </summary>
    /// <param name="js">要执行的 JS 脚本。</param>
    private void ExecuteJavaScriptOnBrowser(string js)
    {
        if (_browser is null)
            return;
        var frame = _browser.GetMainFrame();
        frame.ExecuteJavaScript(js, string.Empty, 0);
    }

    /// <summary>
    /// 父窗口尺寸变化：通知 CEF 重排（CEF 会把自己的子窗口铺满父客户区）。
    /// </summary>
    private void ResizeBrowser()
    {
        if (_browser is null)
            return;
        using CefBrowserHost host = _browser.GetHost();
        host.WasResized();
    }
}

/// <summary>
/// 本窗口的 CefClient 子类：返回生命期与加载期处理器（CEF 在浏览器创建时读取并缓存）。
/// </summary>
internal sealed class WwuiCefClient(CefWindow window, WwuiCefLifeSpanHandler lifeSpan, WwuiCefLoadHandler load) : CefClient
{
    protected override CefLifeSpanHandler? GetLifeSpanHandler() => lifeSpan;
    protected override CefLoadHandler? GetLoadHandler() => load;
}

/// <summary>
/// 本窗口的 CefLifeSpanHandler 子类：创建 / 关闭浏览器回调路由回窗口。
/// </summary>
internal sealed class WwuiCefLifeSpanHandler(CefWindow window) : CefLifeSpanHandler
{
    protected override void OnAfterCreated(CefBrowser browser) => window.OnBrowserCreated(browser);

    /// <summary>
    /// 返回 false：让 CEF 继续关闭流程（会触发 on_before_close → 本平台销毁顶层窗口）。
    /// </summary>
    protected override bool DoClose(CefBrowser browser) => false;

    protected override void OnBeforeClose(CefBrowser browser) => window.OnBrowserClosing(browser);
}

/// <summary>
/// 本窗口的 CefLoadHandler 子类：主 frame 加载完成触发 NavigationCompleted。
/// </summary>
internal sealed class WwuiCefLoadHandler(CefWindow window) : CefLoadHandler
{
    protected override void OnLoadEnd(CefBrowser browser, CefFrame frame, int httpStatusCode)
    {
        if (frame.IsMain)
            window.OnNavigationCompleted();
    }
}
