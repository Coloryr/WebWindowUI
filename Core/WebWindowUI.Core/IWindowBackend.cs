namespace WebWindowUI.Core;

/// <summary>
/// 平台窗口的行为契约，由各平台的窗口实现（WindowsWindow 等）提供。
/// </summary>
public interface IWindowBackend
{
    /// <summary>
    /// 显示窗口并初始化对应的 webview。
    /// </summary>
    void Show();

    /// <summary>
    /// 隐藏窗口（不关闭、不销毁）。
    /// </summary>
    void Hide();

    /// <summary>
    /// 关闭窗口。关闭最后一个窗口后，平台的消息循环自动退出。
    /// </summary>
    void Close();

    /// <summary>
    /// 把窗口带到前台并聚焦（激活）。
    /// </summary>
    void Activate();

    /// <summary>
    /// 修改窗口标题。
    /// </summary>
    /// <param name="title">新标题。</param>
    void SetTitle(string title);

    /// <summary>
    /// 设置窗口图标（标题栏与任务栏）。
    /// </summary>
    /// <param name="icon">窗口图标。</param>
    void SetIcon(WindowIcon icon);

    /// <summary>
    /// 向页面里的 JS 发送一条消息（protobuf 字节，平台层编码为字符串传输）。
    /// </summary>
    /// <param name="message">protobuf 消息字节。</param>
    void PostMessage(byte[] message);

    /// <summary>
    /// 在页面里执行一段 JavaScript，返回其结果（JSON 字符串）。用于宿主主动读取/驱动页面。
    /// </summary>
    /// <param name="script">要执行的 JS。</param>
    /// <returns>执行结果（JSON 字符串）。</returns>
    Task<string> ExecuteScriptAsync(string script);

    /// <summary>
    /// 页面导航完成时触发（用于在页面就绪后推送 Model 初始快照）。
    /// </summary>
    event Action? NavigationCompleted;

    /// <summary>
    /// 窗口销毁时触发（用户关闭或宿主调用 Close()）。用于宿主清理打开状态等。
    /// </summary>
    event Action? Closed;

    /// <summary>
    /// 页面里的 JS 通过 postMessage 回传的消息（protobuf 字节）。
    /// </summary>
    event Action<byte[]>? MessageReceived;
}
