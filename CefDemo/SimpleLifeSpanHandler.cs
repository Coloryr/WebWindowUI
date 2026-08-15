using Xilium.CefGlue;

namespace CefDemo;

/// <summary>
/// simple_life_span_handler.c 移植。
/// </summary>
internal sealed class SimpleLifeSpanHandler : CefLifeSpanHandler
{
    private readonly SimpleClient _parent;

    public SimpleLifeSpanHandler(SimpleClient parent) => _parent = parent;

    /// <summary>
    /// 浏览器创建后加入列表（on_after_created）。
    /// </summary>
    /// <param name="browser">浏览器。</param>
    protected override void OnAfterCreated(CefBrowser browser)
        => _parent.BrowserList.Add(browser);

    /// <summary>
    /// 关闭主窗口特殊处理：唯一浏览器时标记允许关闭，返回 false 允许关闭（do_close）。
    /// </summary>
    /// <param name="browser">浏览器。</param>
    /// <returns>是否取消关闭。</returns>
    protected override bool DoClose(CefBrowser browser)
    {
        if (_parent.BrowserList.Count == 1)
            _parent.IsClosing = true;
        return false;
    }

    /// <summary>
    /// 浏览器销毁前移出列表；全部关闭则退出消息循环（on_before_close）。
    /// </summary>
    /// <param name="browser">浏览器。</param>
    protected override void OnBeforeClose(CefBrowser browser)
    {
        _parent.BrowserList.Remove(browser);

        if (_parent.BrowserList.Count == 0)
            CefRuntime.QuitMessageLoop();
    }
}
