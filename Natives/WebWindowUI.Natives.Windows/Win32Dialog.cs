using System.Runtime.InteropServices;
using WebWindowUI.Core.Platform;

namespace WebWindowUI.Natives.Windows;

/// <summary>
/// Win32 对话框实现（消息框/文件选择/目录选择/保存）。
/// </summary>
public class Win32Dialog : IPlatformDialog
{
    /// <summary>
    /// 单例。
    /// </summary>
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
    /// <param name="option">对话框选项。</param>
    /// <returns>选中的文件路径；取消为 null。</returns>
    public List<string>? OpenFileDialog(SelectDialogOption option)
    {
        uint flags = Win32.OFN_EXPLORER | Win32.OFN_PATHMUSTEXIST;
        if (option.SelectMustExist)
            flags |= Win32.OFN_FILEMUSTEXIST;
        if (option.AllowMultiSelect)
            flags |= Win32.OFN_ALLOWMULTISELECT;

        var ofn = new Win32.OPENFILENAME
        {
            lStructSize = Marshal.SizeOf<Win32.OPENFILENAME>(),
            lpstrTitle = option.Title,
            lpstrFilter = option.Filter,
            lpstrInitialDir = option.InitialDirectory,
            nMaxFile = option.AllowMultiSelect ? Win32.OFN_MULTISELECT_BUFFER : Win32.OFN_SINGLE_SELECT_BUFFER,
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
    /// 打开系统文件夹选择对话框（SHBrowseForFolderW，BIF_NEWDIALOGSTYLE）。
    /// 返回 null = 用户取消；否则为选中的目录完整路径。
    /// </summary>
    /// <param name="option">对话框选项（AllowMultiSelect 忽略——系统对话框单选）。</param>
    /// <returns>选中的目录路径；取消为 null。</returns>
    public List<string>? OpenFolderDialog(SelectDialogOption option)
    {
        var bi = new Win32.BROWSEINFOW
        {
            lpszTitle = option.Title,
            ulFlags = Win32.BIF_RETURNONLYFSDIRS | Win32.BIF_NEWDIALOGSTYLE,
        };
        IntPtr pidl = Win32.SHBrowseForFolderW(ref bi);
        if (pidl == IntPtr.Zero)
            return null; // 用户取消

        try
        {
            IntPtr pathBuffer = Marshal.AllocHGlobal(Win32.OFN_SINGLE_SELECT_BUFFER * sizeof(char));
            try
            {
                if (!Win32.SHGetPathFromIDListW(pidl, pathBuffer))
                    return null;
                return [Marshal.PtrToStringUni(pathBuffer)!];
            }
            finally
            {
                Marshal.FreeHGlobal(pathBuffer);
            }
        }
        finally
        {
            Win32.CoTaskMemFree(pidl);
        }
    }

    /// <summary>
    /// 打开系统保存对话框（OFN_OVERWRITEPROMPT）。单选。
    /// 返回 null = 用户取消；否则为选中的文件完整路径。
    /// </summary>
    /// <param name="option">对话框选项（默认文件名不在选项里，从 Filter 提取首个扩展名作 defExt）。</param>
    /// <returns>选中的文件路径；取消为 null。</returns>
    public string? SaveFileDialog(SelectDialogOption option)
    {
        var ofn = new Win32.OPENFILENAME
        {
            lStructSize = Marshal.SizeOf<Win32.OPENFILENAME>(),
            lpstrTitle = option.Title,
            lpstrFilter = option.Filter,
            nMaxFile = Win32.OFN_SINGLE_SELECT_BUFFER,
            lpstrDefExt = ExtractDefaultExt(option.Filter),
            Flags = (int)(Win32.OFN_OVERWRITEPROMPT | Win32.OFN_HIDEREADONLY | Win32.OFN_PATHMUSTEXIST),
        };

        if (!Win32.GetSaveFileNameW(ref ofn))
            return null; // 用户取消

        // 单选返回普通 NUL 结尾路径（无 OFN_ALLOWMULTISELECT，无内嵌 NUL）
        return ofn.lpstrFile;
    }

    /// <summary>
    /// 从过滤器（"描述\0*.ext\0"）提取首个扩展名（不带点），供保存对话框自动补扩展名。
    /// </summary>
    /// <param name="filter">Windows 格式过滤器。</param>
    /// <returns>扩展名；无则 null。</returns>
    private static string? ExtractDefaultExt(string? filter)
    {
        if (string.IsNullOrEmpty(filter))
            return null;
        foreach (var pattern in filter.Split('\0'))
        {
            var star = pattern.IndexOf('*');
            if (star < 0)
                continue;
            var ext = pattern[(star + 1)..].TrimStart('.');
            if (ext.Length > 0 && !ext.Contains('*') && !ext.Contains('?'))
                return ext;
        }
        return null;
    }
}
