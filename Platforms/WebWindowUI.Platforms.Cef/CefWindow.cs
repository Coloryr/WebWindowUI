using WebWindowUI.Core;
using WebWindowUI.Core.Protocol;
using WebWindowUI.Natives.Windows;
using Xilium.CefGlue;
using Xilium.CefGlue.Common;
using Xilium.CefGlue.Common.Events;
using Xilium.CefGlue.Common.Helpers.Logger;

namespace WebWindowUI.Platforms.Cef;

/// <summary>
/// CEF 平台窗口：直连 CefGlue.Common 内部 CommonBrowserAdapter（IVT 授权；不再经
/// BaseCefBrowser 扩展点——消除 vendored 源码同步负担），浏览器承载于裸 Win32 顶层窗口
/// （Win32CefControl 隐藏宿主 + 重挂载），可创建多个实例。浏览器关闭（仅主浏览器）销毁顶层窗口。
/// </summary>
public sealed class CefWindow : IWindowBackend
{
    /// <summary>
    /// 承载浏览器控件的 Win32 顶层窗口。
    /// </summary>
    private readonly INativeWindow _nativeWindow;

    /// <summary>
    /// 窗口选项。
    /// </summary>
    private readonly WebWindowOptions _options;

    /// <summary>
    /// 平台控件（隐藏宿主 + 重挂载）。
    /// </summary>
    private readonly Win32CefControl _control;

    /// <summary>
    /// CefGlue.Common 浏览器适配器（浏览器创建/导航/JS/事件）。
    /// </summary>
    private readonly CommonBrowserAdapter _adapter;

    /// <summary>
    /// 主浏览器实例（浏览器初始化时记录；BrowserClosed 过滤 DevTools 等弹窗用）。
    /// </summary>
    private CefBrowser? _mainBrowser;

    /// <summary>
    /// 主浏览器 id（scheme 回调分派用）。
    /// </summary>
    private long _browserId;

    /// <summary>
    /// 是否已关闭（Close 调用后置位）。
    /// </summary>
    private bool _closed;

    /// <summary>
    /// 窗口销毁时触发（用户关闭或 Close()）。宿主在此清理与窗口关联的状态。
    /// </summary>
    public event Action? Closed;

    /// <summary>
    /// 页面导航完成时触发（用于在页面就绪后推送 Model 初始快照）。
    /// </summary>
    public event Action? NavigationCompleted;

    /// <summary>
    /// 页面 JS 经 fetch POST（app://__wwui）回传的消息（protobuf 字节，scheme 处理器还原后投递）。
    /// </summary>
    public event Action<byte[]>? MessageReceived;

    /// <summary>
    /// 构造窗口：建 Win32 顶层窗口 + 浏览器适配器（隐藏宿主创建浏览器），设置初始 URL 与尺寸。
    /// </summary>
    /// <param name="options">窗口选项。</param>
    public CefWindow(WebWindowOptions options)
    {
        _options = options;

        // CEF 初始化（首个窗口触发；BaseCefBrowser 原在构造时 Load，现显式调用）。
        if (!CefRuntimeLoader.IsLoaded)
        {
            CefRuntimeLoader.Load();
        }

        _nativeWindow = new Win32NativeWindow(options);
        _nativeWindow.Resize += OnNativeResize;

        _control = new Win32CefControl();
        _adapter = new CommonBrowserAdapter(this, nameof(CefWindow), _control, new NullLogger(nameof(CefWindow)));
        _adapter.Initialized += OnBrowserInitialized;
        _adapter.BrowserClosed += OnBrowserClosed;
        _adapter.LoadEnd += OnLoadEnd;

        // 初始 URL（适配器内部 marshal 到 CEF UI 线程，浏览器初始化后导航）。
        _adapter.Address = WebWindowResource.GetWindowIndexUrl(options.WindowPath);

        // 设置目标窗口与初始尺寸：触发 SizeChanged → 适配器创建浏览器（异步）。
        var size = _nativeWindow.GetSize();
        _control.SetTarget(_nativeWindow.WindowHandle, size.Width, size.Height);
    }

    /// <summary>
    /// 浏览器初始化完成（CEF UI 线程回调）：记录主浏览器并登记 id → 窗口映射。
    /// </summary>
    private void OnBrowserInitialized()
    {
        var browser = _adapter.Browser;
        if (browser != null)
        {
            _mainBrowser = browser;
            _browserId = browser.Identifier;
            CefPlatform.RegisterBrowser(_browserId, this);
        }
    }

