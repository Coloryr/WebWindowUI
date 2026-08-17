using WebWindowUI.Core;

namespace WebWindowUI.Natives.Linux;

/// <summary>
/// GTK 对话框实现（包装 internal <see cref="GtkNative"/> 的原生对话框）。
/// </summary>
public class GtkDialog : IPlatformDialog
{
    /// <summary>
    /// 单例。
    /// </summary>
    public static readonly GtkDialog Dialog = new();

    /// <summary>
    /// 显示系统消息框（GTK 无错误图标区分，error 忽略）。
    /// </summary>
    /// <param name="title">标题。</param>
    /// <param name="message">内容。</param>
    /// <param name="error">是否错误样式（忽略）。</param>
    public void ShowMessageBox(string title, string message, bool error)
        => GtkNative.ShowMessageBox(title, message);

    /// <summary>
    /// 打开文件选择对话框。
    /// </summary>
    /// <param name="title">标题。</param>
    /// <param name="filter">过滤器（GTK 暂不支持，忽略）。</param>
    /// <param name="initialDirectory">初始目录。</param>
    /// <param name="fileMustExist">是否要求文件存在（GTK 暂不支持，忽略）。</param>
    /// <param name="allowMultiSelect">是否允许多选。</param>
    /// <returns>选中的文件路径；取消为 null。</returns>
    public List<string>? OpenFileDialog(string title, string filter, string? initialDirectory = null, bool fileMustExist = true, bool allowMultiSelect = true)
        => GtkNative.OpenFileDialog(title, initialDirectory, allowMultiSelect) is { } files ? [.. files] : null;

    /// <summary>
    /// 打开保存对话框。
    /// </summary>
    /// <param name="title">标题。</param>
    /// <param name="filter">过滤器（GTK 暂不支持，忽略）。</param>
    /// <param name="defaultFileName">默认文件名。</param>
    /// <param name="defaultExt">默认扩展名（GTK 暂不支持，忽略）。</param>
    /// <returns>选择的保存路径；取消为 null。</returns>
    public string? SaveFileDialog(string title, string filter, string? defaultFileName = null, string? defaultExt = null)
        => GtkNative.SaveFileDialog(title, defaultFileName);
}
