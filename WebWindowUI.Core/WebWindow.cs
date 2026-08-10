namespace WebWindowUI;

/// <summary>
/// 跨平台的 WebView 窗口基类。平台实现由入口包 <c>WebWindowUI.Platform.EnsureRegistered()</c>
/// 静态注册进 <see cref="WebWindowPlatform"/>（Windows 用 WebView2、Linux 用 WebKit2GTK、macOS 用 WKWebView），
/// 调用方不需要接触任何平台（Win32 等）API。
///
/// 窗口模式：每个窗口继承 <see cref="WebWindow"/>，构造时传入「窗口路径」——
/// 该路径对应前端 Vue 工程 src/window/&lt;窗口路径&gt;/ 下的一个页面，
/// 首页地址会自动推导为 scheme://localhost/window/&lt;窗口路径&gt;/index.html。
/// </summary>
public abstract class WebWindow
{
    private readonly IWindowBackend _backend;
    private string _title;
    private WebWindowModel? _model;
    private bool _pageLoaded;
    private static int _openCount;

    /// <summary>用默认选项创建窗口（scheme=app，资源由内置 WebResourceResolver 提供）。窗口路径对应前端 src/window/&lt;窗口路径&gt; 页面。</summary>
    protected WebWindow(string windowPath, string title, int width = 1280, int height = 800)
        : this(windowPath, title, new WebWindowOptions(), width, height)
    {
    }

    /// <summary>按指定选项创建窗口（scheme、资源提供者等），尚未显示。</summary>
    protected WebWindow(string windowPath, string title, WebWindowOptions options, int width = 1280, int height = 800)
    {
        WindowPath = NormalizeWindowPath(windowPath);
        options ??= new WebWindowOptions();
        options.HomeUrl = BuildHomeUrl(options.Scheme, WindowPath);
        _backend = WebWindowPlatform.Current.CreateWindow(title, options, width, height);
        _backend.NavigationCompleted += OnBackendNavigationCompleted;
        _backend.NavigationCompleted += () => NavigationCompleted?.Invoke();
        _backend.Closed += () => Closed?.Invoke();
        _backend.MessageReceived += OnBackendMessageReceived;
        _title = title;
        NotifyWindowOpened();
    }

    /// <summary>该窗口对应的窗口路径（前端 src/window/&lt;窗口路径&gt; 的目录名）。</summary>
    public string WindowPath { get; }

    /// <summary>当前打开的窗口数量。</summary>
    public static int OpenCount => _openCount;

    /// <summary>窗口标题。修改后立即生效（同步到平台窗口标题栏）。</summary>
    public string Title
    {
        get => _title;
        set
        {
            _title = value;
            _backend.SetTitle(value);
        }
    }

    /// <summary>显示窗口并初始化 webview。</summary>
    public void Show() => _backend.Show();

    /// <summary>隐藏窗口（不关闭、不销毁）。</summary>
    public void Hide() => _backend.Hide();

    /// <summary>测试用：镜像底层导航完成事件（WebWindow 内部用于推快照，不对外暴露）。</summary>
    internal event Action? NavigationCompleted;

    /// <summary>窗口销毁时触发（用户关闭标题栏或调用 <see cref="Close"/>）。宿主可据此清理打开状态。</summary>
    public event Action? Closed;

    /// <summary>测试用：在页面里执行 JS 并返回 JSON 结果。</summary>
    internal Task<string> ExecuteScriptAsync(string script) => _backend.ExecuteScriptAsync(script);

    /// <summary>关闭窗口。关闭最后一个窗口后，平台的消息循环自动退出。</summary>
    public void Close() => _backend.Close();

    /// <summary>把窗口带到前台并聚焦（激活）。</summary>
    public void Activate() => _backend.Activate();

    /// <summary>设置窗口图标（标题栏与任务栏）。</summary>
    public void SetIcon(WindowIcon icon) => _backend.SetIcon(icon);

