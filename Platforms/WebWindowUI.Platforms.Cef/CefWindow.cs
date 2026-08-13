using System.ComponentModel;
using System.Runtime.InteropServices;
using WebWindowUI.Core;
using WebWindowUI.Core.Protocol;
using WebWindowUI.Natives.Windows;
using Xilium.CefGlue;
using Xilium.CefGlue.Platform.Windows;

namespace WebWindowUI.Platforms.Cef;

/// <summary>
/// CEF 平台：承载 Chromium 的裸 Win32 顶层窗口（CEF 子浏览器窗口为子控件），可创建多个实例。
/// 逐行镜像 <c>WebWindowUI.Platforms.Windows.WindowsWindow</c>，渲染内核换 CEF（CefGlue 托管包装）。
///
/// 生命周期：Show() 建浏览器（CefWindowInfo.Create 设 ParentHandle=本窗口）→ on_after_created
/// 记 CefBrowser 引用并注册 id → on_load_end(is_main) 触发 NavigationCompleted → WM_SIZE 调 was_resized →
/// 关闭（WM_CLOSE / Close()）走 CloseBrowser(false) 正常关闭 → DoClose 返回 false 让 CEF 继续 →
/// on_before_close 摘除浏览器映射并 DestroyWindow → WM_DESTROY 收尾 + 末窗 PostQuitMessage。
///
/// 线程模型：CEF 单线程消息循环（multi_threaded_message_loop=false，CefRuntime.RunMessageLoop）→
/// CEF UI 线程 == 主线程；全部 CEF 回调在 UI 线程到达，Win32 窗口 API 与 CEF 调用都要求 UI 线程
/// （跨线程经 <see cref="CefPlatform.RunOnUiThread"/> marshal，与 Windows 平台同构）。
/// </summary>
public sealed class CefWindow : IWindowBackend
{
    private readonly INativeWindow _nativeWindow;
    private readonly WwuiCefClient _client;
    private readonly WwuiCefLifeSpanHandler _lifeSpanHandler;
    private readonly WwuiCefLoadHandler _loadHandler;

    private CefBrowser? _browser; // on_after_created 记录；on_before_close 置空
    private IntPtr _hIcon;
    private bool _closed;

    /// <summary>
    /// 窗口销毁时触发（用户关闭或 Close()）。宿主在此清理与窗口关联的状态。
    /// </summary>
    public event Action? Closed;

    /// <summary>
    /// 窗口选项（scheme / resolver），scheme 处理器按请求分派到对应窗口时读取。
    /// </summary>
    private WebWindowOptions _options;

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
    public void SetTitle(string title)
    {
        RunOnUiThread(() => _nativeWindow.SetTitle(title));
    }

    /// <summary>
    /// 设置窗口图标（标题栏 + 任务栏）。替换旧图标时释放旧的句柄。
    /// </summary>
    public void SetIcon(WindowIcon icon)
    {
        RunOnUiThread(() =>
        {
            _nativeWindow.SetIcon(icon);
        });
    }

    /// <summary>
    /// 把动作 marshal 到 UI 线程同步执行：UI 线程直接运行；非 UI 线程经
    /// <see cref="CefPlatform.RunOnUiThread"/>（回 UI 线程并阻塞等待）。
    /// Win32 窗口 API（DestroyWindow/SetForegroundWindow/SetWindowTextW/SendMessage）与 CEF 调用都要求 UI 线程。
    /// </summary>
    private void RunOnUiThread(Action action)
        => WebWindowPlatform.Current.RunOnUiThread(action);

    /// <summary>
    /// 向页面 JS 发送一条消息。protobuf 字节经 <see cref="WebView2StringCodec"/> 转成不含 NUL 的
    /// Latin-1 字符串，再用 <see cref="JsStringLiteral.Quote"/> 嵌进 <c>window.wwuiReceive("...")</c>
    /// 经 ExecuteJavaScript 注入（与 Linux/macOS 同构；JS 端 wwuiReceive 还原后 protobufjs 解码）。
    /// 页面未加载完成或窗口已关闭时静默忽略。
    /// </summary>
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
    /// 在页面里执行一段 JavaScript 并返回结果（JSON 编码的字符串，与 WebView2/Linux 对齐；best-effort）。
    /// CEF 的 ExecuteJavaScript 无结果回调：脚本照常执行但返回值固定为空串。
    /// 与 <see cref="PostMessage"/> 一样：CEF 只能在 UI 线程访问，非 UI 线程调用时先投递回 UI 线程再执行，并等待结果。
    /// </summary>
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
    /// scheme 处理器（CefSchemes）在 IO 线程收到 JS 回传，marshal 回 UI 线程后调用本方法。回调在 UI 线程。
    /// </summary>
    internal void OnMessageFromWeb(byte[] payload) => MessageReceived?.Invoke(payload);

    /// <summary>
    /// CEF on_load_end（is_main）→ 主页面导航完成。回调在 UI 线程。
    /// </summary>
    internal void OnNavigationCompleted() => NavigationCompleted?.Invoke();

    /// <summary>
    /// CEF on_after_created：记录浏览器包装并注册 scheme 映射。回调在 UI 线程。
    /// </summary>
    internal void OnBrowserCreated(CefBrowser browser)
    {
        _browser = browser;
        CefPlatform.RegisterBrowser(browser, this); // scheme 处理器按浏览器 id 分派回本窗口
    }

    /// <summary>
    /// CEF on_before_close：摘除浏览器映射、置空浏览器引用，销毁宿主顶层窗口完成收尾（→ WM_DESTROY → 末窗 PostQuitMessage）。
    /// </summary>
    internal void OnBrowserClosing()
    {
        if (_browser is not null)
        {
            CefPlatform.UnregisterBrowser(_browser);
            _browser = null;
        }
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
        // **durable：RuntimeStyle 必须显式 ALLOY（2）！CEF 151 起 DEFAULT(0) 即 Chrome style**
        // （browser_host_create.cc IsChromeStyle：`DEFAULT || CHROME` 都走 ChromeBrowserHostImpl）——
        // Chrome style 走 Chrome UI 的 tab 创建路径，首屏导航的 RFH 匹配不到 CefBrowserHost，
        // 渲染进程 GetNewBrowserInfo 同步超时 2s → 视图被判 EXCLUDED → 子资源永不加载（页面挂白）。
        // 本平台全部按 Alloy 语义实现（SetAsChild/回调/自定义 scheme），必须显式锁定 ALLOY。
        windowInfo.RuntimeStyle = CefRuntimeStyle.Alloy;

        // url 与 client 由 CEF 复制/引用，调用返回即可释放本侧 windowInfo（finalizer 兜底）。
        CefBrowserHost.CreateBrowser(
            windowInfo, _client, new CefBrowserSettings(), WebWindowResource.GetWindowIndexUrl(_options.WindowPath), null, null);
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

    protected override void OnBeforeClose(CefBrowser browser) => window.OnBrowserClosing();
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
