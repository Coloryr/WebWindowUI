using System.ComponentModel;
using System.Runtime.InteropServices;
using Xilium.CefGlue;
using Xilium.CefGlue.Platform.Windows;

namespace WebWindowUI.Cef;

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
/// （跨线程经 <see cref="MessageLoopSynchronizationContext"/> marshal，与 Windows 平台同构）。
/// </summary>
public sealed class CefWindow : IWindowBackend
{
    private const string WindowClass = "CefWindow";

    // ---- 进程级共享状态 ----
    private static bool _classRegistered;
    private static Win32.WndProcDelegate _wndProc = null!; // 保活，防止被 GC 回收
    private static readonly Dictionary<IntPtr, CefWindow> _windows = [];

    private readonly IntPtr _hwnd;
    private readonly WebWindowOptions _options;

    // 本窗口的 CEF 托管回调对象（CefGlue HANDLER 角色；CEF 持原生引用，这里再字段保活到窗口关闭）
    private WwuiCefClient _client = null!;
    private WwuiCefLifeSpanHandler _lifeSpanHandler = null!;
    private WwuiCefLoadHandler _loadHandler = null!;

    private CefBrowser? _browser; // on_after_created 记录；on_before_close 置空
    private IntPtr _hIcon;
    private bool _closed;

    public IntPtr Hwnd => _hwnd;

    /// <summary>窗口销毁时触发（用户关闭或 Close()）。宿主在此清理与窗口关联的状态。</summary>
    public event Action? Closed;

    /// <summary>窗口选项（scheme / resolver），scheme 处理器按请求分派到对应窗口时读取。</summary>
    internal WebWindowOptions Options => _options;

    private CefWindow(IntPtr hwnd, WebWindowOptions options)
    {
        _hwnd = hwnd;
        _options = options;
        _windows[hwnd] = this;
    }

    /// <summary>创建并注册一个尚未显示的窗口。</summary>
    public static CefWindow Create(string title, WebWindowOptions options, int width, int height)
    {
        EnsureClassRegistered();

        IntPtr hwnd = Win32.CreateWindowExW(
            0, WindowClass, title, Win32.WS_OVERLAPPEDWINDOW,
            Win32.CW_USEDEFAULT, Win32.CW_USEDEFAULT, width, height,
            IntPtr.Zero, IntPtr.Zero, Win32.GetModuleHandleW(null), IntPtr.Zero);
        if (hwnd == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "创建窗口失败 (CreateWindowExW)");

        var window = new CefWindow(hwnd, options);
        window.CreateHandlers();
        return window;
    }

    /// <summary>显示窗口并创建 CEF 浏览器（CefWindowInfo 设父窗口，CEF 子窗口铺满客户区）。无头模式只建浏览器、窗口永不显示。</summary>
    public void Show()
    {
        if (!_options.Headless)
            Win32.ShowWindow(_hwnd, Win32.SW_SHOW);
        CreateBrowser();
    }

