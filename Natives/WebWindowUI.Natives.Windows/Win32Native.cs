using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using static WebWindowUI.Natives.Windows.Win32;

namespace WebWindowUI.Natives.Windows;

public static class Win32Native
{
    public static void ShowMessage(string title, string message, bool error)
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
    public static List<string>? OpenFileDialog(
        string title,
        string filter,
        string? initialDirectory = null,
        bool fileMustExist = true,
        bool allowMultiSelect = true)
    {
        uint flags = OFN_EXPLORER | OFN_PATHMUSTEXIST;
        if (fileMustExist)
            flags |= OFN_FILEMUSTEXIST;
        if (allowMultiSelect)
            flags |= OFN_ALLOWMULTISELECT;

        var ofn = new OPENFILENAME
        {
            lStructSize = Marshal.SizeOf<OPENFILENAME>(),
            lpstrTitle = title,
            lpstrFilter = filter,
            lpstrInitialDir = initialDirectory,
            nMaxFile = allowMultiSelect ? OFN_MULTISELECT_BUFFER : OFN_SINGLE_SELECT_BUFFER,
            Flags = (int)flags,
        };

        if (!GetOpenFileNameW(ref ofn))
            return null; // 用户取消

        string raw = ofn.lpstrFile ?? "";
        string[] parts = raw.Split('\0');
        if (parts.Length == 1)
            return new List<string> { raw }; // 单选：完整路径

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
    public static string? SaveFileDialog(string title, string filter, string? defaultFileName = null, string? defaultExt = null)
    {
        var ofn = new OPENFILENAME
        {
            lStructSize = Marshal.SizeOf<OPENFILENAME>(),
            lpstrTitle = title,
            lpstrFilter = filter,
            lpstrFile = defaultFileName,
            nMaxFile = 260,
            lpstrDefExt = defaultExt,
            Flags = (int)(OFN_OVERWRITEPROMPT | OFN_HIDEREADONLY | OFN_PATHMUSTEXIST),
        };

        if (!GetSaveFileNameW(ref ofn))
            return null; // 用户取消

        // 单选返回普通 NUL 结尾路径（无 OFN_ALLOWMULTISELECT，无内嵌 NUL）
        return ofn.lpstrFile;
    }
}
