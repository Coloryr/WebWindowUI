using WebWindowUI.Core;
using WebWindowUI.Sample.Items;

namespace WebWindowUI.Sample;

/// <summary>
/// 列表嵌套窗口（前端 src/window/nested-list/）：演示「List&lt;Model&gt; 嵌套 + 列表项详情子窗口」。
/// 前端 OpenItem(index) 命令 → 打开绑定父列表同一元素实例的 NestedListItemWindow（master-detail）；
/// 子窗口编辑后父窗口列表实时跟随。
/// </summary>
internal sealed class NestedListWindow : WebWindow
{
    private readonly NestedListModel _model;
    private readonly Dictionary<NestedListItemModel, NestedListItemWindow> _detailWindows = [];

    public NestedListWindow() : base(new WebWindowOptions("nested-list")
    {
        Title = "List<>嵌套窗口",
        Width = 860,
        Height = 660
    })
    {
        _model = new NestedListModel
        {
            Title = "List<>嵌套示例",
            Items =
            {
                new NestedListItemModel
                {
                    Title = "设计评审",
                    Priority = 1,
                    Meta = new NestedItemMetaModel { Author = "张三", Note = "评审待办拆分" },
                    Tags = { new NestedItemTagModel { Name = "核心" }, new NestedItemTagModel { Name = "待定" } },
                },
                new NestedListItemModel
                {
                    Title = "代码审查",
                    Priority = 2,
                    Meta = new NestedItemMetaModel { Author = "李四", Note = "重点看写回路径" },
                    Tags = { new NestedItemTagModel { Name = "后端" } },
                },
                new NestedListItemModel { Title = "文档整理", Priority = 3 },
            },
        };
        Model = _model;
        _model.OpenItemRequested += OnOpenItem;
        Closed += () => _model.OpenItemRequested -= OnOpenItem;
    }

    private void OnOpenItem(int index)
    {
        if (index < 0 || index >= _model.Items.Count)
            return;

        NestedListItemModel item = _model.Items[index];
        if (_detailWindows.TryGetValue(item, out NestedListItemWindow? win))
        {
            win.Show();
            win.Activate();
            return;
        }

        // 绑定父列表里的同一个元素实例（master-detail）；关闭后移除记录，下次可重建。
        NestedListItemWindow created = new(item);
        created.Closed += () => _detailWindows.Remove(item);
        _detailWindows[item] = created;
        created.Show();
        created.Activate();
    }
}
