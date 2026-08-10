using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Web.WebView2.Core;
using WebWindowUI.Core;
using WebWindowUI.Core.Protocol;
using WebWindowUI.Natives.Windows;

namespace WebWindowUI.Windows;

/// <summary>
/// Windows 平台：承载 WebView2 的 Win32 裸窗口，可创建多个实例。
/// 同类 scheme 的所有实例共享同一个 CoreWebView2Environment（自定义 scheme 只注册一次）。
/// </summary>
public sealed class WindowsWindow : IWindowBackend
{
    private const string WindowClass = "WebView2Window";

    // ---- 进程级共享状态 ----
    private static bool _classRegistered;
    private static Win32.WndProcDelegate _wndProc = null!; // 保活，防止被 GC 回收
    private static readonly Dictionary<IntPtr, WindowsWindow> _windows = [];
    private static readonly object _envLock = new();
    private static readonly Dictionary<string, Task<CoreWebView2Environment>> _environmentTasks = [];

    private readonly IntPtr _hwnd;
    private readonly WebWindowOptions _options;
    private CoreWebView2Environment? _environment;
    private CoreWebView2Controller? _controller;
    private IntPtr _hIcon;
    private bool _closed;

    public IntPtr Hwnd => _hwnd;

    /// <summary>窗口销毁时触发（用户关闭或 Close()）。宿主在此清理与窗口关联的状态。</summary>
    public event Action? Closed;

    private WindowsWindow(IntPtr hwnd, WebWindowOptions options)
    {
        _hwnd = hwnd;
        _options = options;
        _windows[hwnd] = this;
    }

    /// <summary>创建并注册一个尚未显示的窗口。</summary>
    public static WindowsWindow Create(string title, WebWindowOptions options, int width, int height)
    {
        EnsureClassRegistered();

        var hwnd = Win32.CreateWindowExW(
            0, WindowClass, title, Win32.WS_OVERLAPPEDWINDOW,
            Win32.CW_USEDEFAULT, Win32.CW_USEDEFAULT, width, height,
            IntPtr.Zero, IntPtr.Zero, Win32.GetModuleHandleW(null), IntPtr.Zero);
        if (hwnd == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "创建窗口失败 (CreateWindowExW)");

        return new WindowsWindow(hwnd, options);
    }

    /// <summary>显示窗口并初始化 WebView2。无头模式下只初始化 WebView，窗口永不显示（SW_SHOW 也跳过）。</summary>
    public void Show()
    {
        if (!_options.Headless)
            Win32.ShowWindow(_hwnd, Win32.SW_SHOW);
        _ = InitWebViewAsync();
    }

    /// <summary>隐藏窗口（不关闭、不销毁）。</summary>
    public void Hide() => Win32.ShowWindow(_hwnd, Win32.SW_HIDE);

    /// <summary>关闭窗口。关闭最后一个窗口后程序自动退出。</summary>
    public void Close()
    {
        // DestroyWindow 必须在创建窗口的线程调用；宿主可能从任意线程关窗，marshal 回 UI 线程同步执行。
        RunOnUiThread(() =>
        {
            if (_closed)
                return;
            _closed = true;
            Win32.DestroyWindow(_hwnd);
        });
    }

    /// <summary>把窗口带到前台并聚焦：先恢复最小化，再置前、设焦点。</summary>
    public void Activate()
    {
        RunOnUiThread(() =>
        {
            if (Win32.IsIconic(_hwnd))
                Win32.ShowWindow(_hwnd, Win32.SW_RESTORE);
            Win32.SetForegroundWindow(_hwnd);
            Win32.SetFocus(_hwnd);
        });
    }

    /// <summary>修改窗口标题（立即同步到标题栏）。</summary>
    public void SetTitle(string title)
        => RunOnUiThread(() => Win32.SetWindowTextW(_hwnd, title));

