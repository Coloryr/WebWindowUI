using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using WebWindowUI.Core;
using WebWindowUI.Core.Observable;
using WebWindowUI.Sample.Items;

namespace WebWindowUI.Sample;

/// <summary>
/// 列表嵌套窗口数据模型：Items 为 List&lt;NestedListItemModel&gt;（元素又是嵌套模型），
/// Counts 演示 ObservableDictionary。元素变更重推整个 Items；Items 是显式 get-only 集合属性
/// （前端整列回写经生成器原地清空重建），Counts 演示字典原地增删自动推送。
/// </summary>
public partial class NestedListModel : WebWindowModel
{
    /// <summary>
    /// 窗口标题（普通字段，双向绑定）。
    /// </summary>
    [ObservableProperty]
    public partial string Title { get; set; } = "List<>嵌套";

    /// <summary>
    /// 元素列表：typed repeated（List&lt;NestedListItemModel&gt;）。显式 get-only 属性：前端整列回写
    /// 经生成器原地清空重建（保留实例与订阅）。
    /// </summary>
    public ObservableCollection<NestedListItemModel> Items { get; } = [];

    /// <summary>
    /// ObservableDictionary（字典原地增删自动推送）：.NET 侧原地改自动推前端，前端原地改回写 .NET。
    /// </summary>
    public ObservableDictionary<string, int> Counts { get; } = new()
    {
        ["items"] = 3,
        ["tags"] = 4,
    };

    /// <summary>
    /// 打开列表项详情子窗口：携带被点元素的索引。
    /// </summary>
    public event Action<int>? OpenItemRequested;

    [RelayCommand]
    public void OpenItem(int index) => OpenItemRequested?.Invoke(index);

    /// <summary>
    /// 把指定统计项 +1（演示 .NET 侧原地改字典自动推前端）。
    /// </summary>
    [RelayCommand]
    public void Bump(string key)
    {
        if (Counts.TryGetValue(key, out int value))
            Counts[key] = value + 1;
    }

    public NestedListModel()
    {
        Items.CollectionChanged += (_, _) => RebindItemListeners();
        RebindItemListeners();
    }

    /// <summary>
    /// 订阅每个元素（含其内层 Tags 集合）变更 → 重推整个 Items。先摘后挂保证同一 handler 只挂一次；
    /// 元素被整体替换后按当前 Items 重挂，已移除元素残留订阅是无害孤立引用。
    /// </summary>
    private void RebindItemListeners()
    {
        foreach (NestedListItemModel item in Items)
        {
            item.PropertyChanged -= OnItemChanged;
            item.Tags.CollectionChanged -= OnItemChanged;
            item.PropertyChanged += OnItemChanged;
            item.Tags.CollectionChanged += OnItemChanged;
        }
    }

    /// <summary>
    /// 子窗口（或 .NET 侧）改了元素 → 重推 Items（元素内部 Tags 增删也经同一 handler 推送）。
    /// </summary>
    private void OnItemChanged(object? sender, EventArgs e) => OnPropertyChanged(nameof(Items));
}