    /// <summary>
    /// 浏览器销毁（CEF UI 线程回调）：仅主浏览器销毁顶层窗口并触发 Closed（DevTools 等弹窗忽略）。
    /// </summary>
    /// <param name="browser">已销毁的浏览器。</param>
    private void OnBrowserClosed(CefBrowser browser)
    {
        if (!ReferenceEquals(browser, _mainBrowser))
            return;
        CefPlatform.UnregisterBrowser(_browserId);
        RunOnUiThread(() =>
        {
            _nativeWindow.Close();
            Closed?.Invoke();
        });
    }

    /// <summary>
    /// 加载结束（CEF UI 线程回调）：主帧完成触发导航完成事件。
    /// </summary>
    /// <param name="sender">事件源。</param>
    /// <param name="e">事件参数。</param>
    private void OnLoadEnd(object sender, LoadEndEventArgs e)
    {
        if (e.Frame.IsMain && !_closed)
        {
            NavigationCompleted?.Invoke();
        }
    }

    /// <summary>
    /// 窗口尺寸变化：同步控件尺寸（铺满浏览器）。
    /// </summary>
    private void OnNativeResize()
    {
        var size = _nativeWindow.GetSize();
        _control.SetSize(size.Width, size.Height);
    }

    /// <summary>
    /// 显示窗口（浏览器控件已挂载，随顶层窗口一起可见）。
    /// </summary>
    public void Show()
    {
        _nativeWindow.Show();
        RunOnUiThread(_control.Reapply);
    }

    /// <summary>
    /// 隐藏窗口（不关闭、不销毁）。
    /// </summary>
    public void Hide()
    {
        RunOnUiThread(_nativeWindow.Hide);
    }

    /// <summary>
    /// 关闭窗口：先关浏览器（BrowserClosed 回调销毁顶层窗口），未创建则直接销毁。
    /// 关闭最后一个窗口后程序自动退出。
    /// </summary>
    public void Close()
    {
        if (_closed)
            return;
        _closed = true;
        if (_adapter.IsInitialized)
        {
            try
            {
                _adapter.CloseBrowser(true);
            }
            catch
            {
                // 浏览器已销毁时忽略
            }
        }
        else
        {
            RunOnUiThread(() =>
            {
                _nativeWindow.Close();
                Closed?.Invoke();
            });
        }
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
    /// 设置窗口图标（标题栏 + 任务栏）。
    /// </summary>
    /// <param name="icon">图标。</param>
    public void SetIcon(WindowIcon icon)
    {
        RunOnUiThread(() => _nativeWindow.SetIcon(icon));
    }

    /// <summary>
    /// 把动作 marshal 到原生 UI 线程（主线程）同步执行。
    /// </summary>
    /// <param name="action">要执行的动作。</param>
    private void RunOnUiThread(Action action)
        => WebWindowPlatform.Current.RunOnUiThread(action);

    /// <summary>
    /// 向页面 JS 发送一条消息：protobuf 字节转 NUL 转义串后嵌进 <c>window.wwuiReceive("...")</c> 注入。
    /// </summary>
    /// <param name="message">protobuf 字节。</param>
    public void PostMessage(byte[] message)
    {
        try
        {
            if (_closed)
                return;
            var js = "window.wwuiReceive(" + JsStringLiteral.Quote(WebView2StringCodec.Encode(message)) + ")";
            CefPlatform.PostToCefUiThread(() => _adapter.ExecuteJavaScript(js, "about:blank", 1));
        }
        catch
        {
            // 窗口关闭后浏览器已销毁，忽略（定时器仍在后台推送）
        }
    }

    /// <summary>
    /// 在页面里执行一段 JavaScript 并返回结果（best-effort；失败回退空串）。
    /// </summary>
    /// <param name="script">要执行的 JS 脚本。</param>
    /// <returns>执行结果（JSON 编码字符串）。</returns>
    public Task<string> ExecuteScriptAsync(string script)
    {
        if (_closed)
            throw new InvalidOperationException("窗口已关闭。");
        try
        {
            return _adapter.EvaluateJavaScript<string>(script, "about:blank", 1);
        }
        catch
        {
            return Task.FromResult(string.Empty);
        }
    }

    /// <summary>
    /// scheme 处理器收到 JS 回传、解码后调用本方法。回调在 CEF IO/UI 线程。
    /// </summary>
    /// <param name="payload">protobuf 字节。</param>
    internal void OnMessageFromWeb(byte[] payload) => MessageReceived?.Invoke(payload);
}
