using System.Runtime.InteropServices;
using System.Text;
using WebKit;
using WebWindowUI.Core;
using WebWindowUI.Core.Platform;
using WebWindowUI.Core.Protocol;
using WebWindowUI.Natives.Macos;

namespace WebWindowUI.Platforms.MacOS;

/// <summary>
/// macOS 平台：NSWindow + WKWebView 的窗口，可创建多个实例。自定义 scheme（WKURLSchemeHandler）
/// 与 script message handler 都按窗口独立注册（不像 Linux 共享默认 WebContext 需要进程级注册）。
/// 窗口状态面经 <see cref="MacOSNativeWindow"/> 真实现；Model 双向绑定在基类契约内完成。
/// Cocoa 只允许主线程访问，所有原生操作经 <see cref="RunOnMainThread"/> marshal。
/// </summary>
public sealed class MacOSWindow : WebWindow
{
    private const string BridgeHandlerName = "wwui"; // 与前端桥 webwindowui-bridge 的 HANDLER_NAME 一致

    private readonly MacOSNativeWindow _nativeWindow;
    private readonly WKWebView _webView;
    private readonly Action<byte[]> _modelPushHandler;
    private readonly ManualResetEventSlim _closedEvent = new(false);
    private string _title;
    private WebWindowModel? _model;
    private bool _isLoaded;
    private bool _closed;

    // 强引用保留 delegate/handler 对象——.NET 绑定不 retain ObjC 委托，被 GC 会静默失效
    private readonly MacNavigationDelegate _navigationDelegate;
    private readonly MacScriptMessageHandler _scriptMessageHandler;
    private readonly MacSchemeHandler _schemeHandler;

    /// <summary>
    /// 原生窗口（平台窗口内部使用）。
    /// </summary>
    internal override INativeWindow NativeWindow
    {
        get => _nativeWindow;
        set => throw new NotSupportedException("MacOSWindow 自建原生窗口，不支持替换。");
    }

    /// <summary>
    /// 构造窗口：挂关闭/导航/消息/scheme 四类委托，建 WKWebView 并设为窗口内容。
    /// </summary>
    /// <param name="options">窗口选项。</param>
    internal MacOSWindow(WebWindowOptions options) : base(options)
    {
        _title = options.Title;
        _modelPushHandler = ModelPushed;
        _nativeWindow = new MacOSNativeWindow(options);
        NSWindow window = _nativeWindow.Window;

        // 窗口关闭（windowWillClose:）→ 通知框架关闭
        _nativeWindow.Destory += OnWindowWillClose;

        // 自定义 scheme（app:// / appdata://）按窗口独立注册；回调走静态 WebWindowResource 分派
        _schemeHandler = new MacSchemeHandler();
        var config = new WKWebViewConfiguration();
        config.SetUrlSchemeHandler(_schemeHandler, WebWindowResource.Scheme);
        config.SetUrlSchemeHandler(_schemeHandler, WebWindowResource.SchemeData);

        // JS → native 通道：页面经 window.webkit.messageHandlers.wwui.postMessage(...) 回传 NUL 转义串
        _scriptMessageHandler = new MacScriptMessageHandler(OnBackendMessageReceived);
        config.UserContentController.AddScriptMessageHandler(_scriptMessageHandler, BridgeHandlerName);

        _webView = new WKWebView(CGRect.Empty, config)
        {
            // 交给窗口后按内容尺寸布局，窗口缩放时跟随
            AutoresizingMask = NSViewResizingMask.WidthSizable | NSViewResizingMask.HeightSizable,
        };
        window.ContentView = _webView;

        // 导航完成 → 页面就绪，推初始快照
        _navigationDelegate = new MacNavigationDelegate(OnNavigationFinished);
        _webView.NavigationDelegate = _navigationDelegate;
    }

    /// <summary>
    /// 窗口标题：get 返回跟踪字段；set 同步到标题栏（构造期基类先赋值、原生窗口尚未建，跳过原生调用）。
    /// </summary>
    public override string Title
    {
        get => _title;
        set
        {
            _title = value;
            if (_nativeWindow is not null)
                RunOnMainThread(() => _nativeWindow.SetTitle(value));
        }
    }