    /// <summary>设置窗口图标（标题栏 + 任务栏）。替换旧图标时释放旧的句柄。</summary>
    public void SetIcon(WindowIcon icon)
    {
        RunOnUiThread(() =>
        {
            var hIcon = LoadIconHandle(icon);
            if (hIcon == IntPtr.Zero)
                return;

            if (_hIcon != IntPtr.Zero)
                Win32.DestroyIcon(_hIcon);
            _hIcon = hIcon;

            Win32.SendMessageW(_hwnd, Win32.WM_SETICON, (IntPtr)Win32.ICON_BIG, hIcon);
            Win32.SendMessageW(_hwnd, Win32.WM_SETICON, (IntPtr)Win32.ICON_SMALL, hIcon);
        });
    }

    /// <summary>
    /// 把动作 marshal 到 UI 线程同步执行：UI 线程直接运行；非 UI 线程经
    /// <see cref="MessageLoopSynchronizationContext.Send"/>（回 UI 线程并阻塞等待）。
    /// Win32 窗口 API（DestroyWindow/SetForegroundWindow/SetWindowTextW/SendMessage）都要求 UI 线程。
    /// </summary>
    private void RunOnUiThread(Action action)
    {
        if (Environment.CurrentManagedThreadId == MessageLoopSynchronizationContext.UiThreadId)
        {
            action();
            return;
        }
        MessageLoopSynchronizationContext.Instance.Send(_ => action(), null);
    }

    /// <summary>
    /// 向页面 JS 发送一条消息。protobuf 字节经 <see cref="WebView2StringCodec"/> 转成
    /// 不含 NUL 的 Latin-1 字符串再传给 PostWebMessageAsString：WebView2 的消息字符串通道会在
    /// 第一个 NUL（char code 0）处截断，而 protobuf 字节普遍含 0x00（varint 零值、double 的
    /// fixed64 等），原样传输必然损坏，故只对 NUL（及转义符自身）做转义。JS 端逆操作还原后
    /// 再 protobufjs 解码。页面未加载完成或窗口已关闭时静默忽略。
    /// </summary>
    public void PostMessage(byte[] message)
    {
        try
        {
            // CoreWebView2 只能在 UI 线程访问。属性变更可能发生在任意线程
            // （如示例的 System.Threading.Timer 回调），非 UI 线程调用时先投递回 UI 线程。
            // 用线程 id 判断而非 SynchronizationContext.Current：Timer 会随 ExecutionContext
            // 把 UI 线程的上下文流到线程池线程，SynchronizationContext.Current 会误判。
            if (Environment.CurrentManagedThreadId != MessageLoopSynchronizationContext.UiThreadId)
            {
                MessageLoopSynchronizationContext.Instance.Post(_ => PostMessage(message), null);
                return;
            }
            _controller?.CoreWebView2.PostWebMessageAsString(WebView2StringCodec.Encode(message));
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
        if (Environment.CurrentManagedThreadId != MessageLoopSynchronizationContext.UiThreadId)
        {
            var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            MessageLoopSynchronizationContext.Instance.Post(async _ =>
            {
                try { tcs.TrySetResult(await ExecuteScriptAsync(script)); }
                catch (Exception ex) { tcs.TrySetException(ex); }
            }, null);
            return await tcs.Task;
        }

        if (_controller?.CoreWebView2 is null)
            throw new InvalidOperationException("WebView2 尚未初始化完成。");
        return await _controller.CoreWebView2.ExecuteScriptAsync(script);
    }

    /// <summary>页面导航完成时触发（用于在页面就绪后推送 Model 初始快照）。</summary>
    public event Action? NavigationCompleted;

    /// <summary>页面 JS 通过 postMessage 回传的消息（protobuf 字节，由 Latin-1 字节串还原）。</summary>
    public event Action<byte[]>? MessageReceived;

    /// <summary>把 WindowIcon（文件或流）加载成 HICON。流会先落到临时文件再加载。</summary>
    private static IntPtr LoadIconHandle(WindowIcon icon)
    {
        if (icon.FilePath is not null)
        {
            return Win32.LoadImageW(IntPtr.Zero, icon.FilePath, Win32.IMAGE_ICON,
                0, 0, Win32.LR_LOADFROMFILE | Win32.LR_DEFAULTSIZE);
        }

        if (icon.Stream is not null)
        {
            var tmp = Path.Combine(Path.GetTempPath(), "webwindowui_" + Guid.NewGuid().ToString("N") + ".ico");
            try
            {
                using (FileStream fs = File.Create(tmp))
                    icon.Stream.CopyTo(fs);
                return Win32.LoadImageW(IntPtr.Zero, tmp, Win32.IMAGE_ICON,
                    0, 0, Win32.LR_LOADFROMFILE | Win32.LR_DEFAULTSIZE);
            }
            finally
            {
                try { File.Delete(tmp); } catch { /* 临时文件清理失败可忽略 */ }
            }
        }

        return IntPtr.Zero;
    }

    private static void EnsureClassRegistered()
    {
        if (_classRegistered)
            return;

        _wndProc = WndProc;
        var wc = new Win32.WNDCLASSEXW
        {
            cbSize = (uint)Marshal.SizeOf<Win32.WNDCLASSEXW>(),
            style = Win32.CS_HREDRAW | Win32.CS_VREDRAW,
            lpfnWndProc = _wndProc,
            hInstance = Win32.GetModuleHandleW(null),
            hIcon = Win32.LoadIconW(IntPtr.Zero, Win32.IDI_APPLICATION),
            hCursor = Win32.LoadCursorW(IntPtr.Zero, Win32.IDC_ARROW),
            hbrBackground = (IntPtr)(Win32.COLOR_WINDOW + 1),
            lpszMenuName = null,
            lpszClassName = WindowClass,
        };
        if (Win32.RegisterClassExW(ref wc) == 0)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "注册窗口类失败 (RegisterClassExW)");

        _classRegistered = true;
    }

