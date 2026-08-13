using WebWindowUI.Core.Protocol;

namespace WebWindowUI.Core;

/// <summary>
/// 跨平台的 WebView 窗口基类。平台实现由 <c>WebWindowUI.Platform.EnsureRegistered()</c>
/// 注册进 <see cref="WebWindowPlatform"/>（Windows=WebView2 / Linux=WebKit2GTK / macOS=WKWebView）。
/// 子类构造时传「窗口路径」，对应前端 src/window/&lt;窗口路径&gt;/ 页面，
/// 首页地址自动推导为 scheme://localhost/window/&lt;窗口路径&gt;/index.html。
/// </summary>
public abstract class WebWindow
{
    private readonly IWindowBackend _backend;
    private WebWindowModel? _model;
    private bool _pageLoaded;

    /// <summary>
    /// 按指定选项创建窗口
    /// </summary>
    protected WebWindow(WebWindowOptions options)
    {
        _backend = WebWindowPlatform.Current.CreateWindow(options);
        _backend.NavigationCompleted += OnBackendNavigationCompleted;
        _backend.NavigationCompleted += () => NavigationCompleted?.Invoke();
        _backend.Closed += () => Closed?.Invoke();
        _backend.MessageReceived += OnBackendMessageReceived;
        Title = options.Title;
    }

    /// <summary>
    /// 窗口标题。修改后立即生效（同步到平台窗口标题栏）。
    /// </summary>
    public string Title
    {
        get;
        set
        {
            field = value;
            _backend.SetTitle(value);
        }
    }

    /// <summary>
    /// 显示窗口并初始化 webview。
    /// </summary>
    public void Show() => _backend.Show();

    /// <summary>
    /// 隐藏窗口（不关闭、不销毁）。
    /// </summary>
    public void Hide() => _backend.Hide();

    /// <summary>
    /// 测试用：镜像底层导航完成事件（WebWindow 内部用于推快照，不对外暴露）。
    /// </summary>
    internal event Action? NavigationCompleted;

    /// <summary>
    /// 窗口销毁时触发（用户关闭标题栏或调用 <see cref="Close"/>）。宿主可据此清理打开状态。
    /// </summary>
    public event Action? Closed;

    /// <summary>
    /// 测试用：在页面里执行 JS 并返回 JSON 结果。
    /// </summary>
    internal Task<string> ExecuteScriptAsync(string script) => _backend.ExecuteScriptAsync(script);

    /// <summary>
    /// 关闭窗口。关闭最后一个窗口后，平台的消息循环自动退出。
    /// </summary>
    public void Close() => _backend.Close();

    /// <summary>
    /// 把窗口带到前台并聚焦（激活）。
    /// </summary>
    public void Activate() => _backend.Activate();

    /// <summary>
    /// 设置窗口图标（标题栏与任务栏）。
    /// </summary>
    public void SetIcon(WindowIcon icon) => _backend.SetIcon(icon);

    /// <summary>
    /// 窗口数据模型，与前端 Vue 双向绑定：属性变化推送增量、页面加载推完整快照、
    /// 前端回传 ModelSet 写回属性。同一实例可绑多窗口（共享广播），替换/置空模型时解绑。
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
        // 初始快照不在导航完成时推（页面脚本可能尚未就绪），由前端 Ready 信号统一补发。
    }

    private void OnBackendMessageReceived(byte[] bytes)
    {
        try
        {
            var msg = ModelProtocol.Decode(bytes);
            if (msg is null)
                return;

            // 实例守卫：前端消息必须来自当前绑定实例（0 = 未携带，容忍），防旧实例在途消息串写。
            if (msg.ModelInstanceId != 0 && _model is not null && msg.ModelInstanceId != _model.ModelInstanceId)
                return;

            // 前端桥接就绪：补发初始快照（防快照早于监听器到达而丢失）
            if (msg.Ready is not null && _model is not null)
            {
                _backend.PostMessage(_model.BuildSnapshotEnvelope());
                return;
            }

            // 前端双向绑定回写：ModelSet。ElementProperty 非空 = 集合元素级写回（按 ModelInstanceId 定位元素、
            // 只改该元素属性，保实例），否则旧整属性行为。应用成功后广播给其它绑定窗口（跨窗口同步）。
            if (msg.Set is not null && _model is not null)
            {
                if (string.IsNullOrEmpty(msg.Set.ElementProperty))
                {
                    if (_model.TrySetProperty(msg.Set.Property, msg.Set.Value))
                        _model.BroadcastPropertyUpdate(msg.Set.Property, _backend.PostMessage);
                }
                else if (_model.TrySetElementProperty(msg.Set.Property, msg.Set.ElementInstanceId, msg.Set.ElementProperty, msg.Set.Value))
                {
                    _model.BroadcastElementUpdate(msg.Set.Property, msg.Set.ElementInstanceId, msg.Set.ElementProperty, _backend.PostMessage);
                }
            }

            // 前端命令调用：ModelInvoke，执行模型上的 ICommand（[RelayCommand] 源生成）。
            if (msg.Invoke is not null && _model is not null)
                _model.TryInvokeCommand(msg.Invoke.CommandId, msg.Invoke.Value);
        }
        catch
        {
            // 无法解析或未知消息，忽略
        }
    }
}
