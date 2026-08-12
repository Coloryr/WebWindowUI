using WebWindowUI.Core;

namespace WebWindowUI.Platforms.Linux;

/// <summary>
/// WebKit2 信号 → 托管事件的桥。用 g_signal_connect_data 把两个信号（load-changed、
/// script-message-received::wwui）接到 Cdecl 静态 trampoline（保活），经单个 GCHandle
/// 路由回本实例的事件。全部在主循环线程触发，trampoline 内不做重入。
///
/// 生命周期约定：
///  - 连接前先注册 signal（docs 建议先连信号再 register_script_message_handler，避免漏消息），
///   调用方在 <see cref="Connect"/> 之后才注册 handler；
///  - <see cref="Dispose"/> 断开两个信号并释放 GCHandle；WebKit 对象销毁后 GObject 会自行清理
///   闭包，trampoline 对已释放的 GCHandle 只会吞掉异常（理论上销毁后不再有信号）。
/// </summary>
internal sealed class WebKit2SignalBridge
{
    private const string ScriptMessageSignal = "script-message-received::" + LinuxWindow.BridgeHandlerName;

    // 保活：native 只持有函数指针，委托实例必须被静态字段强引用。
    private static readonly WebKit2Native.SignalLoadChangedCallback _loadChangedTrampoline = OnLoadChanged;
    private static readonly WebKit2Native.SignalScriptMessageReceivedCallback _scriptMessageTrampoline = OnScriptMessageReceived;

    private readonly IntPtr _webView;
    private readonly IntPtr _userContentManager;
    private GCHandle _handle;
    private ulong _loadChangedId;
    private ulong _scriptMessageId;

    /// <summary>页面加载进度变化（参数为 <see cref="WebKit2Native.LoadEvent"/> 的值）。</summary>
    public event Action<int>? LoadChanged;

    /// <summary>页面 JS 经 window.webkit.messageHandlers.wwui.postMessage 回传的字符串。</summary>
    public event Action<string>? ScriptMessageReceived;

    public WebKit2SignalBridge(IntPtr webView)
    {
        _webView = webView;
        _userContentManager = WebKit2Native.GetUserContentManager(webView);
        _handle = GCHandle.Alloc(this);
    }

    /// <summary>连接两个信号（须在注册 script message handler 之前调用）。</summary>
    public void Connect()
    {
        _loadChangedId = WebKit2Native.ConnectSignal(_webView, "load-changed", _loadChangedTrampoline, _handle);
        _scriptMessageId = WebKit2Native.ConnectSignal(_userContentManager, ScriptMessageSignal, _scriptMessageTrampoline, _handle);
        WebWindowLog.Debug($"connect signals: load-changed={_loadChangedId} script-message={_scriptMessageId}");
    }

    /// <summary>断开信号并释放路由 GCHandle。</summary>
    public void Dispose()
    {
        WebKit2Native.DisconnectSignal(_webView, _loadChangedId);
        WebKit2Native.DisconnectSignal(_userContentManager, _scriptMessageId);
        _loadChangedId = 0;
        _scriptMessageId = 0;
        if (_handle.IsAllocated)
            _handle.Free();
    }

    private static void OnLoadChanged(IntPtr view, int loadEvent, IntPtr userData)
    {
        try
        {
            (GCHandle.FromIntPtr(userData).Target as WebKit2SignalBridge)?.LoadChanged?.Invoke(loadEvent);
        }
        catch
        {
            // 窗口已销毁 / GCHandle 已释放等，忽略
        }
    }

    private static void OnScriptMessageReceived(IntPtr manager, IntPtr jsResult, IntPtr userData)
    {
        try
        {
            var bridge = GCHandle.FromIntPtr(userData).Target as WebKit2SignalBridge;
            if (bridge is null || jsResult == IntPtr.Zero)
                return;
            var message = WebKit2Native.JavascriptResultToString(jsResult);
            WebWindowLog.Debug($"script-message received ({message.Length} chars)");
            bridge.ScriptMessageReceived?.Invoke(message);
        }
        catch (Exception ex)
        {
            // 窗口已销毁 / GCHandle 已释放等，忽略（Debug 记录便于排查）
            WebWindowLog.Debug($"script-message handler error: {ex}");
        }
    }
}
