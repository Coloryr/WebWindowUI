using System.Runtime.InteropServices;
using CefSharp;
using CefSharp.WinForms;
using WebWindowUI.Core;
using WebWindowUI.Core.Protocol;
using WebWindowUI.Natives.Windows;

namespace WebWindowUI.Platforms.Cef;

/// <summary>
/// CEF 平台窗口：ChromiumWebBrowser（CefSharp）承载于裸 Win32 顶层窗口，可创建多个实例。
/// 浏览器控件 Handle 经 SetParent 重挂载进顶层窗口客户区并铺满。
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
    /// CefSharp 浏览器控件。
    /// </summary>
    private readonly ChromiumWebBrowser _browser;

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
    /// 构造窗口：建 Win32 顶层窗口 + CefSharp 浏览器控件，控件重挂载进客户区。
    /// </summary>
    /// <param name="options">窗口选项。</param>
    public CefWindow(WebWindowOptions options)
    {
        _options = options;
        _nativeWindow = new Win32NativeWindow(options);
        _nativeWindow.Resize += OnNativeResize;

        // CefSharp 浏览器控件：初始 URL 为 app:// 页面；控件句柄创建后重挂载进顶层窗口。
        _browser = new ChromiumWebBrowser(WebWindowResource.GetWindowIndexUrl(options.WindowPath))
        {
            Dock = DockStyle.Fill,
        };
        _browser.LoadingStateChanged += OnLoadingStateChanged;
        _browser.IsBrowserInitializedChanged += OnBrowserInitializedChanged;

        // 强制创建控件句柄（WinForms 控件句柄惰性创建），随后 SetParent 进原生窗口。
        var handle = _browser.Handle;
        _ = handle;
    }

    /// <summary>
    /// 浏览器初始化完成（CEF UI 线程回调）：登记浏览器 id → 窗口映射并重挂载控件。
    /// </summary>
    /// <param name="sender">控件。</param>
    /// <param name="args">参数。</param>
    private void OnBrowserInitializedChanged(object? sender, EventArgs args)
    {
        if (_browser.IsBrowserInitialized && _browser.GetBrowser() is { } browser)
        {
            CefPlatform.RegisterBrowser(browser.Identifier, this);
        }
        CefPlatform.RunOnUiThread(ReparentIntoNativeWindow);
    }

    /// <summary>
    /// 重挂载浏览器控件句柄进顶层窗口客户区（UI 线程）。
    /// </summary>
    private void ReparentIntoNativeWindow()
    {
        if (_closed)
            return;
        var hwnd = _browser.Handle;
        if (hwnd == IntPtr.Zero)
            return;
        if (SetParent(hwnd, _nativeWindow.WindowHandle) == IntPtr.Zero)
            return;
        var rc = _nativeWindow.GetSize();
        MoveWindow(hwnd, 0, 0, rc.Width, rc.Height, true);
    }

    /// <summary>
    /// 窗口尺寸变化：铺满浏览器控件。
    /// </summary>
    private void OnNativeResize()
    {
        if (_browser.IsDisposed)
            return;
        var rc = _nativeWindow.GetSize();
        MoveWindow(_browser.Handle, 0, 0, rc.Width, rc.Height, true);
    }

    /// <summary>
    /// 加载状态变化：主帧加载完成触发导航完成事件。
    /// </summary>
    /// <param name="sender">控件。</param>
    /// <param name="args">参数。</param>
    private void OnLoadingStateChanged(object? sender, LoadingStateChangedEventArgs args)
    {
        if (!args.IsLoading && !_closed)
        {
            NavigationCompleted?.Invoke();
        }
    }

    /// <summary>
    /// 显示窗口（浏览器控件已挂载，随顶层窗口一起可见）。
    /// </summary>
    public void Show()
    {
        _nativeWindow.Show();
        CefPlatform.RunOnUiThread(ReparentIntoNativeWindow);
    }

    /// <summary>
    /// 隐藏窗口（不关闭、不销毁）。
    /// </summary>
    public void Hide()
    {
        RunOnUiThread(_nativeWindow.Hide);
    }

    /// <summary>
    /// 关闭窗口：先销毁浏览器再销毁顶层窗口。关闭最后一个窗口后程序自动退出。
    /// </summary>
    public void Close()
    {
        if (_closed)
            return;
        _closed = true;
        try
        {
            _browser.Dispose(); // CefSharp：销毁控件即关闭浏览器
        }
        catch
        {
            // 浏览器已销毁时忽略
        }
        RunOnUiThread(() =>
        {
            _nativeWindow.Close();
            Closed?.Invoke();
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
            _browser.ExecuteScriptAsync(js);
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
            var response = _browser.EvaluateScriptAsync(script).Result;
            return Task.FromResult(response.Success ? response.Result?.ToString() ?? string.Empty : string.Empty);
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

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

    [DllImport("user32.dll")]
    private static extern bool MoveWindow(IntPtr hWnd, int x, int y, int nWidth, int nHeight, bool bRepaint);
}
