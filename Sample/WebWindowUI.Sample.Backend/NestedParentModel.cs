using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace WebWindowUI.Sample;

/// <summary>
/// 模型嵌套窗口的数据模型：演示「模型里嵌套模型」——Detail 是另一个 WebWindowModel 实例
/// （单 POCO 属性 → descriptor 里是 ModelValue 兜底，序数键下发）。Detail 实例同时可绑到
/// 嵌套详情子窗口（master-detail），子窗口编辑的是同一个实例；子窗口改了 Detail 内部字段时，
/// 这里重推整个 Detail，父窗口展示实时跟随。
/// </summary>
public partial class NestedParentModel : WebWindowModel
{
    /// <summary>父窗口标题（普通字段，双向绑定）。</summary>
    [ObservableProperty]
    public partial string Title { get; set; } = "模型嵌套";

    /// <summary>嵌套模型属性：单 POCO 属性，descriptor 里是 ModelValue 兜底（序数键，只读展示）。</summary>
    [ObservableProperty]
    public partial NestedDetailModel? Detail { get; set; }

    /// <summary>打开嵌套详情子窗口：子窗口绑定同一个 Detail 实例。</summary>
    public event Action? OpenDetailRequested;

    [RelayCommand]
    public void OpenDetail() => OpenDetailRequested?.Invoke();

    // CommunityToolkit 生成 Detail 的 setter 时按固定顺序调用：
    //   OnDetailChanging(value) → field = value → OnDetailChanged(value) → OnPropertyChanged
    // Changing 时字段 detail 还是旧值 → 摘旧订阅；Changed 时已是新值 → 挂新订阅。

    partial void OnDetailChanging(NestedDetailModel? value)
    {
        if (Detail is not null)
            Detail.PropertyChanged -= OnDetailChangedInner;
    }

    partial void OnDetailChanged(NestedDetailModel? value)
    {
        if (value is not null)
            value.PropertyChanged += OnDetailChangedInner;
    }

    private void OnDetailChangedInner(object? sender, PropertyChangedEventArgs e)
        => OnPropertyChanged(nameof(Detail)); // 嵌套模型内部变化 → 整体重推 Detail
}
