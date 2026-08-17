namespace WebWindowUI.Core;

public record SelectDialogOption
{ 
    public string Title { get; set; }
    public string Filter { get; set; }
    public string? InitialDirectory { get; set; }
    public bool SelectMustExist { get; set; }
    public bool AllowMultiSelect { get; set; }
}

public interface IPlatformDialog
{
    /// <summary>
    /// 显示系统消息框。
    /// </summary>
    /// <param name="title">标题。</param>
    /// <param name="message">消息内容。</param>
    /// <param name="error">是否为错误。</param>
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
    List<string>? OpenFileDialog(SelectDialogOption option);

    List<string>? OpenFolderDialog(SelectDialogOption option);

    /// <summary>
    /// 打开系统保存对话框。
    /// </summary>
    /// <param name="title">对话框标题。</param>
    /// <param name="filter">过滤器（Windows 格式；Linux 暂忽略）。</param>
    /// <param name="defaultFileName">文件名编辑框初值，可为 null。</param>
    /// <param name="defaultExt">默认扩展名（不带点），可为 null。</param>
    /// <returns>选中的文件完整路径；用户取消返回 null。</returns>
    string? SaveFileDialog(SelectDialogOption option);
}
