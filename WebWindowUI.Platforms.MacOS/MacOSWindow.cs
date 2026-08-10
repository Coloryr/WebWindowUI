#if MACOS
namespace WebWindowUI.MacOS;

/// <summary>
/// macOS 平台：NSWindow + WKWebView 的窗口，可创建多个实例。
/// 每个 WKWebView 用自己的 WKWebViewConfiguration，自定义 scheme（WKURLSchemeHandler）与
/// script message handler 都按窗口独立注册——不像 Linux 共享默认 WebContext 那样需要进程级注册。
///
/// 平台限制（与 Windows 有差异，README 也注明）：
///  - SetIcon 无操作：macOS 窗口没有独立的 per-window 图标（图标属于 App Bundle）。
///  - 盲写实现：net10.0-macos 在 Windows 上无法编译，绑定名与签名严格对齐已验证的 .NET macOS API，
///    首次在 Mac 上编译时可能仍需微调。
/// </summary>
public sealed class MacOSWindow : IWindowBackend
{
    private const string BridgeHandlerName = "wwui"; // 与前端桥 webwindowui-bridge 的 HANDLER_NAME 一致

    private readonly NSWindow _window;
    private readonly WKWebView _webView;
    private readonly WebWindowOptions _options;
    private bool _closed;

    // 强引用保留 delegate/handler 对象——.NET 绑定不 retain ObjC 委托，被 GC 会静默失效
    private readonly MacWindowDelegate _windowDelegate;
    private readonly MacNavigationDelegate _navigationDelegate;
    private readonly MacScriptMessageHandler _scriptMessageHandler;
    private readonly MacSchemeHandler _schemeHandler;

    /// <summary>窗口销毁时触发（用户关闭或 Close()）。宿主在此清理与窗口关联的状态。</summary>
    public event Action? Closed;

    private MacOSWindow(NSWindow window, WebWindowOptions options)
    {
        _options = options;
        _window = window;

        // 窗口关闭（windowWillClose:）→ 通知框架关闭
        _windowDelegate = new MacWindowDelegate(OnWindowWillClose);
        window.Delegate = _windowDelegate;

        // 自定义 scheme（app:// / appbin://）按窗口独立注册，回调里经 owner 分派
        _schemeHandler = new MacSchemeHandler(this);
        var config = new WKWebViewConfiguration();
        config.SetUrlSchemeHandler(_schemeHandler, options.Scheme);
        if (!string.IsNullOrEmpty(options.DataScheme) && options.DataScheme != options.Scheme)
            config.SetUrlSchemeHandler(_schemeHandler, options.DataScheme);

        // JS → native 通道：页面经 window.webkit.messageHandlers.wwui.postMessage(...) 回传 NUL 转义串
        _scriptMessageHandler = new MacScriptMessageHandler(bytes => MessageReceived?.Invoke(bytes));
        config.UserContentController.AddScriptMessageHandler(_scriptMessageHandler, BridgeHandlerName);

        _webView = new WKWebView(CGRect.Empty, config)
        {
            // 交给窗口后按内容尺寸布局，窗口缩放时跟随
            AutoresizingMask = NSViewResizingMask.WidthSizable | NSViewResizingMask.HeightSizable,
        };
        window.ContentView = _webView;

        // 导航完成 → 页面就绪，推初始快照
        _navigationDelegate = new MacNavigationDelegate(() => NavigationCompleted?.Invoke());
        _webView.NavigationDelegate = _navigationDelegate;
    }

    /// <summary>创建并注册一个尚未显示的窗口。</summary>
    public static MacOSWindow Create(string title, WebWindowOptions options, int width, int height)
    {
        var window = new NSWindow(
            new CGRect(0, 0, width, height), // content rect
            NSWindowStyle.Titled | NSWindowStyle.Closable | NSWindowStyle.Resizable | NSWindowStyle.Miniaturizable,
            NSBackingStore.Buffered,
            false) // defer: 立即创建原生窗口
        {
            Title = title,
            ReleasedWhenClosed = false, // 否则 Close() 后 NSObject 可能被过度释放
        };
        return new MacOSWindow(window, options);
    }

    /// <summary>显示窗口并加载首页。Cocoa 只允许主线程访问，非主线程调用时 marshal 回主线程。
    /// 无头模式下跳过 MakeKeyAndOrderFront/MakeFirstResponder（窗口不出现在屏幕/Dock），但照常加载首页。</summary>
    public void Show()
    {
        RunOnMainThread(() =>
        {
            if (!_options.Headless)
            {
                _window.MakeKeyAndOrderFront(null);
                _window.MakeFirstResponder(_webView);
            }
            _webView.LoadRequest(NSUrlRequest.FromUrl(NSUrl.FromString(_options.HomeUrl)));
        });
    }

