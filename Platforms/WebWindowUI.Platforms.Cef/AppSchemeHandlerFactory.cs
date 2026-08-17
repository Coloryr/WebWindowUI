using System.Text;
using WebWindowUI.Core;
using WebWindowUI.Core.Protocol;
using Xilium.CefGlue;

namespace WebWindowUI.Platforms.Cef;

/// <summary>
/// app:// / appdata:// scheme 处理器工厂（CefGlue）：GET 服务 wwwroot 资源，POST __wwui 投递 JS 回传消息。
/// </summary>
internal sealed class AppSchemeHandlerFactory : CefSchemeHandlerFactory
{
    /// <summary>
    /// 为请求创建资源处理器。
    /// </summary>
    /// <param name="browser">浏览器。</param>
    /// <param name="frame">帧。</param>
    /// <param name="schemeName">scheme 名。</param>
    /// <param name="request">请求。</param>
    /// <returns>资源处理器。</returns>
    protected override CefResourceHandler Create(CefBrowser browser, CefFrame frame, string schemeName, CefRequest request)
        => new AppResourceHandler(browser?.Identifier ?? 0, request);
}

/// <summary>
/// app:// 资源处理器：GET 读 wwwroot 资源；POST（__wwui）解码 JS 回传字节并投递对应窗口。
/// </summary>
internal sealed class AppResourceHandler : CefResourceHandler
{
    /// <summary>
    /// 请求浏览器 ID（scheme 处理器按此分派回窗口）。
    /// </summary>
    private readonly long _browserId;

    /// <summary>
    /// 响应体字节。
    /// </summary>
    private byte[]? _data;

    /// <summary>
    /// 响应 MIME（不含 charset）。
    /// </summary>
    private string _mime = "text/plain";

    /// <summary>
    /// 响应状态码。
    /// </summary>
    private int _status = 404;

    /// <summary>
    /// Cache-Control；null 不设。
    /// </summary>
    private string? _cacheControl;

    /// <summary>
    /// 已读偏移。
    /// </summary>
    private int _offset;

    /// <summary>
    /// 构造并同步处理请求（GET 解析资源；POST 解码消息）。
    /// </summary>
    /// <param name="browserId">请求浏览器 ID。</param>
    /// <param name="request">请求。</param>
    public AppResourceHandler(long browserId, CefRequest request)
    {
        _browserId = browserId;

        if (request.Method == "POST")
        {
            HandlePost(request);
            return;
        }

        if (WebWindowResource.TryResolvePath(request.Url, out var relative, out var mimeType) is { } stream)
        {
            using (stream)
            {
                using var ms = new MemoryStream();
                stream.CopyTo(ms);
                _data = ms.ToArray();
            }
            // CEF 不识别带 charset 的 MIME（按纯文本显示源码），`;` 剥离。勿设 CefResponse.Charset。
            _mime = mimeType!.Split(';', 2)[0].Trim();
            _status = 200;
            _cacheControl = WebWindowResource.CacheControl(relative!);
        }
        else
        {
            _data = "404 Not Found"u8.ToArray();
            _status = 404;
            _cacheControl = "no-store";
        }
    }

    /// <summary>
    /// 处理 POST：读 body 字节、解码 NUL 转义串还原 protobuf、按浏览器分派回窗口。
    /// </summary>
    /// <param name="request">请求。</param>
    private void HandlePost(CefRequest request)
    {
        _data = Array.Empty<byte>();
        _status = 204;
        _cacheControl = "no-store";

        byte[] payload = [];
        try
        {
            if (request.PostData is { } postData)
            {
                var merged = new List<byte>();
                foreach (var element in postData.GetElements())
                {
                    if (element.GetBytes() is { } bytes)
                        merged.AddRange(bytes);
                }
                if (merged.Count > 0)
                {
                    // fetch 把 JS 字符串按 UTF-8 编码成 body 字节，先解码回 NUL 转义串，再还原成 protobuf 字节
                    var escaped = Encoding.UTF8.GetString([.. merged]);
                    payload = StringCodec.Decode(escaped);
                }
            }
        }
        catch
        {
            // 单个请求失败回退 204，不影响其他请求
        }

        if (payload.Length > 0 && CefPlatform.TryGetWindow(_browserId, out var window))
        {
            window.OnMessageFromWeb(payload);
        }
    }

    /// <summary>
    /// 同步打开：数据已就绪，handleRequest=true 直接走响应头/读流程。
    /// </summary>
    /// <param name="request">请求。</param>
    /// <param name="handleRequest">已同步处理。</param>
    /// <param name="callback">异步回调（同步完成无需使用）。</param>
    /// <returns>是否开始处理。</returns>
    protected override bool Open(CefRequest request, out bool handleRequest, CefCallback callback)
    {
        handleRequest = true;
        return true;
    }

    /// <summary>
    /// 填响应状态、MIME 与响应头。
    /// </summary>
    /// <param name="response">响应对象。</param>
    /// <param name="responseLength">响应体长度。</param>
    /// <param name="redirectUrl">无重定向，置空。</param>
    protected override void GetResponseHeaders(CefResponse response, out long responseLength, out string redirectUrl)
    {
        responseLength = _data?.LongLength ?? 0;
        redirectUrl = string.Empty;
        response.Status = _status;
        response.StatusText = _status == 200 ? "OK" : (_status == 404 ? "Not Found" : "No Content");
        response.MimeType = _mime;
        response.SetHeaderByName("Access-Control-Allow-Origin", "*", overwrite: true);
        if (_cacheControl is { } cache)
        {
            response.SetHeaderByName("Cache-Control", cache, overwrite: true);
        }
    }

    /// <summary>
    /// 输出下一段响应体；写完返回 true（CEF 继续调 Read），无数据（bytesRead=0）返回 false 表示完成。
    /// 注意：返回 true 表示「成功写入」，与是否有剩余数据无关；返回 false 且 bytesRead=0 才表示响应完成。
    /// </summary>
    /// <param name="response">输出流。</param>
    /// <param name="bytesToRead">本次最大读取字节数。</param>
    /// <param name="bytesRead">本次写入字节数。</param>
    /// <param name="callback">异步回调（同步完成无需使用，释放防泄漏）。</param>
    /// <returns>是否继续读取。</returns>
    protected override bool Read(Stream response, int bytesToRead, out int bytesRead, CefResourceReadCallback callback)
    {
        callback?.Dispose();
        if (_data is null || _offset >= _data.Length)
        {
            bytesRead = 0;
            return false;
        }
        bytesRead = Math.Min(bytesToRead, _data.Length - _offset);
        response.Write(_data, _offset, bytesRead);
        _offset += bytesRead;
        return true;
    }

    /// <summary>
    /// 跳过响应体；跳到末尾返回 false。
    /// </summary>
    /// <param name="bytesToSkip">要跳过的字节数。</param>
    /// <param name="bytesSkipped">实际跳过字节数。</param>
    /// <param name="callback">异步回调（同步完成无需使用）。</param>
    /// <returns>是否还有数据。</returns>
    protected override bool Skip(long bytesToSkip, out long bytesSkipped, CefResourceSkipCallback callback)
    {
        var remaining = (_data?.Length ?? 0) - _offset;
        var actual = Math.Min(bytesToSkip, remaining);
        _offset += (int)actual;
        bytesSkipped = actual;
        return _offset < (_data?.Length ?? 0);
    }

    /// <summary>
    /// 请求被取消：字节数组可 GC。
    /// </summary>
    protected override void Cancel()
    {
    }
}