    /// <summary>
    /// 窗口数据模型：绑定时订阅模型推送（属性变化/集合变更→PostMessage），解绑时退订；
    /// 页面加载完成后补发完整快照。同一实例绑多窗口 = 共享广播。
    /// </summary>
    public override WebWindowModel? Model
    {
        get => _model;
        set
        {
            if (_model == value)
                return;
            _model?.UnsubscribePushed(_modelPushHandler);
            _model = value;
            _model?.SubscribePushed(_modelPushHandler);
            if (_model is not null && _isLoaded)
                PostMessage(_model.BuildSnapshotEnvelope());
        }
    }

    /// <summary>
    /// 窗口装饰样式。
    /// </summary>
    public override SystemDecorations SystemDecorations
    {
        get => GetNative(() => _nativeWindow.SystemDecorations);
        set => RunOnMainThread(() => _nativeWindow.SystemDecorations = value);
    }

    /// <summary>
    /// 窗口状态。
    /// </summary>
    public override WindowState WindowState
    {
        get => GetNative(() => _nativeWindow.WindowState);
        set => RunOnMainThread(() => _nativeWindow.WindowState = value);
    }

    /// <summary>
    /// 窗口位置。
    /// </summary>
    public override Point2I Position
    {
        get => GetNative(() => _nativeWindow.Position);
        set => RunOnMainThread(() => _nativeWindow.Position = value);
    }

    /// <summary>
    /// 窗口尺寸。
    /// </summary>
    public override Point2I Size
    {
        get => GetNative(() => _nativeWindow.Size);
        set => RunOnMainThread(() => _nativeWindow.Size = value);
    }

    /// <summary>
    /// 最小尺寸（0 = 不限）。
    /// </summary>
    public override Point2I MinSize
    {
        get => _nativeWindow.MinSize;
        set => _nativeWindow.MinSize = value;
    }

    /// <summary>
    /// 最大尺寸（0 = 不限）。
    /// </summary>
    public override Point2I MaxSize
    {
        get => _nativeWindow.MaxSize;
        set => _nativeWindow.MaxSize = value;
    }

    /// <summary>
    /// 是否显示在任务栏（Dock）：macOS 按 App 不按窗口，no-op（文档注明）。
    /// </summary>
    public override bool ShowInTaskbar
    {
        get => _nativeWindow.ShowInTaskbar;
        set => _nativeWindow.ShowInTaskbar = value;
    }

    /// <summary>
    /// 是否可调整大小。
    /// </summary>
    public override bool CanResize
    {
        get => _nativeWindow.CanResize;
        set => RunOnMainThread(() => _nativeWindow.CanResize = value);
    }

    /// <summary>
    /// 是否可最小化。
    /// </summary>
    public override bool CanMinimize
    {
        get => _nativeWindow.CanMinimize;
        set => RunOnMainThread(() => _nativeWindow.CanMinimize = value);
    }

    /// <summary>
    /// 是否可最大化：macOS 无独立开关，no-op（文档注明）。
    /// </summary>
    public override bool CanMaximize
    {
        get => _nativeWindow.CanMaximize;
        set => _nativeWindow.CanMaximize = value;
    }

    /// <summary>
    /// 是否对话框式窗口：macOS 用 NSPanel，运行时不可切换，no-op（文档注明）。
    /// </summary>
    public override bool IsDialog
    {
        get => _nativeWindow.IsDialog;
        set => _nativeWindow.IsDialog = value;
    }

    /// <summary>
    /// 窗口当前是否活动（由系统推导；置前台请用 <see cref="Activate"/>）。
    /// </summary>
    public override bool IsActive
    {
        get => GetNative(() => _nativeWindow.IsActive);
        set { }
    }

    /// <summary>
    /// 窗口所在显示器。
    /// </summary>
    public override Screen Screens => GetNative(() => _nativeWindow.Screens);

    /// <summary>
    /// 显示窗口并加载首页。Cocoa 只允许主线程访问，非主线程调用时 marshal 回主线程。
    /// 无头模式下跳过 MakeKeyAndOrderFront（窗口不出现在屏幕/Dock），但照常加载首页。
    /// </summary>
    public override void Show(WebWindow? Parent = null)
    {
        RunOnMainThread(() =>
        {
            if (!Options.Headless)
            {
                _nativeWindow.Show();
            }
            var url = WebWindowResource.GetWindowIndexUrl(Options.WindowPath, Options.Query);
            WebWindowLog.Debug($"macos show {url}");
            _webView.LoadRequest(NSUrlRequest.FromUrl(NSUrl.FromString(url)!));
        });
    }