    /// <summary>隐藏窗口（不关闭、不销毁）。</summary>
    public void Hide() => Win32.ShowWindow(_hwnd, Win32.SW_HIDE);

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
            else
                Win32.DestroyWindow(_hwnd); // 浏览器未建成：直接销毁窗口收尾
        });
    }

    /// <summary>把窗口带到前台并聚焦：先恢复最小化，再置前、设焦点。</summary>
    public void Activate()
    {
        RunOnUiThread(() =>
        {
            if (Win32.IsIconic(_hwnd))
                Win32.ShowWindow(_hwnd, Win32.SW_RESTORE);
            Win32.SetForegroundWindow(_hwnd);
            Win32.SetFocus(_hwnd);
        });
    }

    /// <summary>修改窗口标题（立即同步到标题栏）。</summary>
    public void SetTitle(string title)
        => RunOnUiThread(() => Win32.SetWindowTextW(_hwnd, title));

    /// <summary>设置窗口图标（标题栏 + 任务栏）。替换旧图标时释放旧的句柄。</summary>
    public void SetIcon(WindowIcon icon)
    {
        RunOnUiThread(() =>
        {
            IntPtr hIcon = LoadIconHandle(icon);
            if (hIcon == IntPtr.Zero)
                return;

            if (_hIcon != IntPtr.Zero)
                Win32.DestroyIcon(_hIcon);
            _hIcon = hIcon;

            Win32.SendMessageW(_hwnd, Win32.WM_SETICON, (IntPtr)Win32.ICON_BIG, hIcon);
            Win32.SendMessageW(_hwnd, Win32.WM_SETICON, (IntPtr)Win32.ICON_SMALL, hIcon);
        });
    }

    /// <summary>
    /// 把动作 marshal 到 UI 线程同步执行：UI 线程直接运行；非 UI 线程经
    /// <see cref="MessageLoopSynchronizationContext.Send"/>（回 UI 线程并阻塞等待）。
    /// Win32 窗口 API（DestroyWindow/SetForegroundWindow/SetWindowTextW/SendMessage）与 CEF 调用都要求 UI 线程。
    /// </summary>
    private void RunOnUiThread(Action action)
    {
        if (Environment.CurrentManagedThreadId == MessageLoopSynchronizationContext.UiThreadId)
        {
            action();
            return;
        }
        MessageLoopSynchronizationContext.Instance.Send(_ => action(), null);
    }

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
            if (Environment.CurrentManagedThreadId != MessageLoopSynchronizationContext.UiThreadId)
            {
                MessageLoopSynchronizationContext.Instance.Post(_ => PostMessage(message), null);
                return;
            }
            if (_closed || _browser is null)
                return;
            string js = "window.wwuiReceive(" + JsStringLiteral.Quote(WebView2StringCodec.Encode(message)) + ")";
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
        if (Environment.CurrentManagedThreadId != MessageLoopSynchronizationContext.UiThreadId)
        {
            var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            MessageLoopSynchronizationContext.Instance.Post(async _ =>
            {
                try { tcs.TrySetResult(await ExecuteScriptAsync(script)); }
                catch (Exception ex) { tcs.TrySetException(ex); }
            }, null);
            return await tcs.Task;
        }

        // 与 Windows 的 InvalidOperationException("WebView2 尚未初始化完成。") 对齐：窗口已关闭时明确报错
        if (_closed)
            throw new InvalidOperationException("窗口已关闭。");
        if (_browser is not null)
            ExecuteJavaScriptOnBrowser(script);
        return "";
    }

    /// <summary>页面导航完成时触发（用于在页面就绪后推送 Model 初始快照）。</summary>
    public event Action? NavigationCompleted;

    /// <summary>页面 JS 经 fetch POST（app://localhost/__wwui）回传的消息（protobuf 字节，scheme 处理器还原后投递）。</summary>
    public event Action<byte[]>? MessageReceived;

    /// <summary>scheme 处理器（CefSchemes）在 IO 线程收到 JS 回传，marshal 回 UI 线程后调用本方法。回调在 UI 线程。</summary>
    internal void OnMessageFromWeb(byte[] payload) => MessageReceived?.Invoke(payload);

    /// <summary>CEF on_load_end（is_main）→ 主页面导航完成。回调在 UI 线程。</summary>
    internal void OnNavigationCompleted() => NavigationCompleted?.Invoke();

    /// <summary>CEF on_after_created：记录浏览器包装并注册 scheme 映射。回调在 UI 线程。</summary>
    internal void OnBrowserCreated(CefBrowser browser)
    {
        _browser = browser;
        CefSchemes.RegisterBrowser(browser, this); // scheme 处理器按浏览器 id 分派回本窗口
    }

    /// <summary>CEF on_before_close：摘除浏览器映射、置空浏览器引用，销毁宿主顶层窗口完成收尾（→ WM_DESTROY → 末窗 PostQuitMessage）。</summary>
    internal void OnBrowserClosing()
    {
        if (_browser is not null)
        {
            CefSchemes.UnregisterBrowser(_browser);
            _browser = null;
        }
        if (!_closed)
            Win32.DestroyWindow(_hwnd); // CEF 子窗口已销毁，顶层窗口随浏览器一起消失
    }

    private void CreateHandlers()
    {
        // 每个窗口一套 CefGlue 托管回调对象（client + life_span + load）。CEF 在 CreateBrowser
        // 时对 client 及 GetLifeSpanHandler/GetLoadHandler 返回值 add_ref（CefGlue _roots 保活），
        // 这里字段再显式保活到窗口关闭，杜绝托管对象提前回收。
        _lifeSpanHandler = new WwuiCefLifeSpanHandler(this);
        _loadHandler = new WwuiCefLoadHandler(this);
        _client = new WwuiCefClient(this, _lifeSpanHandler, _loadHandler);
    }

    /// <summary>创建 CEF 浏览器。必须在 UI 线程（Show 从 Main 的 UI 线程调用）。</summary>
    private void CreateBrowser()
    {
        if (_browser is not null)
            return;

        Win32.GetClientRect(_hwnd, out Win32.RECT rc);
        var windowInfo = CefWindowInfo.Create();
        windowInfo.ParentHandle = _hwnd;
        windowInfo.Style = WindowStyle.WS_CHILD | WindowStyle.WS_CLIPCHILDREN
            | WindowStyle.WS_CLIPSIBLINGS | WindowStyle.WS_TABSTOP | WindowStyle.WS_VISIBLE;
        windowInfo.Bounds = new CefRectangle(0, 0, rc.Right, rc.Bottom);
        // **durable：RuntimeStyle 必须显式 ALLOY（2）！CEF 151 起 DEFAULT(0) 即 Chrome style**
        // （browser_host_create.cc IsChromeStyle：`DEFAULT || CHROME` 都走 ChromeBrowserHostImpl）——
        // Chrome style 走 Chrome UI 的 tab 创建路径，首屏导航的 RFH 匹配不到 CefBrowserHost，
        // 渲染进程 GetNewBrowserInfo 同步超时 2s → 视图被判 EXCLUDED → 子资源永不加载（页面挂白）。
        // 本平台全部按 Alloy 语义实现（SetAsChild/回调/自定义 scheme），必须显式锁定 ALLOY。
        windowInfo.RuntimeStyle = CefRuntimeStyle.Alloy;

        // url 与 client 由 CEF 复制/引用，调用返回即可释放本侧 windowInfo（finalizer 兜底）。
        CefBrowserHost.CreateBrowser(
            windowInfo, _client, new CefBrowserSettings(), _options.HomeUrl, null, null);
    }

    /// <summary>正常关闭浏览器（forceClose=false：让 CEF 跑 beforeunload 等再关）。CEF 随后调 DoClose → on_before_close。</summary>
    private void CloseBrowserGraceful()
    {
        if (_browser is null)
            return;
        using CefBrowserHost host = _browser.GetHost();
        host.CloseBrowser(false);
    }

    /// <summary>在浏览器主 frame 里执行一段 JS。必须在 UI 线程且浏览器存活。</summary>
    private void ExecuteJavaScriptOnBrowser(string js)
    {
        if (_browser is null)
            return;
        CefFrame frame = _browser.GetMainFrame();
        frame.ExecuteJavaScript(js, string.Empty, 0);
    }

    /// <summary>把 WindowIcon（文件或流）加载成 HICON。流会先落到临时文件再加载。</summary>
    private static IntPtr LoadIconHandle(WindowIcon icon)
    {
        if (icon.FilePath is not null)
        {
            return Win32.LoadImageW(IntPtr.Zero, icon.FilePath, Win32.IMAGE_ICON,
                0, 0, Win32.LR_LOADFROMFILE | Win32.LR_DEFAULTSIZE);
        }

        if (icon.Stream is not null)
        {
            string tmp = Path.Combine(Path.GetTempPath(), "webwindowui_" + Guid.NewGuid().ToString("N") + ".ico");
            try
            {
                using (FileStream fs = File.Create(tmp))
                    icon.Stream.CopyTo(fs);
                return Win32.LoadImageW(IntPtr.Zero, tmp, Win32.IMAGE_ICON,
                    0, 0, Win32.LR_LOADFROMFILE | Win32.LR_DEFAULTSIZE);
            }
            finally
            {
                try { File.Delete(tmp); } catch { /* 临时文件清理失败可忽略 */ }
            }
        }

        return IntPtr.Zero;
    }

    private static void EnsureClassRegistered()
    {
        if (_classRegistered)
            return;

        _wndProc = WndProc;
        var wc = new Win32.WNDCLASSEXW
        {
            cbSize = (uint)Marshal.SizeOf<Win32.WNDCLASSEXW>(),
            style = Win32.CS_HREDRAW | Win32.CS_VREDRAW,
            lpfnWndProc = _wndProc,
            hInstance = Win32.GetModuleHandleW(null),
            hIcon = Win32.LoadIconW(IntPtr.Zero, Win32.IDI_APPLICATION),
            hCursor = Win32.LoadCursorW(IntPtr.Zero, Win32.IDC_ARROW),
            hbrBackground = (IntPtr)(Win32.COLOR_WINDOW + 1),
            lpszMenuName = null,
            lpszClassName = WindowClass,
        };
        if (Win32.RegisterClassExW(ref wc) == 0)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "注册窗口类失败 (RegisterClassExW)");

        _classRegistered = true;
    }

    /// <summary>窗口过程入口：通过 HWND 找到对应的窗口实例。</summary>
    private static IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
        => _windows.TryGetValue(hwnd, out CefWindow? window)
            ? window.OnWndProc(msg, wParam, lParam)
            : Win32.DefWindowProcW(hwnd, msg, wParam, lParam);

    private IntPtr OnWndProc(uint msg, IntPtr wParam, IntPtr lParam)
    {
        switch (msg)
        {
            case Win32.WM_CLOSE:
                // 用户点标题栏 X：交给 CEF 正常关闭；浏览器未建成直接销毁
                if (_browser is not null)
                    CloseBrowserGraceful();
                else
                    Win32.DestroyWindow(_hwnd);
                return IntPtr.Zero;

            case Win32.WM_DESTROY:
                // 最终收尾（由 on_before_close 里的 DestroyWindow 触发）
                if (_hIcon != IntPtr.Zero)
                {
                    Win32.DestroyIcon(_hIcon);
                    _hIcon = IntPtr.Zero;
                }
                _windows.Remove(_hwnd);
                _closed = true;
                Closed?.Invoke();
                WebWindow.NotifyWindowClosed();
                if (WebWindow.OpenCount == 0)
                    Win32.PostQuitMessage(0); // 最后一个窗口关闭，退出消息循环
                return IntPtr.Zero;

            case Win32.WM_SIZE:
                ResizeBrowser();
                return IntPtr.Zero;

            default:
                return Win32.DefWindowProcW(_hwnd, msg, wParam, lParam);
        }
    }

    /// <summary>父窗口尺寸变化：通知 CEF 重排（CEF 会把自己的子窗口铺满父客户区）。</summary>
    private void ResizeBrowser()
    {
        if (_browser is null)
            return;
        using CefBrowserHost host = _browser.GetHost();
        host.WasResized();
    }
}

