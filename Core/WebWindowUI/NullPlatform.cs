using WebWindowUI.Core;

namespace WebWindowUI;

/// <summary>
/// 未注册平台时的兜底实现（所有成员抛 NotImplementedException）。
/// </summary>
public class NullPlatform : IWebWindowPlatform
{
    /// <summary>
    /// 构造兜底平台（抛未实现异常）。
    /// </summary>
    public NullPlatform()
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// 创建窗口后端（未实现）。
    /// </summary>
    /// <param name="options">窗口选项。</param>
    /// <returns>窗口后端。</returns>
    public IWindowBackend CreateWindow(WebWindowOptions options)
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// 是否在 UI 线程（未实现）。
    /// </summary>
    /// <returns>是否在 UI 线程。</returns>
    public bool IsUiThread()
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// 打开文件对话框（未实现）。
    /// </summary>
    /// <param name="title">标题。</param>
    /// <param name="filter">过滤器。</param>
    /// <param name="initialDirectory">初始目录。</param>
    /// <param name="fileMustExist">是否要求文件存在。</param>
    /// <param name="allowMultiSelect">是否允许多选。</param>
    /// <returns>选中的文件路径。</returns>
    public string[]? OpenFileDialog(string title, string filter, string? initialDirectory = null, bool fileMustExist = true, bool allowMultiSelect = true)
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// 运行消息循环（未实现）。
    /// </summary>
    public void RunMessageLoop()
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// 在 UI 线程运行委托（未实现）。
    /// </summary>
    /// <param name="action">委托。</param>
    public void RunOnUiThread(Action action)
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// 打开保存对话框（未实现）。
    /// </summary>
    /// <param name="title">标题。</param>
    /// <param name="filter">过滤器。</param>
    /// <param name="defaultFileName">默认文件名。</param>
    /// <param name="defaultExt">默认扩展名。</param>
    /// <returns>选中的文件路径。</returns>
    public string? SaveFileDialog(string title, string filter, string? defaultFileName = null, string? defaultExt = null)
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// 显示系统消息框（未实现）。
    /// </summary>
    /// <param name="title">标题。</param>
    /// <param name="message">消息。</param>
    /// <param name="error">是否错误。</param>
    public void ShowMessageBox(string title, string message, bool error)
    {
        throw new NotImplementedException();
    }
}