    /// <summary>隐藏窗口（不关闭、不销毁）。</summary>
    public void Hide() => RunOnMainThread(() => _window.OrderOut(null));

    /// <summary>关闭窗口。windowWillClose: → 通知框架关闭。</summary>
    public void Close()
    {
        RunOnMainThread(() =>
        {
            if (_closed)
                return;
            _window.Close();
        });
    }

    /// <summary>把窗口带到前台并聚焦。进程本身不带 bundle 时无法跨 App 置前，仅激活本窗口。</summary>
    public void Activate()
    {
        RunOnMainThread(() =>
        {
            _window.MakeKeyAndOrderFront(null);
            _window.MakeFirstResponder(_webView);
        });
    }

    /// <summary>修改窗口标题（立即同步到标题栏）。</summary>
    public void SetTitle(string title) => RunOnMainThread(() => _window.Title = title);

    /// <summary>设置窗口图标。macOS 窗口无 per-window 图标（图标属于 App Bundle），无操作。</summary>
    public void SetIcon(WindowIcon icon)
    {
        // 平台限制，文档注明
    }

    /// <summary>
    /// 向页面 JS 发送一条消息。protobuf 字节经 <see cref="WebView2StringCodec"/> 转成不含 NUL 的
    /// Latin-1 字符串，再用 <see cref="JsStringLiteral.Quote"/> 嵌进 <c>window.wwuiReceive("...")</c>
    /// 经 evaluateJavaScript 注入。JS 端 wwuiReceive 还原后 protobufjs 解码。
    /// 与 Windows 一致：WKWebView 只能主线程访问，属性变更可能发生在任意线程，先投递回主线程。
    /// </summary>
    public void PostMessage(byte[] message)
    {
        try
        {
            if (Environment.CurrentManagedThreadId != MacOSMessageLoopSynchronizationContext.UiThreadId)
            {
                MacOSMessageLoopSynchronizationContext.Instance.Post(_ => PostMessage(message), null);
                return;
            }
            string js = "window.wwuiReceive(" + JsStringLiteral.Quote(WebView2StringCodec.Encode(message)) + ")";
            _ = _webView.EvaluateJavaScriptAsync(js)
                .ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
        }
        catch
        {
            // 窗口关闭后 WKWebView 已销毁，忽略（定时器仍在后台推送）
        }
    }

    /// <summary>
    /// 在页面里执行一段 JavaScript 并返回结果（与 WebView2 一样是 JSON 编码的字符串；best-effort）。
    /// 与 <see cref="PostMessage"/> 一样：WKWebView 只能主线程访问，非主线程调用时先投递回主线程再执行。
    /// 结果用 JSON.stringify 包一层，与 WebView2 ExecuteScriptAsync 返回「结果值的 JSON 表示」对齐。
    /// </summary>
    public async Task<string> ExecuteScriptAsync(string script)
    {
        if (Environment.CurrentManagedThreadId != MacOSMessageLoopSynchronizationContext.UiThreadId)
        {
            var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            MacOSMessageLoopSynchronizationContext.Instance.Post(async _ =>
            {
                try { tcs.TrySetResult(await ExecuteScriptAsync(script)); }
                catch (Exception ex) { tcs.TrySetException(ex); }
            }, null);
            return await tcs.Task;
        }

        NSObject? result = await _webView.EvaluateJavaScriptAsync("JSON.stringify(" + script + ")");
        return result?.ToString() ?? "null";
    }

    /// <summary>页面导航完成时触发（用于在页面就绪后推送 Model 初始快照）。</summary>
    public event Action? NavigationCompleted;

    /// <summary>页面 JS 通过 script message handler 回传的消息（protobuf 字节，由 NUL 转义串还原）。</summary>
    public event Action<byte[]>? MessageReceived;

    private void OnWindowWillClose()
    {
        if (_closed)
            return;
        _closed = true;
        Closed?.Invoke();
        WebWindow.NotifyWindowClosed();
        if (WebWindow.OpenCount == 0)
            NSApplication.SharedApplication.Terminate(null); // 最后一个窗口关闭，退出主事件循环
    }

    /// <summary>
    /// 把动作 marshal 到主线程同步执行：主线程直接运行；非主线程经
    /// <see cref="MacOSMessageLoopSynchronizationContext.Send"/>（回主线程并阻塞等待）。
    /// </summary>
    private void RunOnMainThread(Action action)
    {
        if (Environment.CurrentManagedThreadId == MacOSMessageLoopSynchronizationContext.UiThreadId)
        {
            action();
            return;
        }
        MacOSMessageLoopSynchronizationContext.Instance.Send(_ => action(), null);
    }

