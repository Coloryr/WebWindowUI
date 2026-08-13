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

    /// <summary>
    /// 在UI线程中运行
    /// </summary>
    /// <param name="action"></param>
    void RunOnUiThread(Action action);
    /// <summary>
    /// 是否在UI线程中
    /// </summary>
    /// <returns></returns>
    bool IsUiThread();
    /// <summary>
    /// 显示一个系统弹窗
    /// </summary>
    /// <param name="title">标题</param>
    /// <param name="message">消息</param>
    /// <param name="error">错误</param>
    void ShowMessageBox(string title, string message, bool error);

    /// <summary>
    /// 打开系统文件选择对话框。
    /// </summary>
    /// <param name="title">对话框标题。</param>
    /// <param name="filter">过滤器（Windows 格式 "描述\0*.ext\0"；Linux 暂忽略）。</param>
    /// <param name="initialDirectory">初始目录，可为 null。</param>
    /// <param name="fileMustExist">是否只能选择已存在的文件。</param>
    /// <param name="allowMultiSelect">是否允许多选。</param>
    /// <returns>选中的文件完整路径数组；用户取消返回 null。</returns>
    string[]? OpenFileDialog(string title, string filter, string? initialDirectory = null, bool fileMustExist = true, bool allowMultiSelect = true);

    /// <summary>
    /// 打开系统保存对话框。
    /// </summary>
    /// <param name="title">对话框标题。</param>
    /// <param name="filter">过滤器（Windows 格式；Linux 暂忽略）。</param>
    /// <param name="defaultFileName">文件名编辑框初值，可为 null。</param>
    /// <param name="defaultExt">默认扩展名（不带点），可为 null。</param>
    /// <returns>选中的文件完整路径；用户取消返回 null。</returns>
    string? SaveFileDialog(string title, string filter, string? defaultFileName = null, string? defaultExt = null);
}
