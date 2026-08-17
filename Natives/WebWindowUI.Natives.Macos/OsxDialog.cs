using WebWindowUI.Core.Platform;

namespace WebWindowUI.Natives.Macos;

/// <summary>
/// macOS 对话框实现（NSAlert / NSOpenPanel / NSSavePanel）。
/// </summary>
public class OsxDialog : IPlatformDialog
{
    /// <summary>
    /// 单例。
    /// </summary>
    public static readonly OsxDialog Dialog = new();

    /// <summary>
    /// 系统弹窗（NSAlert，主线程调用）。
    /// </summary>
    /// <param name="title">标题。</param>
    /// <param name="message">内容。</param>
    /// <param name="error">是否错误样式。</param>
    public void ShowMessageBox(string title, string message, bool error)
    {
        var alert = new NSAlert
        {
            MessageText = title,
            InformativeText = message,
            AlertStyle = error ? NSAlertStyle.Critical : NSAlertStyle.Informational,
        };
        alert.RunModal();
    }

    /// <summary>
    /// 文件选择对话框（NSOpenPanel）。返回 null = 取消。
    /// filter 为 Windows 格式，macOS 暂不支持（忽略）。
    /// </summary>
    /// <param name="option">对话框选项。</param>
    /// <returns>选中的文件路径；取消为 null。</returns>
    public List<string>? OpenFileDialog(SelectDialogOption option)
    {
        var panel = NSOpenPanel.OpenPanel;
        panel.Title = option.Title;
        panel.CanChooseFiles = true;
        panel.CanChooseDirectories = false;
        panel.AllowsMultipleSelection = option.AllowMultiSelect;
        if (option.InitialDirectory is not null)
            panel.DirectoryUrl = NSUrl.FromFilename(option.InitialDirectory);
        if (panel.RunModal() != 1) // NSModalResponseOK = 1
            return null;
        var urls = panel.Urls;
        var result = new List<string>(urls.Length);
        for (int i = 0; i < urls.Length; i++)
            result.Add(urls[i].Path!);
        return result;
    }

    /// <summary>
    /// 目录选择对话框（NSOpenPanel，仅目录）。返回 null = 取消。
    /// </summary>
    /// <param name="option">对话框选项。</param>
    /// <returns>选中的目录路径；取消为 null。</returns>
    public List<string>? OpenFolderDialog(SelectDialogOption option)
    {
        var panel = NSOpenPanel.OpenPanel;
        panel.Title = option.Title;
        panel.CanChooseFiles = false;
        panel.CanChooseDirectories = true;
        panel.AllowsMultipleSelection = false;
        if (option.InitialDirectory is not null)
            panel.DirectoryUrl = NSUrl.FromFilename(option.InitialDirectory);
        if (panel.RunModal() != 1) // NSModalResponseOK = 1
            return null;
        return [panel.Url?.Path!];
    }

    /// <summary>
    /// 保存对话框（NSSavePanel）。返回 null = 取消。
    /// filter 为 Windows 格式，macOS 暂不支持（忽略）。
    /// </summary>
    /// <param name="option">对话框选项。</param>
    /// <returns>选择的保存路径；取消为 null。</returns>
    public string? SaveFileDialog(SelectDialogOption option)
    {
        var panel = NSSavePanel.SavePanel;
        panel.Title = option.Title;
        if (panel.RunModal() != 1) // NSModalResponseOK = 1
            return null;
        return panel.Url?.Path;
    }
}
