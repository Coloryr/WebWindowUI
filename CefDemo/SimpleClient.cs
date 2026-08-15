using Xilium.CefGlue;

namespace CefDemo;

/// <summary>
/// simple_handler.c 移植：CefClient + 三个子处理器 + 浏览器列表 + CloseAllBrowsers。
/// </summary>
internal sealed class SimpleClient : CefClient
{
    /// <summary>
    /// 全局单例（simple_handler_get_instance）。
    /// </summary>
    public static SimpleClient? Instance { get; private set; }

    private readonly SimpleDisplayHandler _displayHandler;
    private readonly SimpleLifeSpanHandler _lifeSpanHandler;
    private readonly SimpleLoadHandler _loadHandler;

    /// <summary>
    /// 是否为 Alloy 风格（仅 Alloy 显示标题/错误页）。
    /// </summary>
    public bool IsAlloyStyle { get; }

    /// <summary>
    /// 现有浏览器列表（simple_browser_list）。
    /// </summary>
    public readonly SimpleBrowserList BrowserList = new();

    /// <summary>
    /// 窗口关闭中标志。
    /// </summary>
    public bool IsClosing;

    private SimpleClient(bool isAlloyStyle)
    {
        IsAlloyStyle = isAlloyStyle;
        _displayHandler = new SimpleDisplayHandler(this);
        _lifeSpanHandler = new SimpleLifeSpanHandler(this);
        _loadHandler = new SimpleLoadHandler(this);
        Instance = this;
    }

    /// <summary>
    /// 创建客户端（simple_handler_create）并设全局单例。
    /// </summary>
    /// <param name="isAlloyStyle">是否 Alloy 风格。</param>
    /// <returns>客户端实例。</returns>
    public static SimpleClient Create(bool isAlloyStyle) => new(isAlloyStyle);

    /// <inheritdoc />
    protected override CefDisplayHandler GetDisplayHandler() => _displayHandler;

    /// <inheritdoc />
    protected override CefLifeSpanHandler GetLifeSpanHandler() => _lifeSpanHandler;

    /// <inheritdoc />
    protected override CefLoadHandler GetLoadHandler() => _loadHandler;

    /// <summary>
    /// 关闭全部浏览器（simple_handler_close_all_browsers）：非 UI 线程则投递 UI 线程任务。
    /// </summary>
    /// <param name="forceClose">是否强制关闭。</param>
    public void CloseAllBrowsers(bool forceClose)
    {
        if (!CefRuntime.CurrentlyOn(CefThreadId.UI))
        {
            CefRuntime.PostTask(CefThreadId.UI, new CloseBrowsersTask(this, forceClose));
            return;
        }

        for (var i = 0; i < BrowserList.Count; i++)
        {
            BrowserList.Get(i)?.GetHost().CloseBrowser(forceClose);
        }
    }

    /// <summary>
    /// UI 线程关窗任务（close_browsers_task_t）。
    /// </summary>
    private sealed class CloseBrowsersTask : CefTask
    {
        private readonly SimpleClient _client;
        private readonly bool _forceClose;

        public CloseBrowsersTask(SimpleClient client, bool forceClose)
        {
            _client = client;
            _forceClose = forceClose;
        }

        /// <inheritdoc />
        protected override void Execute() => _client.CloseAllBrowsers(_forceClose);
    }
}
