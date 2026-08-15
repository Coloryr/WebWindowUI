using Xilium.CefGlue;

namespace CefDemo;

/// <summary>
/// simple_browser_list.c 移植：持有浏览器引用的可增长列表。
/// </summary>
internal sealed class SimpleBrowserList
{
    private readonly List<CefBrowser> _browsers = new();

    /// <summary>
    /// 浏览器数量（browser_list_count）。
    /// </summary>
    public int Count => _browsers.Count;

    /// <summary>
    /// 添加浏览器（browser_list_add，持引用）。
    /// </summary>
    /// <param name="browser">浏览器。</param>
    public void Add(CefBrowser browser) => _browsers.Add(browser);

    /// <summary>
    /// 移除指定实例（browser_list_remove，按 IsSame 匹配）。
    /// </summary>
    /// <param name="browser">浏览器。</param>
    public void Remove(CefBrowser browser) => _browsers.RemoveAll(b => b.IsSame(browser));

    /// <summary>
    /// 取下标浏览器（browser_list_get）。
    /// </summary>
    /// <param name="index">下标。</param>
    /// <returns>浏览器或 null。</returns>
    public CefBrowser? Get(int index) => index < _browsers.Count ? _browsers[index] : null;
}
