using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WebWindowUI.Sample.Items;
using WebWindowUI.Core;
using WebWindowUI.Core.Observable;

namespace WebWindowUI.Sample;

/// <summary>
/// 列表嵌套窗口的数据模型（示例「List&lt;&gt;嵌套窗口」）：Items 是 List&lt;NestedListItemModel&gt;，
/// 每个元素又是嵌套模型（内部有 List&lt;NestedItemTagModel&gt; 与单模型 NestedItemMetaModel）。
/// OpenItem(index) 命令打开绑定 Items[index] 元素的列表项详情子窗口（master-detail）。
///
/// 子窗口编辑的是父列表里的同一个元素实例：元素 PropertyChanged（含其内层 Tags 集合变化）时，
/// 这里重推整个 Items，父窗口列表实时跟随子窗口的修改。
///
/// 集合写法演示：
/// - Items：显式 get-only 属性（不加 [ObservableProperty]）——生成器照常收集（字段号/快照/集合订阅），
///   前端整列回写经生成器 TrySet 原地清空重建（保留实例与订阅），双向照常。
/// - Counts：ObservableDictionary（WebWindowUI 核心库提供）——.NET 侧原地改（dict[k]=v / Add / Remove）
///   抛 CollectionChanged → 框架整属性重推前端；前端原地改经深 watch 整字典 name 键回写 .NET。
/// </summary>
public partial class NestedListModel : WebWindowModel
{
    /// <summary>
    /// 窗口标题（普通字段，双向绑定）。
    /// </summary>
    [ObservableProperty]
    public partial string Title { get; set; } = "List<>嵌套";

    /// <summary>元素列表：typed repeated（List&lt;NestedListItemModel&gt;），前端强类型数组。
    /// 显式 get-only 属性（不加 [ObservableProperty]）：前端整列回写经生成器原地清空重建。</summary>
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

    /// <summary>订阅每个元素（含其内层 Tags 集合）的变更 → 重推整个 Items，父窗口列表实时同步。
    /// 先摘后挂保证同一 handler 只挂一次（CollectionChanged 每次都会重跑本方法）；元素可能被整体
    /// 替换（子窗口整列回写重建集合），这里按当前 Items 重挂订阅；已移除元素残留的订阅指向上一次
    /// 集合，是无害的孤立引用。</summary>
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
