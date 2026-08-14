using WebWindowUI.Core;
using WebWindowUI.Core.Protocol;
using WebWindowUI.Natives.Windows;
using Xilium.CefGlue;

namespace WebWindowUI.Platforms.Cef;

/// <summary>
/// CEF 平台窗口：承载 Chromium 的裸 Win32 顶层窗口（CEF 子浏览器窗口为子控件），可创建多个实例。
/// 镜像 <c>WindowsWindow</c>，渲染内核换 CEF（CefGlue 托管包装）；浏览器生命周期由基类 <see cref="BaseCefBrowser"/> 承担。
/// </summary>
public sealed class CefWindow : BaseCefBrowser, IWindowBackend
{
    /// <summary>
    /// 承载浏览器子窗口的 Win32 顶层窗口。
    /// </summary>
    private readonly INativeWindow _nativeWindow;

    /// <summary>
    /// 窗口选项（scheme / resolver）。
    /// </summary>
    private readonly WebWindowOptions _options;

    /// <summary>
    /// 窗口销毁时触发（用户关闭或 Close()）。宿主在此清理与窗口关联的状态。
    /// </summary>
    public event Action? Closed;

    /// <summary>
    /// 构造窗口：建 Win32 顶层窗口并订阅基类浏览器生命周期事件（浏览器延后到 Show 时创建）。
    /// </summary>
    /// <param name="options">窗口选项。</param>
    public CefWindow(WebWindowOptions options)
        : this(options, new Win32NativeWindow(options))
    {
    }

    /// <summary>
    /// 私有构造：把 Win32 顶层窗口适配成宿主控件交给基类，保存原生窗口引用。
    /// </summary>
    /// <param name="options">窗口选项。</param>
    /// <param name="nativeWindow">已创建的 Win32 顶层窗口。</param>
    private CefWindow(WebWindowOptions options, Win32NativeWindow nativeWindow)
        : base(new Win32Control(nativeWindow))
    {
        _options = options;
        _nativeWindow = nativeWindow;

        Address = WebWindowResource.GetWindowIndexUrl(options.WindowPath);

        BrowserInitialized += OnBrowserInitialized;
        BrowserClosed += OnBrowserClosed;
        LoadEnd += OnLoadEnd;
    }

    /// <summary>
    /// 显示窗口并创建 CEF 浏览器（CEF 子窗口铺满客户区）。无头模式只建浏览器、窗口永不显示。
    /// </summary>
    public void Show()
    {
        _nativeWindow.Show();
        var rc = _nativeWindow.GetSize(); // Rectangle(0,0,客户区宽,客户区高)
        CreateBrowser(rc.Width, rc.Height);
    }

    /// <summary>
    /// 隐藏窗口（不关闭、不销毁）。
    /// </summary>
    public void Hide()
    {
        RunOnUiThread(_nativeWindow.Hide);
    }

