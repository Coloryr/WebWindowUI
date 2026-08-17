using WebWindowUI.Core.Platform;
using WebWindowUI.Core.Protocol;

namespace WebWindowUI.Core;

/// <summary>
/// 跨平台的 WebView 窗口基类：构造传「窗口路径」，对应前端 src/window/&lt;窗口路径&gt;/ 页面，首页地址自动推导。
/// </summary>
public abstract class WebWindow
{
    public abstract SystemDecorations SystemDecorations { get; set; }
    public abstract WindowState WindowState { get; set; }
    public abstract Point2I Position { get; set; }
    public abstract Point2I Size { get; set; }
    public abstract Point2I MinSize { get; set; }
    public abstract Point2I MaxSize { get; set; }
    public abstract bool ShowInTaskbar { get; set; }
    public abstract bool CanResize { get; set; }
    public abstract bool CanMinimize { get; set; }
    public abstract bool CanMaximize { get; set; }
    public abstract bool IsDialog { get; set; }
    public abstract bool IsActive { get; set; }
    public abstract Screen Screens { get; }

    /// <summary>
    /// 窗口标题
    /// </summary>
    public abstract string Title { get; set; }

    /// <summary>
    /// 窗口数据模型，与前端 Vue 双向绑定；同一实例可绑多窗口（共享广播），替换/置空模型时解绑。
    /// </summary>
    public abstract WebWindowModel? Model { get; set; }

    internal abstract INativeWindow NativeWindow { get; set; }

    public WebWindowOptions Options { get; init; }

    public event EventHandler? Loaded;

    /// <summary>
    /// 窗口关闭
    /// </summary>
    public event EventHandler? Closed;

    /// <summary>
    /// 窗口关闭
    /// </summary>
    public event EventHandler<Task<bool>>? Closing;

    public event EventHandler<Point2I>? Resize;
    public event EventHandler<Point2I>? Move;
    public event EventHandler<bool> Active;
    public event EventHandler<WindowState> WindowStateChange;
    public event EventHandler<SystemDecorations> SystemDecorationsChange;

    /// <summary>
    /// 按指定选项创建窗口。
    /// </summary>
    /// <param name="options">窗口选项。</param>
    protected WebWindow(WebWindowOptions options)
    {
        Options = options;
        Title = options.Title;
    }

    /// <summary>
    /// 显示窗口
    /// </summary>
    public abstract void Show(WebWindow? Parent = null);

    public abstract void ShowDialog(WebWindow? Parent = null);

    /// <summary>
    /// 隐藏窗口
    /// </summary>
    public abstract void Hide();

    /// <summary>
    /// 关闭窗口
    /// </summary>
    public abstract void Close(object? result);

    /// <summary>
    /// 窗口前台
    /// </summary>
    public abstract void Activate();

    /// <summary>
    /// 设置窗口图标
    /// </summary>
    /// <param name="icon">窗口图标。</param>
    public abstract void SetIcon(WindowIcon? icon);

    /// <summary>
    /// 测试用：在页面里执行 JS 并返回 JSON 结果。
    /// </summary>
    /// <param name="script">要执行的 JS。</param>
    /// <returns>JS 执行结果（JSON 字符串）。</returns>
    internal abstract Task<string> ExecuteScriptAsync(string script);

    /// <summary>
    /// 向页面里的 JS 发送一条消息（protobuf 字节，平台层编码为字符串传输）。
    /// </summary>
    /// <param name="message">protobuf 消息字节。</param>
    internal abstract void PostMessage(byte[] message);

    /// <summary>
    /// 触发页面加载完成事件。
    /// </summary>
    protected void RaiseLoaded() => Loaded?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// 触发窗口关闭事件。
    /// </summary>
    protected void RaiseClosed() => Closed?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// 触发窗口关闭请求（关闭前询问，可取消）。
    /// </summary>
    protected void RaiseClosing()
    {
        if (Closing is not null)
            Closing?.Invoke(this, Task.FromResult(true));
    }

    /// <summary>
    /// 触发窗口尺寸变化事件。
    /// </summary>
    /// <param name="size">新尺寸。</param>
    protected void RaiseResize(Point2I size) => Resize?.Invoke(this, size);

    /// <summary>
    /// 触发窗口位置变化事件。
    /// </summary>
    /// <param name="position">新位置。</param>
    protected void RaiseMove(Point2I position) => Move?.Invoke(this, position);

    /// <summary>
    /// 触发窗口激活状态变化事件。
    /// </summary>
    /// <param name="active">是否激活。</param>
    protected void RaiseActive(bool active) => Active?.Invoke(this, active);

    /// <summary>
    /// 触发窗口状态变化事件。
    /// </summary>
    /// <param name="state">新状态。</param>
    protected void RaiseWindowStateChange(WindowState state) => WindowStateChange?.Invoke(this, state);

    /// <summary>
    /// 触发窗口装饰样式变化事件。
    /// </summary>
    /// <param name="decorations">新装饰样式。</param>
    protected void RaiseSystemDecorationsChange(SystemDecorations decorations)
        => SystemDecorationsChange?.Invoke(this, decorations);

    /// <summary>
    /// 处理前端 postMessage 回传的 protobuf 消息（Ready/Set/Invoke 分派）。
    /// </summary>
    /// <param name="bytes">前端回传的 protobuf 字节。</param>
    internal void OnBackendMessageReceived(byte[] bytes)
    {
        try
        {
            var msg = ModelProtocol.Decode(bytes);
            if (msg is null)
                return;

            // 实例守卫：前端消息必须来自当前绑定实例（0 = 未携带，容忍），防旧实例在途消息串写。
            if (msg.ModelInstanceId != 0 && Model is not null && msg.ModelInstanceId != Model.ModelInstanceId)
                return;

            // 前端桥接就绪：补发初始快照（防快照早于监听器到达而丢失）
            if (msg.Ready is not null && Model is not null)
            {
                PostMessage(Model.BuildSnapshotEnvelope());
                return;
            }

            // 前端双向绑定回写：ModelSet。ElementProperty 非空 = 元素级写回（按 ModelInstanceId 定位元素，保实例）。
            if (msg.Set is not null && Model is { } model)
            {
                if (string.IsNullOrEmpty(msg.Set.ElementProperty))
                {
                    if (model.TrySetProperty(msg.Set.Property, msg.Set.Value))
                        model.BroadcastPropertyUpdate(msg.Set.Property, PostMessage);
                }
                else if (model.TrySetElementProperty(msg.Set.Property, msg.Set.ElementInstanceId, msg.Set.ElementProperty, msg.Set.Value))
                {
                    model.BroadcastElementUpdate(msg.Set.Property, msg.Set.ElementInstanceId, msg.Set.ElementProperty, PostMessage);
                }
            }

            // 前端命令调用：ModelInvoke，执行模型上的 ICommand（[RelayCommand] 源生成）。
            if (msg.Invoke is not null && Model is { } model1)
                model1.TryInvokeCommand(msg.Invoke.CommandId, msg.Invoke.Value);
        }
        catch
        {
            // 无法解析或未知消息，忽略
        }
    }
}
