using CommunityToolkit.Mvvm.ComponentModel;

namespace WebWindowUI.Demo.SharedNotes;

/// <summary>
/// 一张便签：作者 + 内容 + 时间。作为 NotesModel.Notes 的元素（typed repeated，列表元素双向）。
/// </summary>
public partial class NoteModel : WebWindowModel
{
    [ObservableProperty]
    public partial string Author { get; set; } = "";

    [ObservableProperty]
    public partial string Text { get; set; } = "";

    [ObservableProperty]
    public partial string Time { get; set; } = "";
}
