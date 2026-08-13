using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WebWindowUI.Core;

namespace WebWindowUI.Demo.SharedNotes;

/// <summary>
/// 共享便签本模型：同一个实例同时绑定多个窗口（多订阅者广播）。
/// 任一窗口发送/删除经命令走 .NET，广播到所有订阅者；改动源窗口不重复接收（远程回写排除源），其余窗口实时跟随。
/// </summary>
public partial class NotesModel : WebWindowModel
{
    /// <summary>
    /// get-only ObservableCollection（免 [ObservableProperty]），原地增删自动推送整列表。
    /// </summary>
    public ObservableCollection<NoteModel> Notes { get; } = new();

    /// <summary>
    /// 与各窗口输入框双向绑定；发送后由命令清空并推回。
    /// </summary>
    [ObservableProperty]
    public partial string Input { get; set; } = "";

    [ObservableProperty]
    public partial string Status { get; set; } = "就绪";

    [ObservableProperty]
    public partial int Total { get; set; }

    /// <summary>
    /// 发送便签：把 Input 追加进 Notes（同模型多窗口即时广播），随后清空输入。
    /// </summary>
    [RelayCommand]
    public void Send()
    {
        string text = Input.Trim();
        if (text.Length == 0) return;

        Notes.Add(new NoteModel
        {
            Author = "共享便签",
            Text = text,
            Time = DateTime.Now.ToString("HH:mm:ss"),
        });
        Input = "";
        Total = Notes.Count;
        Status = $"已发送（共 {Notes.Count} 条）";
    }

    /// <summary>
    /// 按序数键删除指定位置的便签（typed repeated 双向：前端传 index）。
    /// </summary>
    [RelayCommand]
    public void Remove(int index)
    {
        if (index < 0 || index >= Notes.Count) return;
        Notes.RemoveAt(index);
        Total = Notes.Count;
        Status = $"已删除（剩 {Notes.Count} 条）";
    }
}