    /// <summary>窗口关闭回调（windowWillClose:）。</summary>
    private sealed class MacWindowDelegate : NSWindowDelegate
    {
        private readonly Action _onWillClose;
        public MacWindowDelegate(Action onWillClose) => _onWillClose = onWillClose;
        public override void WillClose(NSNotification notification) => _onWillClose();
    }

    /// <summary>导航完成回调（webView:didFinishNavigation:）。</summary>
    private sealed class MacNavigationDelegate : NSObject, IWKNavigationDelegate
    {
        private readonly Action _onFinished;
        public MacNavigationDelegate(Action onFinished) => _onFinished = onFinished;
        [Export("webView:didFinishNavigation:")]
        public void DidFinishNavigation(WKWebView webView, WKNavigation navigation) => _onFinished();
    }

    /// <summary>JS → native：window.webkit.messageHandlers.wwui.postMessage(...) 的回调。</summary>
    private sealed class MacScriptMessageHandler : NSObject, IWKScriptMessageHandler
    {
        private readonly Action<byte[]> _onMessage;
        public MacScriptMessageHandler(Action<byte[]> onMessage) => _onMessage = onMessage;

        [Export("userContentController:didReceiveScriptMessage:")]
        public void DidReceiveScriptMessage(WKUserContentController userContentController, WKScriptMessage message)
        {
            // message.Body 对字符串是桥接的 NSString（__NSCFString）；本桥传 NUL 转义的 Latin-1 串
            if (message.Body is not NSString ns || ns.Length == 0)
                return;
            _onMessage(WebView2StringCodec.Decode(ns.ToString()));
        }
    }

    /// <summary>
    /// 自定义 scheme 响应（app:// / appbin://）。WKURLSchemeTask 的回调在后台队列触发，
    /// 这里同步构造响应即可（读流 → NSData → DidReceiveResponse/Data/Finish）。
    /// </summary>
    private sealed class MacSchemeHandler : NSObject, IWKUrlSchemeHandler
    {
        private readonly MacOSWindow _owner;
        public MacSchemeHandler(MacOSWindow owner) => _owner = owner;

        [Export("webView:startURLSchemeTask:")]
        public void StartUrlSchemeTask(WKWebView webView, IWKUrlSchemeTask urlSchemeTask)
        {
            try
            {
                string uri = urlSchemeTask.Request.Url.AbsoluteString;
                WebWindowOptions options = _owner._options;

                // 数据通道：请求来自 DataScheme 时交给 DataResolver，否则走 UI 资源（ResourceResolver）
                bool isData = WebResourceLocator.IsScheme(uri, options.DataScheme);
                string scheme = isData ? options.DataScheme! : options.Scheme;
                Func<string, Stream?>? resolver = isData ? options.DataResolver : options.ResourceResolver;

                if (resolver is not null && WebResourceLocator.TryResolvePath(uri, scheme, out string? relative, out string? mimeType))
                {
                    Stream? stream = resolver(relative!);
                    if (stream is not null)
                    {
                        using (stream)
                        {
                            // 读全量字节再交给 NSData（响应需一次给完；嵌入式流不可 seek）
                            byte[] bytes;
                            using (var ms = new MemoryStream())
                            {
                                stream.CopyTo(ms);
                                bytes = ms.ToArray();
                            }
                            SendResponse(urlSchemeTask, 200, mimeType!, ResourceHeaders.CacheControl(relative!), bytes);
                        }
                        return;
                    }
                }
            }
            catch
            {
                // 读取或构造响应失败时回退 404
            }
            SendResponse(urlSchemeTask, 404, "text/plain; charset=utf-8", "no-store",
                Encoding.UTF8.GetBytes("404 Not Found"));
        }

        [Export("webView:stopURLSchemeTask:")]
        public void StopUrlSchemeTask(WKWebView webView, IWKUrlSchemeTask urlSchemeTask)
        {
            // 任务被取消/替换时调用；无需清理（响应还未发出则 WKWebView 自行失效）
        }

        private static void SendResponse(IWKUrlSchemeTask task, int status, string contentType, string cacheControl, byte[] bytes)
        {
            try
            {
                // FromObjectsAndKeys(objects, keys, count)：与 ObjC dictionaryWithObjects:forKeys:count: 同序
                var headers = NSDictionary.FromObjectsAndKeys(
                    new NSObject[] { new NSString(contentType), new NSString(cacheControl) },
                    new NSObject[] { new NSString("Content-Type"), new NSString("Cache-Control") },
                    2);
                task.DidReceiveResponse(new NSHttpUrlResponse(task.Request.Url, status, "HTTP/1.1", headers));
                task.DidReceiveData(NSData.FromArray(bytes));
                task.DidFinish();
            }
            catch
            {
                // 任务已被 WKWebView 失效（导航取消等），忽略
            }
        }
    }
}
#endif