    /// <summary>
    /// 关闭窗口。关闭最后一个窗口后程序自动退出。
    /// 走 CEF 正常关闭（CloseBrowser → DoClose → on_before_close → DestroyWindow → WM_DESTROY）。
    /// CloseBrowser（CEF UI 线程）与 _nativeWindow.Close（主线程）各自 marshal。
    /// </summary>
    public void Close()
    {
        if (IsClosed)
            return;
        CloseBrowser(); // 适配器内部 marshal 到 CEF UI 线程
        RunOnUiThread(_nativeWindow.Close);
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
    /// 把动作 marshal 到原生 UI 线程（主线程）同步执行（Win32 窗口 API 要求主线程）。
    /// </summary>
    /// <param name="action">要执行的动作。</param>
    private void RunOnUiThread(Action action)
        => WebWindowPlatform.Current.RunOnUiThread(action);

    /// <summary>
    /// 向页面 JS 发送一条消息：protobuf 字节转 NUL 转义串后嵌进 <c>window.wwuiReceive("...")</c> 注入。
    /// 属性变更可发生在任意线程（如 System.Threading.Timer 回调），执行 marshal 交给适配器（CEF UI 线程）。
    /// 窗口已关闭时静默忽略。
    /// </summary>
    /// <param name="message">protobuf 字节。</param>
    public void PostMessage(byte[] message)
    {
        try
        {
            if (IsClosed)
                return;
            var js = "window.wwuiReceive(" + JsStringLiteral.Quote(WebView2StringCodec.Encode(message)) + ")";
            ExecuteJavaScript(js, string.Empty, 0); // 适配器内部 marshal 到 CEF UI 线程
        }
        catch
        {
            // 窗口关闭后浏览器已销毁，忽略（定时器仍在后台推送）
        }
    }

    /// <summary>
    /// 在页面里执行一段 JavaScript 并返回结果（与 WebView2/Linux 对齐；best-effort）。
    /// CEF 的 ExecuteJavaScript 无结果回调，返回值固定为空串；执行 marshal 交给适配器（CEF UI 线程）。
    /// </summary>
    /// <param name="script">要执行的 JS 脚本。</param>
    /// <returns>执行结果（JSON 编码字符串；CEF 下固定空串）。</returns>
    public Task<string> ExecuteScriptAsync(string script)
    {
        // 与 Windows 的 InvalidOperationException("WebView2 尚未初始化完成。") 对齐：窗口已关闭时明确报错
        if (IsClosed)
            throw new InvalidOperationException("窗口已关闭。");
        ExecuteJavaScript(script, string.Empty, 0); // 适配器内部 marshal 到 CEF UI 线程
        return Task.FromResult("");
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
    /// scheme 处理器在 IO 线程收到 JS 回传、marshal 到 CEF UI 线程后调用本方法。回调在 CEF UI 线程。
    /// </summary>
    /// <param name="payload">protobuf 字节。</param>
    internal void OnMessageFromWeb(byte[] payload) => MessageReceived?.Invoke(payload);

    /// <summary>
    /// 基类 LoadEnd（on_load_end 主 frame）→ 主页面导航完成。
    /// </summary>
    private void OnLoadEnd() => NavigationCompleted?.Invoke();

    /// <summary>
    /// 基类 BrowserInitialized（on_after_created，CEF UI 线程回调）：注册 scheme 映射，随后自动打开 DevTools。
    /// DevTools 打开 marshal 到 CEF UI 线程执行（ShowDevTools 要求）。
    /// </summary>
    private void OnBrowserInitialized()
    {
        if (UnderlyingBrowser is { } browser)
            CefPlatform.RegisterBrowser(browser, this); // scheme 处理器按浏览器 id 分派回本窗口

        Task.Run(() =>
        {
            Thread.Sleep(1000);
            CefPlatform.RunOnCefUiThread(ShowDeveloperTools);
        });
    }

    /// <summary>
    /// 基类 BrowserClosed（on_before_close，CEF UI 线程回调）：摘除映射，销毁宿主顶层窗口 marshal 回主线程
    /// （→ WM_DESTROY → 末窗 PostQuitMessage）。
    /// </summary>
    /// <param name="browser">正在关闭的浏览器。</param>
    private void OnBrowserClosed(CefBrowser browser)
    {
        CefPlatform.UnregisterBrowser(browser);
        RunOnUiThread(() =>
        {
            if (!IsClosed)
                _nativeWindow.Close(); // CEF 子窗口已销毁，顶层窗口随浏览器一起消失
            Closed?.Invoke();
        });
    }
}

/// <summary>
/// Win32 宿主控件（<see cref="IControl"/> 实现）：把 Win32 顶层窗口适配成浏览器子窗口挂载点 + 尺寸通知。
/// 浏览器直接作为顶层窗口的子控件（本平台每窗口一个 CEF 显示，最简单直接）。
/// </summary>
internal sealed class Win32Control : IControl
{
    /// <summary>
    /// 被适配的 Win32 顶层窗口。
    /// </summary>
    private readonly INativeWindow _nativeWindow;

    /// <summary>
    /// 订阅窗口尺寸变化，转发为控件尺寸通知（首个尺寸触发浏览器创建）。
    /// </summary>
    /// <param name="nativeWindow">宿主 Win32 窗口。</param>
    public Win32Control(INativeWindow nativeWindow)
    {
        _nativeWindow = nativeWindow;
        nativeWindow.Resize += () =>
        {
            var rc = nativeWindow.GetSize();
            SizeChanged?.Invoke(new CefSize(rc.Width, rc.Height));
        };
    }

    /// <summary>
    /// 控件尺寸变化（首个尺寸触发浏览器创建）。
    /// </summary>
    public event Action<CefSize>? SizeChanged;

    /// <summary>
    /// 浏览器子窗口的宿主视图句柄。
    /// </summary>
    /// <param name="initialWidth">初始宽度。</param>
    /// <param name="initialHeight">初始高度。</param>
    /// <returns>宿主窗口句柄。</returns>
    public IntPtr? GetHostViewHandle(int initialWidth, int initialHeight) => _nativeWindow.WindowHandle;

    /// <summary>
    /// 渲染挂载通知（浏览器直接子挂载，无需额外处理）。
    /// </summary>
    /// <param name="browserHandle">浏览器窗口句柄。</param>
    public void InitializeRender(IntPtr browserHandle) { }

    /// <summary>
    /// 渲染销毁通知（浏览器窗口随宿主窗口销毁）。
    /// </summary>
    public void DestroyRender() { }
}
