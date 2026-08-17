using UniformTypeIdentifiers;
using WebWindowUI.Core;

namespace WebWindowUI.Platforms.MacOS;

/// <summary>
/// macOS 对话框实现（NSAlert / NSOpenPanel / NSSavePanel）。
/// </summary>
public class MacOSDialog : IPlatformDialog
{
    /// <summary>
    /// 单例。
    /// </summary>
    public static readonly MacOSDialog Dialog = new();

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
    /// <param name="title">标题。</param>
    /// <param name="filter">过滤器（不支持，忽略）。</param>
    /// <param name="initialDirectory">初始目录。</param>
    /// <param name="fileMustExist">是否要求文件存在（不支持，忽略）。</param>
    /// <param name="allowMultiSelect">是否允许多选。</param>
    /// <returns>选中的文件路径；取消为 null。</returns>
    public List<string>? OpenFileDialog(string title, string filter, string? initialDirectory = null, bool fileMustExist = true, bool allowMultiSelect = true)
    {
        var panel = NSOpenPanel.OpenPanel;
        panel.Title = title;
        panel.CanChooseFiles = true;
        panel.CanChooseDirectories = false;
        panel.AllowsMultipleSelection = allowMultiSelect;
        if (initialDirectory is not null)
            panel.DirectoryUrl = NSUrl.FromFilename(initialDirectory);
        if (panel.RunModal() != 1) // NSModalResponseOK = 1
            return null;
        var urls = panel.Urls;
        var result = new List<string>(urls.Length);
        for (int i = 0; i < urls.Length; i++)
            result.Add(urls[i].Path!);
        return result;
    }

    /// <summary>
    /// 保存对话框（NSSavePanel）。返回 null = 取消。
    /// filter 为 Windows 格式，macOS 暂不支持（忽略）。
    /// </summary>
    /// <param name="title">标题。</param>
    /// <param name="filter">过滤器（不支持，忽略）。</param>
    /// <param name="defaultFileName">默认文件名。</param>
    /// <param name="defaultExt">默认扩展名。</param>
    /// <returns>选择的保存路径；取消为 null。</returns>
    public string? SaveFileDialog(string title, string filter, string? defaultFileName = null, string? defaultExt = null)
    {
        var panel = NSSavePanel.SavePanel;
        panel.Title = title;
        if (defaultFileName is not null)
            panel.NameFieldStringValue = defaultFileName;
        if (defaultExt is not null)
            panel.AllowedContentTypes = new[] { UTType.CreateFromExtension(defaultExt)! };
        if (panel.RunModal() != 1) // NSModalResponseOK = 1
            return null;
        return panel.Url?.Path;
    }
}