    /// <summary>窗口过程入口：通过 HWND 找到对应的窗口实例。</summary>
    private static IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
        => _windows.TryGetValue(hwnd, out WindowsWindow? window)
            ? window.OnWndProc(msg, wParam, lParam)
            : Win32.DefWindowProcW(hwnd, msg, wParam, lParam);

    private IntPtr OnWndProc(uint msg, IntPtr wParam, IntPtr lParam)
    {
        switch (msg)
        {
            case Win32.WM_CLOSE:
                Win32.DestroyWindow(_hwnd);
                return IntPtr.Zero;

            case Win32.WM_DESTROY:
                if (_hIcon != IntPtr.Zero)
                {
                    Win32.DestroyIcon(_hIcon);
                    _hIcon = IntPtr.Zero;
                }
                _controller?.Close();
                _controller = null;
                _windows.Remove(_hwnd);
                _closed = true;
                Closed?.Invoke();
                WebWindow.NotifyWindowClosed();
                if (WebWindow.OpenCount == 0)
                    Win32.PostQuitMessage(0); // 最后一个窗口关闭，退出消息循环
                return IntPtr.Zero;

            case Win32.WM_SIZE:
                ResizeWebView();
                return IntPtr.Zero;

            default:
                return Win32.DefWindowProcW(_hwnd, msg, wParam, lParam);
        }
    }

    private void ResizeWebView()
    {
        if (_controller is null)
            return;

        Win32.GetClientRect(_hwnd, out Win32.RECT rc);
        _controller.Bounds = new Rectangle(0, 0, rc.Right, rc.Bottom);
    }

    private async Task InitWebViewAsync()
    {
        try
        {
            _environment = await GetSharedEnvironmentAsync(_options.Scheme, _options.DataScheme);
            if (_closed)
                return;

            _controller = await _environment.CreateCoreWebView2ControllerAsync(_hwnd);
            if (_closed)
            {
                _controller.Close();
                _controller = null;
                return;
            }

            ResizeWebView();

            var core = _controller.CoreWebView2;
            core.Settings.IsStatusBarEnabled = false;
            if (_options.ResourceResolver is not null || _options.DataResolver is not null)
            {
                core.AddWebResourceRequestedFilter($"{_options.Scheme}://*/*", CoreWebView2WebResourceContext.All);
                if (!string.IsNullOrEmpty(_options.DataScheme))
                    core.AddWebResourceRequestedFilter($"{_options.DataScheme}://*/*", CoreWebView2WebResourceContext.All);
                core.WebResourceRequested += OnWebResourceRequested;
            }
            core.Navigate(_options.HomeUrl);

            // Model 双向绑定通道：页面就绪通知 + JS 回传消息
            core.NavigationCompleted += (_, _) => NavigationCompleted?.Invoke();
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
            ShowError($"WebView2 初始化失败：{ex.Message}\n请确认已安装 WebView2 运行时。");
        }
    }