/// <summary>本窗口的 CefClient 子类：返回生命期与加载期处理器（CEF 在浏览器创建时读取并缓存）。</summary>
internal sealed class WwuiCefClient : CefClient
{
    private readonly CefWindow _window;
    private readonly WwuiCefLifeSpanHandler _lifeSpan;
    private readonly WwuiCefLoadHandler _load;

    public WwuiCefClient(CefWindow window, WwuiCefLifeSpanHandler lifeSpan, WwuiCefLoadHandler load)
    {
        _window = window;
        _lifeSpan = lifeSpan;
        _load = load;
    }

    protected override CefLifeSpanHandler? GetLifeSpanHandler() => _lifeSpan;
    protected override CefLoadHandler? GetLoadHandler() => _load;
}

/// <summary>本窗口的 CefLifeSpanHandler 子类：创建 / 关闭浏览器回调路由回窗口。</summary>
internal sealed class WwuiCefLifeSpanHandler : CefLifeSpanHandler
{
    private readonly CefWindow _window;

    public WwuiCefLifeSpanHandler(CefWindow window) => _window = window;

    protected override void OnAfterCreated(CefBrowser browser) => _window.OnBrowserCreated(browser);

    /// <summary>返回 false：让 CEF 继续关闭流程（会触发 on_before_close → 本平台销毁顶层窗口）。</summary>
    protected override bool DoClose(CefBrowser browser) => false;

    protected override void OnBeforeClose(CefBrowser browser) => _window.OnBrowserClosing();
}

/// <summary>本窗口的 CefLoadHandler 子类：主 frame 加载完成触发 NavigationCompleted。</summary>
internal sealed class WwuiCefLoadHandler : CefLoadHandler
{
    private readonly CefWindow _window;

    public WwuiCefLoadHandler(CefWindow window) => _window = window;

    protected override void OnLoadEnd(CefBrowser browser, CefFrame frame, int httpStatusCode)
    {
        if (frame.IsMain)
            _window.OnNavigationCompleted();
    }
}
