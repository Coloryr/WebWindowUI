using WebWindowUI.Core.Platform;

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
    /// <param name="option">对话框选项（Filter/SelectMustExist 暂不支持，忽略）。</param>
    /// <returns>选中的文件路径；取消为 null。</returns>
    public List<string>? OpenFileDialog(SelectDialogOption option)
        => GtkNative.OpenFileDialog(option.Title, option.InitialDirectory, option.AllowMultiSelect) is { } files ? [.. files] : null;

    /// <summary>
    /// 打开目录选择对话框（单选）。
    /// </summary>
    /// <param name="option">对话框选项（Filter/SelectMustExist/AllowMultiSelect 暂不支持，忽略）。</param>
    /// <returns>选中的目录路径；取消为 null。</returns>
    public List<string>? OpenFolderDialog(SelectDialogOption option)
        => GtkNative.OpenFolderDialog(option.Title, option.InitialDirectory) is { } dir ? [dir] : null;

    /// <summary>
    /// 打开保存对话框。
    /// </summary>
    /// <param name="option">对话框选项（Filter 暂不支持，忽略）。</param>
    /// <returns>选择的保存路径；取消为 null。</returns>
    public string? SaveFileDialog(SelectDialogOption option)
        => GtkNative.SaveFileDialog(option.Title, null);
}
