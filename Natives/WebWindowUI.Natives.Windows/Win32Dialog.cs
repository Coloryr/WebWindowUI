using System.Runtime.InteropServices;
using WebWindowUI.Core;

namespace WebWindowUI.Natives.Windows;

public class Win32Dialog : IPlatformDialog
{
    public static readonly Win32Dialog Dialog = new();

    /// <summary>
    /// 显示系统消息框。
    /// </summary>
    /// <param name="title">标题。</param>
    /// <param name="message">内容。</param>
    /// <param name="error">是否错误图标。</param>
    public void ShowMessageBox(string title, string message, bool error)
    {
        Win32.MessageBoxW(IntPtr.Zero, message, title, error ? Win32.MB_ICONERROR : Win32.MB_ICONINFORMATION);
    }

    /// <summary>
    /// 打开系统文件选择对话框（OFN_EXPLORER）。
    /// 返回 null = 用户取消；否则为选中的文件完整路径列表（可能为空列表）。
    /// </summary>
    /// <param name="title">对话框标题。</param>
    /// <param name="filter">过滤器，如 "所有文件 (*.*)\0*.*\0文本文件 (*.txt)\0*.txt\0"（末尾 \0 由封送器补成双 NUL）。</param>
    /// <param name="initialDirectory">基础路径（初始目录，null = 系统默认）。</param>
    /// <param name="fileMustExist">是否为文件：true = 只能选已存在的文件（OFN_FILEMUSTEXIST）。</param>
    /// <param name="allowMultiSelect">是否允许多选（OFN_ALLOWMULTISELECT）。</param>
    public List<string>? OpenFileDialog(
        string title,
        string filter,
        string? initialDirectory = null,
        bool fileMustExist = true,
        bool allowMultiSelect = true)
    {
        uint flags = Win32.OFN_EXPLORER | Win32.OFN_PATHMUSTEXIST;
        if (fileMustExist)
            flags |= Win32.OFN_FILEMUSTEXIST;
        if (allowMultiSelect)
            flags |= Win32.OFN_ALLOWMULTISELECT;

        var ofn = new Win32.OPENFILENAME
        {
            lStructSize = Marshal.SizeOf<Win32.OPENFILENAME>(),
            lpstrTitle = title,
            lpstrFilter = filter,
            lpstrInitialDir = initialDirectory,
            nMaxFile = allowMultiSelect ? Win32.OFN_MULTISELECT_BUFFER : Win32.OFN_SINGLE_SELECT_BUFFER,
            Flags = (int)flags,
        };

        if (!Win32.GetOpenFileNameW(ref ofn))
            return null; // 用户取消

        string raw = ofn.lpstrFile ?? "";
        string[] parts = raw.Split('\0');
        if (parts.Length == 1)
            return [raw]; // 单选：完整路径

        // 多选：parts[0] = 目录，其后为各文件名
        var files = new List<string>(parts.Length - 1);
        for (int i = 1; i < parts.Length; i++)
        {
            if (parts[i].Length > 0)
                files.Add(Path.Combine(parts[0], parts[i]));
        }
        return files;
    }

    /// <summary>
    /// 打开系统保存对话框（OFN_OVERWRITEPROMPT）。单选。
    /// 返回 null = 用户取消；否则为选中的文件完整路径。
    /// </summary>
    /// <param name="title">对话框标题。</param>
    /// <param name="filter">过滤器，格式同 <see cref="OpenFileDialog"/>。</param>
    /// <param name="defaultFileName">文件名编辑框初值（可为 null）。</param>
    /// <param name="defaultExt">用户未输入扩展名时自动补的扩展名（不带点，可为 null）。</param>
    public string? SaveFileDialog(string title, string filter, string? defaultFileName = null, string? defaultExt = null)
    {
        var ofn = new Win32.OPENFILENAME
        {
            lStructSize = Marshal.SizeOf<Win32.OPENFILENAME>(),
            lpstrTitle = title,
            lpstrFilter = filter,
            lpstrFile = defaultFileName,
            nMaxFile = 260,
            lpstrDefExt = defaultExt,
            Flags = (int)(Win32.OFN_OVERWRITEPROMPT | Win32.OFN_HIDEREADONLY | Win32.OFN_PATHMUSTEXIST),
        };

        if (!Win32.GetSaveFileNameW(ref ofn))
            return null; // 用户取消

        // 单选返回普通 NUL 结尾路径（无 OFN_ALLOWMULTISELECT，无内嵌 NUL）
        return ofn.lpstrFile;
    }
}
