namespace WebWindowUI.Core.Platform;

/// <summary>
/// 平台原生窗口抽象：只含「窗口平台相关」能力（句柄生命周期 + 窗口状态/装饰），
/// 不含任何 WebView 内容。各平台 WebWindow 壳持有一个原生窗口并委托窗口状态。
/// </summary>
public interface INativeWindow
{
    /// <summary>
    /// 窗口销毁时触发。
    /// </summary>
    event Action? Destory;

    /// <summary>
    /// 窗口尺寸变化时触发（可经 GetSize 读新尺寸）。
    /// </summary>
    event Action? Resize;

    /// <summary>
    /// 窗口位置变化时触发。
    /// </summary>
    event Action<Point2I>? Move;

    /// <summary>
    /// 窗口激活/失活时触发。
    /// </summary>
    event Action<bool>? Active;

    /// <summary>
    /// 窗口状态（最小化/最大化/全屏等）变化时触发。
    /// </summary>
    event Action<WindowState>? WindowStateChange;

    /// <summary>
    /// 窗口装饰样式变化时触发。
    /// </summary>
    event Action<SystemDecorations>? SystemDecorationsChange;

    /// <summary>
    /// 创建窗口托盘
    /// </summary>
    /// <returns></returns>
    ITrayIcon CreateTrayIcon(string name);

    /// <summary>
    /// 平台窗口句柄。
    /// </summary>
    IntPtr WindowHandle { get; }

    /// <summary>
    /// 显示窗口。
    /// </summary>
    void Show();

    /// <summary>
    /// 隐藏窗口（不销毁）。
    /// </summary>
    void Hide();

    /// <summary>
    /// 关闭窗口。
    /// </summary>
    void Close();

    /// <summary>
    /// 激活/聚焦窗口。
    /// </summary>
    void Activate();

    /// <summary>
    /// 修改标题。
    /// </summary>
    /// <param name="title">新标题。</param>
    void SetTitle(string title);

    /// <summary>
    /// 设置图标。
    /// </summary>
    /// <param name="icon">窗口图标。</param>
    void SetIcon(WindowIcon icon);

    /// <summary>
    /// 获取客户区尺寸。
    /// </summary>
    /// <returns>客户区尺寸。</returns>
    Point2I GetSize();

    /// <summary>
    /// 窗口装饰样式（None 无边框/Border 标题栏/Full 全装饰）。
    /// </summary>
    SystemDecorations SystemDecorations { get; set; }

    /// <summary>
    /// 窗口状态（Normal/Minimize/Maximize/Full/FullBorderLess）。
    /// </summary>
    WindowState WindowState { get; set; }

    /// <summary>
    /// 窗口位置（屏幕坐标，左上角）。
    /// </summary>
    Point2I Position { get; set; }

    /// <summary>
    /// 窗口尺寸（客户区，与 GetSize 一致）。
    /// </summary>
    Point2I Size { get; set; }

    /// <summary>
    /// 最小尺寸（0 表示不限制）。
    /// </summary>
    Point2I MinSize { get; set; }

    /// <summary>
    /// 最大尺寸（0 表示不限制）。
    /// </summary>
    Point2I MaxSize { get; set; }

    /// <summary>
    /// 是否显示在任务栏。
    /// </summary>
    bool ShowInTaskbar { get; set; }

    /// <summary>
    /// 是否可调整大小。
    /// </summary>
    bool CanResize { get; set; }

    /// <summary>
    /// 是否可最小化。
    /// </summary>
    bool CanMinimize { get; set; }

    /// <summary>
    /// 是否可最大化。
    /// </summary>
    bool CanMaximize { get; set; }

    /// <summary>
    /// 是否对话框式窗口。
    /// </summary>
    bool IsDialog { get; set; }

    /// <summary>
    /// 窗口当前是否活动（系统状态推导，只读）。
    /// </summary>
    bool IsActive { get; }

    /// <summary>
    /// 窗口所在显示器（主屏回退）。
    /// </summary>
    Screen Screens { get; }
}
