using WebWindowUI.Core;
using WebWindowUI.Core.Protocol;
using WebWindowUI.Natives.Windows;
using Xilium.CefGlue;
using Xilium.CefGlue.Common;
using Xilium.CefGlue.Common.Platform;

namespace WebWindowUI.Platforms.Cef;

/// <summary>
/// CEF 平台窗口：承载 Chromium 的裸 Win32 顶层窗口（CEF 子浏览器窗口为子控件），可创建多个实例。
/// 镜像 <c>WindowsWindow</c>，渲染内核换 CEF。**浏览器托管层直接用 CefGlue.Common 的
/// <see cref="BaseCefBrowser"/>**（不再自己镜像 CommonBrowserAdapter/CommonCefClient），宿主控件
/// 由 <see cref="Win32CefControl"/> 适配裸 Win32 窗口。
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
    /// 宿主控件（浏览器挂载点）；基类 ctor 经 CreateControl 创建，Show 时触发尺寸创建浏览器。
    /// </summary>
    private Win32CefControl? _control;

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
    /// 页面 JS 经 fetch POST（appdata://__wwui）回传的消息（protobuf 字节，scheme 处理器还原后投递）。
    /// </summary>
    public event Action<byte[]>? MessageReceived;

    /// <summary>
    /// 构造窗口：建 Win32 顶层窗口并交给基类（CefGlue.Common.BaseCefBrowser），订阅浏览器生命周期事件。
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
        : base()
    {
        _options = options;
        _nativeWindow = nativeWindow;
        _control!.Attach(nativeWindow); // 基类 ctor 已经 CreateControl 建空壳，这里绑定原生窗口

        Task.Run(() =>
        {
            Thread.Sleep(10000);
            RunOnUiThread(() =>
            {
                Address = "chrome://gpu"; // 临时：验证 DevTools 是否因自定义 scheme 崩
            });
        });

        BrowserInitialized += OnBrowserInitialized;
        BrowserClosed += OnBrowserClosed;
        LoadEnd += (_, _) => NavigationCompleted?.Invoke();
    }

    /// <summary>
    /// 创建窗口模式宿主控件（CefGlue.Common 要求；基类 ctor 调用，派生字段未初始化，
    /// 故此处建空壳、派生 ctor 里 Attach 原生窗口）。
    /// </summary>
    /// <returns>宿主控件。</returns>
    internal override IControl CreateControl()
    {
        _control = new Win32CefControl();
        return _control;
    }

    /// <summary>
    /// OSR 宿主（本平台窗口模式，不支持）。
    /// </summary>
    /// <returns>抛 NotSupportedException。</returns>
    internal override IOffScreenControlHost CreateOffScreenControlHost() => throw new NotSupportedException("OSR 不支持");

    /// <summary>
    /// OSR 弹窗宿主（本平台窗口模式，不支持）。
    /// </summary>
    /// <returns>抛 NotSupportedException。</returns>
    internal override IOffScreenPopupHost CreatePopupHost() => throw new NotSupportedException("OSR 不支持");

    /// <summary>
    /// OSR 键盘处理器（本平台窗口模式，不支持）。
    /// </summary>
    /// <param name="control">控件对象。</param>
    /// <returns>抛 NotSupportedException。</returns>
    public override IOffScreenKeyboardHandler CreateOffScreenKeyboardHandler(object control) => throw new NotSupportedException("OSR 不支持");

    /// <summary>
    /// 显示窗口并创建 CEF 浏览器（CEF 子窗口铺满客户区）。无头模式只建浏览器、窗口永不显示。
    /// </summary>
    public void Show()
    {
        _nativeWindow.Show();
        _control?.NotifySize(); // 触发尺寸 → CefGlue.Common 首次尺寸创建浏览器
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
    /// 走 CEF 正常关闭（CloseBrowser → on_before_close → BrowserClosed → 销毁顶层窗口）。
    /// </summary>
    public void Close()
    {
        if (_closed)
            return;
        _closed = true;
        CloseBrowser(); // 基类（CefGlue.Common）新增：marshal 到 CEF UI 线程
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
        RunOnUiThread(() => _nativeWindow.SetIcon(icon));
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
            if (_closed)
                return;
            var js = "window.wwuiReceive(" + JsStringLiteral.Quote(WebView2StringCodec.Encode(message)) + ")";
            ExecuteJavaScript(js, string.Empty, 0); // 基类 marshal 到 CEF UI 线程
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
        if (_closed)
            throw new InvalidOperationException("窗口已关闭。");
        ExecuteJavaScript(script, string.Empty, 0); // 基类 marshal 到 CEF UI 线程
        return Task.FromResult("");
    }

    /// <summary>
    /// scheme 处理器在 IO 线程收到 JS 回传、marshal 到 CEF UI 线程后调用本方法。回调在 CEF UI 线程。
    /// </summary>
    /// <param name="payload">protobuf 字节。</param>
    internal void OnMessageFromWeb(byte[] payload) => MessageReceived?.Invoke(payload);

    /// <summary>
    /// 基类 BrowserInitialized（on_after_created，CEF UI 线程回调）：注册 scheme 映射（DevTools 自动打开见下）。
    /// </summary>
    private void OnBrowserInitialized()
    {
        if (UnderlyingBrowser is { } browser)
            CefPlatform.RegisterBrowser(browser, this); // scheme 处理器按浏览器 id 分派回本窗口
    }

    /// <summary>
    /// 基类 BrowserClosed（on_before_close，CEF UI 线程回调）：仅主浏览器关闭时摘除映射并销毁宿主顶层窗口
    /// （→ WM_DESTROY → 末窗 PostQuitMessage）。DevTools 等附加浏览器关闭不影响主窗口。
    /// </summary>
    /// <param name="browser">正在关闭的浏览器。</param>
    private void OnBrowserClosed(CefBrowser browser)
    {
        if (!ReferenceEquals(browser, UnderlyingBrowser))
            return; // DevTools 等附加浏览器关闭，不影响主窗口
        CefPlatform.UnregisterBrowser(browser);
        RunOnUiThread(() =>
        {
            if (!_closed)
                _nativeWindow.Close(); // CEF 子窗口已销毁，顶层窗口随浏览器一起消失
            Closed?.Invoke();
        });
    }
}
