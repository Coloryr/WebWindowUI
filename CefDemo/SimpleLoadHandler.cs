using System;
using System.Text;
using Xilium.CefGlue;

namespace CefDemo;

/// <summary>
/// simple_load_handler.c 移植：Alloy 风格且非 ERR_ABORTED 时用 data: URI 显示错误页。
/// </summary>
internal sealed class SimpleLoadHandler : CefLoadHandler
{
    private readonly SimpleClient _parent;

    public SimpleLoadHandler(SimpleClient parent) => _parent = parent;

    /// <summary>
    /// 加载错误：Alloy 风格显示错误页，否则交给 Chrome（load_handler_on_load_error）。
    /// </summary>
    /// <param name="browser">浏览器。</param>
    /// <param name="frame">帧。</param>
    /// <param name="errorCode">错误码。</param>
    /// <param name="errorText">错误文本。</param>
    /// <param name="failedUrl">失败 URL。</param>
    protected override void OnLoadError(CefBrowser browser, CefFrame frame, CefErrorCode errorCode, string errorText, string failedUrl)
    {
        if (!_parent.IsAlloyStyle || errorCode == CefErrorCode.Aborted)
            return;

        var errorHtml = $"<html><body bgcolor=\"white\"><h2>Failed to load URL with error {(int)errorCode}.</h2></body></html>";
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(errorHtml));
        frame.LoadUrl($"data:text/html;base64,{encoded}");
    }
}
