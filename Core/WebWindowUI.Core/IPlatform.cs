namespace WebWindowUI.Core;

/// <summary>
/// 一个操作系统平台的 WebWindow 实现（Windows / Linux / macOS）。
/// </summary>
public interface IPlatform
{
    /// <summary>
    /// 平台对话框（消息框/文件选择/保存）。
    /// </summary>
    IPlatformDialog Dialog { get; }

    /// <summary>
    /// 初始化
    /// </summary>
    void Init(string[] args);
    /// <summary>
    /// 创建一个尚未显示的窗口后端。
    /// </summary>
    IWindowBackend CreateWindow(WebWindowOptions options);

    /// <summary>
    /// 运行平台的消息循环，直到所有窗口关闭后返回。
    /// </summary>
    void RunMessageLoop();

    /// <summary>
    /// 在 UI 线程中运行委托。
    /// </summary>
    /// <param name="action">要在 UI 线程执行的委托。</param>
    void RunOnUiThread(Action action);
    /// <summary>
    /// 是否在 UI 线程中。
    /// </summary>
    /// <returns>是否在 UI 线程。</returns>
    bool IsUiThread();

}
