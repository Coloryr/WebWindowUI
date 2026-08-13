using Microsoft.Web.WebView2.Core;
using WebWindowUI.Core;
using WebWindowUI.Core.Protocol;
using WebWindowUI.Natives.Windows;

namespace WebWindowUI.Platforms.Windows;

/// <summary>
/// Windows 平台：承载 WebView2 的 Win32 裸窗口，可创建多个实例。
/// 同类 scheme 的所有实例共享同一个 CoreWebView2Environment（自定义 scheme 只注册一次）。
/// </summary>
public sealed class WindowsWindow : IWindowBackend
{
    private readonly WebWindowOptions _options;
    private readonly Win32NativeWindow _nativeWindow;

    private CoreWebView2Controller? _controller;

    /// <summary>
    /// 原生窗口句柄。
    /// </summary>
    public IntPtr Hwnd => _nativeWindow.WindowHandle;

    private bool _closed;

    /// <summary>
    /// 窗口销毁时触发（用户关闭或 Close()）。宿主在此清理与窗口关联的状态。
    /// </summary>
    public event Action? Closed;

    internal WindowsWindow(WebWindowOptions options)
    {
        _options = options;
        _nativeWindow = new Win32NativeWindow(options);

        _nativeWindow.Destory += NativeWindow_Destory;
        _nativeWindow.Resize += NativeWindow_Resize;
    }

    /// <summary>
    /// 原生窗口尺寸变化：同步 WebView2 控件边界。
    /// </summary>
    private void NativeWindow_Resize()
    {
        if (_controller is null)
            return;

        _controller.Bounds = _nativeWindow.GetSize();
    }

    /// <summary>
    /// 原生窗口销毁：关闭 WebView2 控制器并触发 Closed。
    /// </summary>
    private void NativeWindow_Destory()
    {
        _controller?.Close();
        _controller = null;
        _closed = true;
        Closed?.Invoke();
    }

    /// <summary>
    /// 显示窗口并异步初始化 WebView2（无头模式只初始化不显示）。
    /// </summary>
    public void Show()
    {
        if (!_options.Headless)
        {
            _nativeWindow.Show();
        }
        _ = InitWebViewAsync();
    }

    /// <summary>
    /// 隐藏窗口（不关闭、不销毁）。
    /// </summary>
    public void Hide()
    {
        if (!_options.Headless)
        {
            _nativeWindow.Hide();
        }
    }

    /// <summary>
    /// 关闭窗口。关闭最后一个窗口后程序自动退出。
    /// </summary>
    public void Close()
    {
        // DestroyWindow 必须在创建窗口的线程调用；宿主可能从任意线程关窗，marshal 回 UI 线程同步执行。
        WebWindowPlatform.Current.RunOnUiThread(() =>
        {
            if (_closed)
                return;
            _closed = true;
            _nativeWindow.Close();
        });
    }

    /// <summary>
    /// 把窗口带到前台并聚焦：先恢复最小化，再置前、设焦点。
    /// </summary>
    public void Activate()
    {
        WebWindowPlatform.Current.RunOnUiThread(_nativeWindow.Activate);
    }

    /// <summary>
    /// 修改窗口标题（立即同步到标题栏）。
    /// </summary>
    public void SetTitle(string title)
    {
        WebWindowPlatform.Current.RunOnUiThread(() => _nativeWindow.SetTitle(title));
    }

    /// <summary>
    /// 设置窗口图标（标题栏 + 任务栏）。替换旧图标时释放旧的句柄。
    /// </summary>
    public void SetIcon(WindowIcon icon)
    {
        WebWindowPlatform.Current.RunOnUiThread(() =>
        {
            _nativeWindow.SetIcon(icon);
        });
    }

    /// <summary>
    /// 向页面 JS 发送一条 protobuf 消息：经 <see cref="WebView2StringCodec"/> 做 NUL 转义后走
    /// PostWebMessageAsString（WebView2 消息通道在首个 NUL 处截断，protobuf 字节普遍含 0x00）。
    /// </summary>
    /// <param name="message">protobuf 字节。</param>
    public void PostMessage(byte[] message)
    {
        try
        {
            if (WebWindowPlatform.Current.IsUiThread())
            {
                _controller?.CoreWebView2.PostWebMessageAsString(WebView2StringCodec.Encode(message));
            }
            else
            {
                WebWindowPlatform.Current.RunOnUiThread(() => PostMessage(message));
            }
        }
        catch
        {
            // 窗口关闭后控制器已释放，忽略（定时器仍在后台推送）
        }
    }

    /// <summary>
    /// 在页面里执行一段 JavaScript 并返回结果（JSON 编码的字符串，与 WebView2 一致）。
    /// 与 <see cref="PostMessage"/> 一样：CoreWebView2 只能在 UI 线程访问，非 UI 线程调用时
    /// 先投递回 UI 线程再执行，并等待结果。
    /// </summary>
    public async Task<string> ExecuteScriptAsync(string script)
    {
        if (!WebWindowPlatform.Current.IsUiThread())
        {
            var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            WebWindowPlatform.Current.RunOnUiThread(async () =>
            {
                try { tcs.TrySetResult(await ExecuteScriptAsync(script)); }
                catch (Exception ex) { tcs.TrySetException(ex); }
            });
            return await tcs.Task;
        }

        if (_controller?.CoreWebView2 is null)
            throw new InvalidOperationException("WebView2 尚未初始化完成。");
        return await _controller.CoreWebView2.ExecuteScriptAsync(script);
    }

    /// <summary>
    /// 页面导航完成时触发（用于在页面就绪后推送 Model 初始快照）。
    /// </summary>
    public event Action? NavigationCompleted;

    /// <summary>
    /// 页面 JS 通过 postMessage 回传的消息（protobuf 字节，由 Latin-1 字节串还原）。
    /// </summary>
    public event Action<byte[]>? MessageReceived;

    /// <summary>
    /// 创建 WebView2 控制器、导航到窗口页面并挂导航/消息回调。
    /// </summary>
    private async Task InitWebViewAsync()
    {
        try
        {
            _controller = await WindowsPlatform.CreateCoreWebView2ControllerAsync(_nativeWindow.WindowHandle);
            if (_closed)
            {
                _controller.Close();
                _controller = null;
                return;
            }

            _controller.Bounds = _nativeWindow.GetSize();

            var core = _controller.CoreWebView2;

            core.Navigate(WebWindowResource.GetWindowIndexUrl(_options.WindowPath));

            // Model 双向绑定通道：页面就绪通知 + JS 回传消息
            core.NavigationCompleted += (_, _) =>
            {
                NavigationCompleted?.Invoke();
            };
            core.WebMessageReceived += (_, args) =>
            {
                var message = args.TryGetWebMessageAsString();
                if (message.Length == 0)
                    return;

                // JS 侧经 NUL 转义编码后回传（模型.bridge 的 bytesToEscaped），这里还原回 protobuf 字节
                MessageReceived?.Invoke(WebView2StringCodec.Decode(message));
            };
        }
        catch (Exception ex)
        {
            WebWindowLog.Error($"WebView2 初始化失败：{ex.Message}\n请确认已安装 WebView2 运行时。");
        }
    }
}
