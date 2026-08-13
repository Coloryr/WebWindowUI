using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WebWindowUI.Core;

namespace WebWindowUI.Demo.Todo;

/// <summary>
/// 待办列表模型：演示「typed repeated List&lt;Model&gt; 双向」+「MVVM 命令」+「磁盘持久化」。
/// Items 是 get-only ObservableCollection（免 [ObservableProperty]）：.NET 侧 Add/Remove 原地增删自动推前端，
/// 前端整列表回写经生成器原地清空重建；增删改通过命令触发并持久化到 %LocalAppData%。
/// </summary>
public partial class TodoListModel : WebWindowModel
{
    private readonly string _saveFile;

    /// <summary>
    /// 任务列表：前端强类型 TodoItemModel[]，勾选/增删即整列表回写。
    /// </summary>
    public ObservableCollection<TodoItemModel> Items { get; } = new();

    /// <summary>
    /// 新增输入框（前端 v-model 双向回写）。
    /// </summary>
    [ObservableProperty]
    public partial string NewTitle { get; set; } = "";

    /// <summary>
    /// 保存状态提示（如「已保存 12:03:45（5 项）」）。
    /// </summary>
    [ObservableProperty]
    public partial string Status { get; set; } = "就绪";

    public TodoListModel()
    {
        string dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WebWindowUI.Demo.Todo");
        Directory.CreateDirectory(dir);
        _saveFile = Path.Combine(dir, "todos.json");
        Load();
    }

    /// <summary>
    /// 新增任务：前端 model.addTitle(标题) → 命令 → 加入列表 + 持久化 + 整列自动推送。
    /// </summary>
    [RelayCommand]
    public void AddTitle(string title)
    {
        string t = title.Trim();
        if (t.Length == 0)
            return;
        Items.Add(new TodoItemModel { Title = t, Priority = Items.Count % 3 + 1 });
        NewTitle = "";
        Save();
    }

    /// <summary>
    /// 切换完成状态：前端 model.toggle(全列表下标) → 命令 → 改 Done + 持久化。
    /// </summary>
    [RelayCommand]
    public void Toggle(int index)
    {
        if (index < 0 || index >= Items.Count)
            return;
        Items[index].Done = !Items[index].Done;
        Save();
    }

    /// <summary>
    /// 删除任务：前端 model.remove(全列表下标) → 命令 → 移除 + 持久化。
    /// </summary>
    [RelayCommand]
    public void Remove(int index)
    {
        if (index < 0 || index >= Items.Count)
            return;
        Items.RemoveAt(index);
        Save();
    }

    /// <summary>
    /// 清除已完成：前端 model.clearCompleted() → 命令 → 批量移除 + 持久化。
    /// </summary>
    [RelayCommand]
    public void ClearCompleted()
    {
        for (int i = Items.Count - 1; i >= 0; i--)
        {
            if (Items[i].Done)
                Items.RemoveAt(i);
        }
        Save();
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_saveFile))
                return;
            var dto = JsonSerializer.Deserialize<List<TodoItemDto>>(File.ReadAllText(_saveFile));
            if (dto is null)
                return;
            foreach (var d in dto)
            {
                Items.Add(new TodoItemModel
                {
                    Title = d.Title,
                    Done = d.Done,
                    Priority = d.Priority,
                    CreatedAt = d.CreatedAt,
                });
            }
            Status = $"已加载 {Items.Count} 项";
        }
        catch
        {
            // 损坏/不兼容的存档忽略，从空列表开始
        }
    }

    private void Save()
    {
        try
        {
            var dto = Items
                .Select(i => new TodoItemDto { Title = i.Title, Done = i.Done, Priority = i.Priority, CreatedAt = i.CreatedAt })
                .ToList();
            File.WriteAllText(_saveFile, JsonSerializer.Serialize(dto, new JsonSerializerOptions { WriteIndented = true }));
            Status = $"已保存 {DateTime.Now:HH:mm:ss}（{Items.Count} 项）";
        }
        catch (Exception e)
        {
            Status = $"保存失败：{e.Message}";
        }
    }

    /// <summary>
    /// 持久化 DTO：只落业务字段，避开 WebWindowModel 基类状态。
    /// </summary>
    private sealed class TodoItemDto
    {
        public string Title { get; set; } = "";
        public bool Done { get; set; }
        public int Priority { get; set; }
        public string CreatedAt { get; set; } = "";
    }
}
