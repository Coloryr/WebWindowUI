using Xilium.CefGlue;

namespace WebWindowUI.Platforms.Cef;

/// <summary>
/// CEF 浏览器基类（镜像上游 Xilium.CefGlue.Common.BaseCefBrowser 的 API 形状，按本平台裁剪）：
/// 浏览器生命周期由 <see cref="CommonBrowserAdapter"/> 承担，本类只暴露高层事件与操作；
/// 宿主控件由子类提供（本平台每个窗口只有一个 CEF 显示，控件即窗口本身）。
/// </summary>
public abstract class BaseCefBrowser
{
    /// <summary>
    /// 浏览器生命周期与事件引擎。
    /// </summary>
    private readonly CommonBrowserAdapter _adapter;

    /// <summary>
    /// 是否已关闭（CloseBrowser 调用后置位）。
    /// </summary>
    private bool _closed;

    /// <summary>
    /// 浏览器初始化完成（on_after_created，仅主浏览器触发一次）。
    /// </summary>
    public event Action? BrowserInitialized;

    /// <summary>
    /// 主 frame 加载完成。
    /// </summary>
    public event Action? LoadEnd;

    /// <summary>
    /// 主浏览器关闭（on_before_close）。
    /// </summary>
    public event Action<CefBrowser>? BrowserClosed;

    /// <summary>
    /// 绑定浏览器事件引擎（浏览器延后到 CreateBrowser 时创建）。宿主控件为 internal 类型，故构造 internal。
    /// </summary>
    /// <param name="control">宿主控件（浏览器子窗口挂载点）。</param>
    internal BaseCefBrowser(IControl control)
    {
        _adapter = new CommonBrowserAdapter(this, control);
        _adapter.Initialized += () => BrowserInitialized?.Invoke();
        _adapter.LoadEnd += (_, _) => LoadEnd?.Invoke();
        _adapter.BrowserClosed += b => BrowserClosed?.Invoke(b);
    }

    /// <summary>
    /// 底层主浏览器；null 表示尚未创建或已关闭。
    /// </summary>
    protected CefBrowser? UnderlyingBrowser => _adapter.Browser;

    /// <summary>
    /// 是否已关闭（CloseBrowser 已调用）。
    /// </summary>
    protected bool IsClosed => _closed;

    /// <summary>
    /// 底层浏览器是否已初始化（on_after_created 已触发）。
    /// </summary>
    public bool IsBrowserInitialized => _adapter.IsInitialized;

    /// <summary>
    /// 起始/当前 URL（构造期设置即起始导航地址；浏览器已建后设置须在 CEF UI 线程）。
    /// </summary>
    public string Address
    {
        get => _adapter.Address;
        set => _adapter.Address = value;
    }

    /// <summary>
    /// 创建浏览器（CEF 子窗口铺满宿主客户区）。已创建则忽略。
    /// </summary>
    /// <param name="width">客户区宽度。</param>
    /// <param name="height">客户区高度。</param>
    protected void CreateBrowser(int width, int height) => _adapter.CreateBrowser(width, height);

    /// <summary>
    /// 在浏览器主 frame 里执行一段 JS。浏览器未就绪时静默忽略。
    /// </summary>
    /// <param name="code">要执行的 JS 脚本。</param>
    /// <param name="url">脚本所在 URL。</param>
    /// <param name="line">脚本起始行号。</param>
    public void ExecuteJavaScript(string code, string url, int line) => _adapter.ExecuteJavaScript(code, url, line);

    /// <summary>
    /// 打开 DevTools 调试工具（独立弹窗）。
    /// </summary>
    public void ShowDeveloperTools() => _adapter.ShowDeveloperTools();

    /// <summary>
    /// 关闭浏览器（forceClose=false：让 CEF 跑 beforeunload 等再关）。随后 on_before_close → BrowserClosed。
    /// </summary>
    public void CloseBrowser()
    {
        if (_closed)
            return;
        _closed = true;
        _adapter.CloseBrowser(false);
    }
}