    /// <summary>
    /// 窗口数据模型（如 MainWindowModel），与前端 Vue 双向绑定：
    /// 单属性变化自动推送 ModelUpdate（protobuf），页面加载完成时推送完整快照
    /// （生成器产出的 MainWindowModel 消息或通用 ModelSnapshot），
    /// 前端回传 ModelSet 时写回属性。
    /// 同一模型实例可绑到多个窗口（共享广播）：本窗口经 SubscribePushed 订阅本窗口的推送，
    /// 替换/置空模型时解绑；远程回写应用后排除源窗口广播给其余绑定窗口。
    /// </summary>
    public WebWindowModel? Model
    {
        get => _model;
        set
        {
            if (ReferenceEquals(_model, value))
                return;

            _model?.UnsubscribePushed(_backend.PostMessage);

            _model = value;
            if (_model is not null)
            {
                _model.SubscribePushed(_backend.PostMessage);
                if (_pageLoaded) // 页面已就绪（如运行中更换模型）立即补发快照
                    _backend.PostMessage(_model.BuildSnapshotEnvelope());
            }
        }
    }

    private void OnBackendNavigationCompleted()
    {
        _pageLoaded = true;
        // 初始快照不在此推：导航完成（Finished）时页面模块脚本可能尚未执行，wwuiReceive 未定义，
        // 推送必失败（Linux/WebKit 的 Finished 早于模块脚本执行，实测 TypeError）。桥就绪后页面发 Ready，
        // 由 Ready 路径（见 OnBackendMessageReceived）统一补发快照——这是唯一可靠的就绪信号。
    }

    private void OnBackendMessageReceived(byte[] bytes)
    {
        try
        {
            WebMessage? msg = ModelProtocol.Decode(bytes);
            if (msg is null)
                return;

            // 前端桥接就绪：补发初始快照（防止快照早于页面监听器到达而丢失）
            if (msg.Ready is not null && _model is not null)
            {
                _backend.PostMessage(_model.BuildSnapshotEnvelope());
                return;
            }

            // 前端双向绑定回写：ModelSet { property, value }。应用成功后把结果广播给
            // 除源窗口外的其它绑定窗口（共享同一模型实例时跨窗口同步；单窗口模型排除后无人接收）。
            if (msg.Set is not null && _model is not null)
            {
                if (_model.TrySetProperty(msg.Set.Property, msg.Set.Value))
                    _model.BroadcastPropertyUpdate(msg.Set.Property, _backend.PostMessage);
            }

            // 前端命令调用：ModelInvoke { command, value }。执行模型上的 ICommand
            // （[RelayCommand] 源生成）；命令方法里的属性变化照常走增量推送（不在回写抑制期间）。
            if (msg.Invoke is not null && _model is not null)
                _model.TryInvokeCommand(msg.Invoke.Command, msg.Invoke.Value);
        }
        catch
        {
            // 无法解析或未知消息，忽略
        }
    }

    /// <summary>运行当前平台的消息循环，直到所有窗口关闭后返回。</summary>
    public static void RunMessageLoop() => WebWindowPlatform.Current.RunMessageLoop();

    /// <summary>
    /// 由 scheme 与窗口路径推导首页地址：scheme://localhost/window/&lt;窗口路径&gt;/index.html。
    /// 窗口页面统一放在 wwwroot 的 window/ 文件夹下（与前端 src/window/ 对应）。
    /// </summary>
    internal static string BuildHomeUrl(string scheme, string windowPath)
    {
        string p = NormalizeWindowPath(windowPath);
        return $"{scheme}://localhost/window/{(p.Length == 0 ? "" : p + "/")}index.html";
    }

    private static string NormalizeWindowPath(string windowPath)
        => (windowPath ?? "").Trim().Trim('/');

    internal static void NotifyWindowOpened() => Interlocked.Increment(ref _openCount);
    internal static void NotifyWindowClosed() => Interlocked.Decrement(ref _openCount);
}