    private static void ShowError(string message) => Log.Debug(message);

    /// <summary>同类 scheme 的窗口共享一个环境；用 Task 缓存避免并发创建多个浏览器进程。</summary>
    private static Task<CoreWebView2Environment> GetSharedEnvironmentAsync(string scheme, string? dataScheme)
    {
        // 环境按 scheme + 数据通道一起键控：同 scheme 不同 DataScheme 的窗口应各自拥有环境
        //（自定义 scheme 注册是环境的构造参数，第一个创建的环境决定了注册内容）。
        var key = scheme + "|" + dataScheme;
        lock (_envLock)
        {
            if (!_environmentTasks.TryGetValue(key, out Task<CoreWebView2Environment>? task))
            {
                // 注册自定义 scheme。注意：此版本的包装器要求通过构造函数传入，
                // new() 后访问 CustomSchemeRegistrations 会是 null。
                var registrations = new List<CoreWebView2CustomSchemeRegistration>
                {
                    new(scheme)
                    {
                        // 允许 app://host/path 这种带 host 的 URL
                        HasAuthorityComponent = true,
                    },
                };
                // 数据通道：同一个环境里再注册一个专用 scheme，托管大块/二进制数据
                if (!string.IsNullOrEmpty(dataScheme)
                    && !string.Equals(dataScheme, scheme, StringComparison.OrdinalIgnoreCase))
                {
                    registrations.Add(new CoreWebView2CustomSchemeRegistration(dataScheme)
                    {
                        HasAuthorityComponent = true,
                    });
                }
                var options = new CoreWebView2EnvironmentOptions(customSchemeRegistrations: registrations);
                task = CoreWebView2Environment.CreateAsync(null, null, options);
                _environmentTasks[key] = task;
            }
            return task;
        }
    }

    private void OnWebResourceRequested(object? sender, CoreWebView2WebResourceRequestedEventArgs args)
    {
        try
        {
            // 数据通道：请求来自 DataScheme 时交给 DataResolver，否则走 UI 资源（ResourceResolver）
            var isData = WebResourceLocator.IsScheme(args.Request.Uri, _options.DataScheme);
            var scheme = isData ? _options.DataScheme! : _options.Scheme;
            var resolver = isData ? _options.DataResolver : _options.ResourceResolver;
            if (isData) Log.Debug($"DATA-REQ {args.Request.Uri}");

            if (resolver is not null && WebResourceLocator.TryResolvePath(args.Request.Uri, scheme, out string? relative, out string? mimeType))
            {
                Stream? stream = resolver(relative!);
                if (isData) Log.Debug($"DATA-HIT {relative} -> {stream?.Length ?? -1}");
                if (stream is not null)
                {
                    // 注意：流交给 WebView2 后由其负责释放，这里不要 Dispose
                    // 缓存策略集中在 ResourceHeaders（Linux/macOS 复用同一决策）：
                    // vite 的 hash 构建产物可 immutable 长缓存，index.html 等未 hash 的入口必须 no-store。
                    args.Response = _environment!.CreateWebResourceResponse(
                        stream,
                        200,
                        "OK",
                        $"Content-Type: {mimeType}\r\nCache-Control: {ResourceHeaders.CacheControl(relative!)}");
                    return;
                }
            }
        }
        catch
        {
            // 读取或构造响应失败时直接放行，WebView2 会显示错误页
        }

        var notFound = new MemoryStream(Encoding.UTF8.GetBytes("404 Not Found"));
        args.Response = _environment!.CreateWebResourceResponse(
            notFound,
            404,
            "Not Found",
            "Content-Type: text/plain\r\nCache-Control: no-store");
    }
}