    /// <summary>
    /// 模态显示：显示窗口后阻塞调用线程直到关闭。主线程调用走裸 CFRunLoopRunInMode 泵（排干主队列，
    /// 派发 WKWebView 事件）；后台线程直接等待（关闭由主事件循环泵触发）。对话框关闭结果经 Close(result) 传入。
    /// </summary>
    public override void ShowDialog(WebWindow? Parent = null)
    {
        Show(Parent);
        if (Environment.CurrentManagedThreadId == MacOSMessageLoopSynchronizationContext.UiThreadId)
        {
            using var mode = new NSString("kCFRunLoopDefaultMode"); // 强引用保住 CFString（Handle 别名不 retain）
            IntPtr modeHandle = mode.Handle;
            while (!_closed)
                CFRunLoopRunInMode(modeHandle, 0.1, false);
        }
        else
        {
            _closedEvent.Wait();
        }
    }

    /// <summary>
    /// 隐藏窗口（不关闭、不销毁）。
    /// </summary>
    public override void Hide() => RunOnMainThread(() => _nativeWindow.Hide());

    /// <summary>
    /// 关闭窗口。windowWillClose: → 通知框架关闭。
    /// </summary>
    /// <param name="result">对话框关闭结果（当前平台未使用）。</param>
    public override void Close(object? result)
    {
        RunOnMainThread(() =>
        {
            if (_closed)
                return;
            _nativeWindow.Close();
        });
    }

    /// <summary>
    /// 把窗口带到前台并聚焦。进程本身不带 bundle 时无法跨 App 置前，仅激活本窗口。
    /// </summary>
    public override void Activate() => RunOnMainThread(_nativeWindow.Activate);

    /// <summary>
    /// 设置窗口图标。macOS 窗口无 per-window 图标（图标属于 App Bundle），无操作。
    /// </summary>
    /// <param name="icon">窗口图标；null 不操作。</param>
    public override void SetIcon(WindowIcon? icon)
    {
        // 平台限制，文档注明
    }

