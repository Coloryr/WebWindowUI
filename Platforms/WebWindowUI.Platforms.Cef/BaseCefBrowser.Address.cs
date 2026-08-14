namespace Xilium.CefGlue.Common;

/// <summary>
/// BaseCefBrowser 的 Address partial 实现（Win32 平台）：直接转发到适配器。
/// CefGlue.Common 排除 BaseCefBrowser.cs，平台工程链接后需提供本 partial（镜像 CefGlue.WPF 的做法）。
/// </summary>
partial class BaseCefBrowser
{
    /// <summary>
    /// 当前/起始 URL（浏览器未建时暂存，建后立即导航）。
    /// </summary>
    public partial string Address
    {
        get => _adapter.Address;
        set => _adapter.Address = value;
    }
}
