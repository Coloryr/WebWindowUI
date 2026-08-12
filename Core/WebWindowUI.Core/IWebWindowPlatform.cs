namespace WebWindowUI.Core;

/// <summary>
/// 一个操作系统平台的 WebWindow 实现（Windows / Linux / macOS）。
/// </summary>
public interface IWebWindowPlatform
{
    /// <summary>
    /// 创建一个尚未显示的窗口后端。
    /// </summary>
    IWindowBackend CreateWindow(WebWindowOptions options);

    /// <summary>
    /// 运行平台的消息循环，直到所有窗口关闭后返回。
    /// </summary>
    void RunMessageLoop();
}