    /// <summary>
    /// 向页面 JS 发送一条消息。protobuf 字节经 <see cref="StringCodec"/> 转成不含 NUL 的
    /// Latin-1 字符串，再用 <see cref="JsStringLiteral.Quote"/> 嵌进 <c>window.wwuiReceive("...")</c>
    /// 经 evaluateJavaScript 注入。JS 端 wwuiReceive 还原后 protobufjs 解码。
    /// 与 Windows 一致：WKWebView 只能主线程访问，属性变更可能发生在任意线程，先投递回主线程。
    /// </summary>
    /// <param name="message">protobuf 字节。</param>
    internal override void PostMessage(byte[] message)
    {
        try
        {
            if (Environment.CurrentManagedThreadId != MacOSMessageLoopSynchronizationContext.UiThreadId)
            {
                MacOSMessageLoopSynchronizationContext.Instance.Post(_ => PostMessage(message), null);
                return;
            }
            if (_closed)
                return;
            var js = "window.wwuiReceive(" + JsStringLiteral.Quote(StringCodec.Encode(message)) + ")";
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
    /// <param name="script">要执行的 JS。</param>
    /// <returns>JS 执行结果（JSON 字符串）。</returns>
    internal override async Task<string> ExecuteScriptAsync(string script)
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

        var result = await _webView.EvaluateJavaScriptAsync("JSON.stringify(" + script + ")");
        return result?.ToString() ?? "null";
    }

    /// <summary>
    /// 导航完成回调：页面就绪 → Loaded + 补发 Model 快照。
    /// </summary>
    private void OnNavigationFinished()
    {
        WebWindowLog.Debug("macos nav-finished");
        _isLoaded = true;
        RaiseLoaded();
        if (_model is not null)
            PostMessage(_model.BuildSnapshotEnvelope());
    }

    /// <summary>
    /// 窗口关闭回调：注销窗口表；最后一个窗口关闭 → Terminate 退出主事件循环。
    /// </summary>
    private void OnWindowWillClose()
    {
        if (_closed)
            return;
        _closed = true;
        _model?.UnsubscribePushed(_modelPushHandler);
        _closedEvent.Set();
        RaiseClosed();
        MacOSPlatform.WindowClose(this); // 注销窗口表；最后一个窗口关闭 → Terminate 退出主事件循环
    }

    /// <summary>
    /// 模型推送回调：把变化信封转发给页面。
    /// </summary>
    /// <param name="envelope">模型变化信封（protobuf 字节）。</param>
    private void ModelPushed(byte[] envelope) => PostMessage(envelope);

    /// <summary>
    /// 在主线程读原生窗口状态（NSWindow 只能主线程访问）。
    /// </summary>
    /// <typeparam name="T">状态值类型。</typeparam>
    /// <param name="getter">读取委托。</param>
    /// <returns>状态值。</returns>
    private T GetNative<T>(Func<T> getter)
    {
        T result = default!;
        RunOnMainThread(() => result = getter());
        return result;
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

    /// <summary>
    /// 排干主队列的嵌套 run loop（模态对话框专用；CFString mode 须强引用保 Handle）。
    /// </summary>
    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern nint CFRunLoopRunInMode(IntPtr mode, double seconds, bool returnAfterSourceHandled);

    /// <summary>
    /// 导航完成回调（webView:didFinishNavigation:）。
    /// </summary>
    private sealed class MacNavigationDelegate(Action onFinished) : NSObject, IWKNavigationDelegate
    {
        [Export("webView:didFinishNavigation:")]
        public void DidFinishNavigation(WKWebView webView, WKNavigation navigation)
        {
            WebWindowLog.Debug("macos nav-finished");
            onFinished();
        }
    }

    /// <summary>
    /// JS → native：window.webkit.messageHandlers.wwui.postMessage(...) 的回调。
    /// </summary>
    private sealed class MacScriptMessageHandler(Action<byte[]> onMessage) : NSObject, IWKScriptMessageHandler
    {
        [Export("userContentController:didReceiveScriptMessage:")]
        public void DidReceiveScriptMessage(WKUserContentController userContentController, WKScriptMessage message)
        {
            // message.Body 对字符串是桥接的 NSString（__NSCFString）；本桥传 NUL 转义的 Latin-1 串
            if (message.Body is not NSString ns || ns.Length == 0)
                return;
            onMessage(StringCodec.Decode(ns.ToString()));
        }
    }

    /// <summary>
    /// 自定义 scheme 响应（app:// / appdata://）。WKURLSchemeTask 的回调在后台队列触发，
    /// 这里同步构造响应即可（读流 → NSData → DidReceiveResponse/Data/Finish）。
    /// app 与 appdata 统一走 <see cref="WebWindowResource"/>：UI 资源（wwwroot）与数据通道
    /// （custom route）由它按 scheme + host 分派（镜像 Windows 的 OnWebResourceRequested / Linux 的 HandleUriSchemeRequest）。
    /// </summary>
    private sealed class MacSchemeHandler : NSObject, IWKUrlSchemeHandler
    {
        [Export("webView:startURLSchemeTask:")]
        public void StartUrlSchemeTask(WKWebView webView, IWKUrlSchemeTask urlSchemeTask)
        {
            try
            {
                var uri = urlSchemeTask.Request.Url?.AbsoluteString;
                if (uri is not null
                    && WebWindowResource.TryResolvePath(uri, out string? relative, out string? mimeType) is { } stream)
                {
                    using (stream)
                    {
                        // 读全量字节再交给 NSData（响应需一次给完；嵌入式流不可 seek）
                        using var ms = new MemoryStream();
                        stream.CopyTo(ms);
                        var bytes = ms.ToArray();
                        SendResponse(urlSchemeTask, 200, mimeType!, WebWindowResource.CacheControl(relative!), bytes);
                    }
                    return;
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
                // CORS：页面源 fetch 数据通道属跨源，须回 Access-Control-Allow-Origin（同 Windows/CEF 策略）
                var headers = NSDictionary.FromObjectsAndKeys(
                    new NSObject[] { new NSString(contentType), new NSString(cacheControl), new NSString("*") },
                    new NSObject[] { new NSString("Content-Type"), new NSString("Cache-Control"), new NSString("Access-Control-Allow-Origin") },
                    3);
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
