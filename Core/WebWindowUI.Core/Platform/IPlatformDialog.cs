namespace WebWindowUI.Core.Platform;

/// <summary>
/// 选择参数
/// </summary>
public record SelectDialogOption
{ 
    /// <summary>
    /// 显示标题
    /// </summary>
    public string Title { get; set; }
    /// <summary>
    /// 过滤
    /// </summary>
    public string Filter { get; set; }
    /// <summary>
    /// 初始路径
    /// </summary>
    public string? InitialDirectory { get; set; }
    /// <summary>
    /// 是否只能选中存在的
    /// </summary>
    public bool SelectMustExist { get; set; }
    /// <summary>
    /// 是否允许多选
    /// </summary>
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
    /// <param name="option"></param>
    /// <returns>选中的文件，取消为Null</returns>
    List<string>? OpenFileDialog(SelectDialogOption option);

    /// <summary>
    /// 打开系统文件夹选择对话框
    /// </summary>
    /// <param name="option"></param>
    /// <returns>选中的路径，取消为Null</returns>
    List<string>? OpenFolderDialog(SelectDialogOption option);

    /// <summary>
    /// 打开系统保存对话框。
    /// </summary>
    /// <param name="option"></param>
    /// <returns>选中的文件，取消为Null</returns>
    string? SaveFileDialog(SelectDialogOption option);
}
