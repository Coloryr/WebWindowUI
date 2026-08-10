using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using WebWindowUI.Core.Protocol;

namespace WebWindowUI.Core;

/// <summary>
/// 窗口数据模型的基类。继承后即可绑定到 <see cref="WebWindow.Model"/>，
/// 与前端（Vue）做双向绑定：
///
/// - 同一模型实例可同时绑定到多个窗口（多订阅者广播）：各窗口经
///   <see cref="SubscribePushed"/> 订阅，属性变化全量广播给所有绑定窗口；
///   前端回写（ModelSet）应用后经 <see cref="BroadcastPropertyUpdate"/> 排除源窗口广播，
///   其余窗口同步——共享模型跨窗口联动，独立实例互不干扰。
/// - 单属性值变化（如 [ObservableProperty] 生成的可写属性）时，自动把增量消息推送给 WebView 前端。
///   增量载荷由生成器为每个模型单独产出的 update 消息（如 MainWindowModelUpdate）编码，
///   只有被修改的字段会出现在载荷里；没有生成 update 编码器的模型不推送增量（只发完整快照）；
/// - 页面加载完成时推送完整快照：优先用生成器产出的完整模型消息
///   （由 MainWindowModelProto 之类的生成代码 override <see cref="ModelId"/>/
///   <see cref="EncodeFullSnapshot"/>），否则回退到通用 ModelSnapshot（property → ModelValue）；
/// - 前端回传 ModelSet { property, value } 时会写回对应属性。
///
/// 复杂值经 <see cref="ModelProtocol.ToModelValue"/> 递归展开并做环检测，
/// 不合格（自引用等）直接抛 InvalidOperationException。
/// </summary>
public abstract partial class WebWindowModel : ObservableObject
{
    protected WebWindowModel()
    {
        PropertyChanged += OnModelPropertyChanged;
    }

    /// <summary>已绑定窗口的推送订阅（多窗口共享同一模型实例时各窗口各一条）。入参为 protobuf 信封字节。</summary>
    private readonly List<Action<byte[]>> _pushed = [];

    /// <summary>绑定窗口的推送回调（WebWindow.Model setter 调用）。重复订阅去重。</summary>
    internal void SubscribePushed(Action<byte[]> handler)
    {
        lock (_pushed)
        {
            if (!_pushed.Contains(handler))
                _pushed.Add(handler);
        }
    }

    /// <summary>解绑窗口的推送回调（WebWindow.Model setter 替换/置空模型时调用）。
    /// 最后一个窗口解绑后模型不再被任何窗口引用，自动退订集合订阅（防外部数据层集合的事件留住模型，见 #5）。</summary>
    internal void UnsubscribePushed(Action<byte[]> handler)
    {
        bool last;
        lock (_pushed)
        {
            _pushed.Remove(handler);
            last = _pushed.Count == 0;
        }
        if (last)
            UnbindCollections();
    }

    /// <summary>解除全部绑定（模型实例生命周期结束时由宿主调用）：清空推送订阅 + 退订全部集合。</summary>
    internal void Unbind()
    {
        lock (_pushed)
            _pushed.Clear();
        UnbindCollections();
    }

    /// <summary>退订全部集合属性的 CollectionChanged（模型不再被引用时防泄漏；重绑时由
    /// <see cref="EnsureCollectionSubscribed"/> 重新挂接）。</summary>
    private void UnbindCollections()
    {
        foreach (var kv in _collectionSubs)
            kv.Value.CollectionChanged -= OnCollectionChanged;
        _collectionSubs.Clear();
    }

    /// <summary>正在应用前端回写（TrySetProperty 期间）。置位时 PropertyChanged 不回传，避免回声消息。</summary>
    private bool _isApplyingRemoteWrite;

    /// <summary>向全部绑定窗口推送信封。单订阅者快路径（免 ToArray 分配）；无订阅者直接返回（空转短路）。</summary>
    private void PushEnvelope(byte[] bytes)
    {
        Action<byte[]>[]? snapshot = null;
        lock (_pushed)
        {
            if (_pushed.Count == 0)
                return;
            if (_pushed.Count == 1)
            {
                _pushed[0](bytes); // 单订阅者：免 ToArray（Monitor 可重入，PostMessage handler 不回锁 _pushed）
                return;
            }
            snapshot = [.. _pushed];
        }
        foreach (var handler in snapshot)
            handler(bytes);
    }

    /// <summary>向除 exclude 外的全部绑定窗口推送信封（远程回写后的跨窗口同步；exclude = 源窗口）。</summary>
    private void PushEnvelope(byte[] bytes, Action<byte[]> exclude)
    {
        Action<byte[]>[]? snapshot = null;
        lock (_pushed)
        {
            if (_pushed.Count == 0)
                return;
            if (_pushed.Count == 1)
            {
                if (_pushed[0] != exclude)
                    _pushed[0](bytes);
                return;
            }
            snapshot = [.. _pushed];
        }
        foreach (var handler in snapshot)
            if (handler != exclude)
                handler(bytes);
    }

    /// <summary>
    /// 模型序号（生成器烘焙：完整消息名 FNV-1a 哈希）。线缆上代替冗长的消息名——ModelUpdate/
    /// GeneratedModel 都只发 <see cref="ModelId"/>，前端经生成器烘焙进 descriptor/TS 的
    /// __protocol 校验并解码。0 表示没有生成编码器，完整快照回退到通用 ModelSnapshot、
    /// 属性变化不推送增量。
    /// </summary>
    protected virtual int ModelId => 0;

    /// <summary>把整个模型序列化为生成消息的 protobuf 字节（仅当 <see cref="ModelId"/> 非 0 时调用）。</summary>
    protected virtual byte[] EncodeFullSnapshot()
        => throw new NotSupportedException($"模型 {GetType().Name} 未由 WebWindowUI.Generator 生成完整模型编码器。");

    /// <summary>
    /// 把单个属性变化编码成增量 update 消息的 protobuf 字节（仅当 <see cref="ModelId"/> 非 0 时调用）。
    /// 生成代码按属性名 set 对应字段，载荷里只包含被修改的字段。
    /// </summary>
    protected virtual byte[] EncodePropertyUpdate(string propertyName, object? value)
        => throw new NotSupportedException($"模型 {GetType().Name} 未由 WebWindowUI.Generator 生成增量 update 编码器。");

    /// <summary>
    /// 源生成器产出的「前端回写属性」钩子：命中返回 true（值已写入）。未命中返回 false
    /// （属性不在生成 switch 中——模型未接分析器或属性非公开；不再反射兜底）。
    /// </summary>
    protected virtual bool TrySetGeneratedProperty(string name, ModelValue? value) => false;

    /// <summary>
    /// 源生成器产出的「命令调用」钩子：commandId = 命令序号（[RelayCommand] 方法声明序，与前端
    /// TS 镜像烘焙的调用序号一致）。命中返回 true（命令已执行或已被 CanExecute 门控拒绝）；
    /// 未命中返回 false（命令未由生成器收集——非 [RelayCommand] 方法；不再反射兜底）。
    /// </summary>
    protected virtual bool TryInvokeGeneratedCommand(int commandId, ModelValue? value) => false;

    /// <summary>源生成器产出的「按名读值」钩子：命中返回 true 并输出属性现值；未命中返回 false（不再反射兜底）。</summary>
    protected virtual bool TryGetGeneratedProperty(string name, out object? value) { value = null; return false; }

    /// <summary>源生成器产出的集合订阅：对每个 INotifyCollectionChanged 属性 EnsureCollectionSubscribed。</summary>
    protected virtual void SubscribeGeneratedCollections() { }

    /// <summary>以前端回写语义应用属性 setter（期间抑制 PropertyChanged 回声；供生成代码调用）。</summary>
    protected void ApplyRemoteWrite(Action setter)
    {
        _isApplyingRemoteWrite = true;
        try
        {
            setter();
        }
        finally
        {
            _isApplyingRemoteWrite = false;
        }
    }

    private void OnModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is null)
            return;

        // 生成代码按名读值（无反射）；未命中（模型未接分析器/属性非公开）直接跳过订阅与推送。
        if (!TryGetGeneratedProperty(e.PropertyName, out object? value))
            return;

        // 集合属性被替换（.NET 代码或前端回写重建 ObservableCollection）时切换 CollectionChanged 订阅。
        // 须在回声抑制之前执行——回写期间也要把新实例挂上，否则后续 .Add() 静默丢失。
        EnsureCollectionSubscribed(e.PropertyName, value);

        // 前端回写引起的属性变化不再回传：值本身就来自前端，再推送一条 update 是冗余回声。
        if (_isApplyingRemoteWrite)
            return;

        // 增量更新走生成器为模型单独产出的 update 消息（只编码被修改的字段）；
        // 未生成 update 编码器的模型不推送增量。
        if (ModelId == 0)
            return;

        PushEnvelope(BuildUpdateEnvelope(e.PropertyName, value));
    }

    /// <summary>集合属性（ObservableCollection 等 INotifyCollectionChanged）→ 当前订阅实例。</summary>
    private readonly Dictionary<string, INotifyCollectionChanged> _collectionSubs = [];

    /// <summary>
    /// 挂接全部集合属性的 CollectionChanged。字段初始化器（<c>todos = new()</c>）在基类构造
    /// **之后**才执行，基类 ctor 扫描看不到已初始化的集合，须在首次推送/快照时武装；
    /// 之后集合实例被替换由 <see cref="OnModelPropertyChanged"/> 切换订阅。供 WebWindow 绑定
    /// 与单元测试调用。生成代码直接按属性名挂接（无反射）。
    /// </summary>
    internal void ArmCollectionSubscriptions()
        => SubscribeGeneratedCollections();

    /// <summary>确保集合属性的 CollectionChanged 挂到当前实例；值非集合或换了实例时切换订阅。</summary>
    protected void EnsureCollectionSubscribed(string propertyName, object? value)
    {
        if (value is not INotifyCollectionChanged coll)
        {
            if (_collectionSubs.Remove(propertyName, out INotifyCollectionChanged? old))
                old.CollectionChanged -= OnCollectionChanged;
            return;
        }
        if (_collectionSubs.TryGetValue(propertyName, out INotifyCollectionChanged? cur) && ReferenceEquals(cur, coll))
            return;
        cur?.CollectionChanged -= OnCollectionChanged;
        coll.CollectionChanged += OnCollectionChanged;
        _collectionSubs[propertyName] = coll;
    }

    /// <summary>
    /// ObservableCollection 增删（.Add/.Remove/.Clear/.Insert…）→ 差量补丁推送（#3）：
    /// 按事件 Action 编码 CollectionPatch（Insert/Remove/Replace/Move），前端对响应式数组原地 splice——
    /// 比整列表增量省流量、免整列重建。Reset 事件不带新旧元素，无法差量编码 → 回退整列表补丁
    /// （Items 承载全量，前端整体替换）。补丁自包含（property + action + items），不依赖
    /// ModelId；远程回写期间（_isApplyingRemoteWrite）不推送。
    /// </summary>
    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_isApplyingRemoteWrite)
            return;

        foreach (var kv in _collectionSubs)
        {
            if (!ReferenceEquals(kv.Value, sender))
                continue;
            if (sender is IDictionary)
            {
                // ObservableDictionary 等字典：键值语义无索引差量，原地改（dict[k]=v / Add / Remove / Clear）
                // → 整属性重推（复用增量 update 消息，ModelValue 对象 map 整体替换前端对象）。
                if (ModelId == 0)
                    return;
                if (TryGetGeneratedProperty(kv.Key, out object? value))
                    PushEnvelope(BuildUpdateEnvelope(kv.Key, value));
                return;
            }
            if (e.Action == NotifyCollectionChangedAction.Reset)
            {
                // Reset 不带新旧元素，无法差量编码——回退整列表补丁（重读属性取全量）。
                if (TryGetGeneratedProperty(kv.Key, out object? value))
                    PushEnvelope(BuildPatchEnvelope(kv.Key, CollectionPatchAction.Reset, value));
                return;
            }
            PushEnvelope(BuildPatchEnvelope(kv.Key, e));
            return;
        }
    }

    /// <summary>把集合变更事件编码成差量补丁信封（Insert/Remove/Replace/Move）。</summary>
    private static byte[] BuildPatchEnvelope(string propertyName, NotifyCollectionChangedEventArgs e)
    {
        var patch = new CollectionPatch { Property = propertyName };
        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add:
                patch.Action = CollectionPatchAction.Insert;
                patch.Index = e.NewStartingIndex;
                foreach (var item in e.NewItems!)
                    patch.Items.Add(ModelProtocol.ToModelValue(item));
                break;
            case NotifyCollectionChangedAction.Remove:
                patch.Action = CollectionPatchAction.Remove;
                patch.Index = e.OldStartingIndex;
                patch.Count = e.OldItems!.Count;
                break;
            case NotifyCollectionChangedAction.Replace:
                patch.Action = CollectionPatchAction.Replace;
                patch.Index = e.NewStartingIndex;
                patch.Count = e.OldItems!.Count;
                foreach (var item in e.NewItems!)
                    patch.Items.Add(ModelProtocol.ToModelValue(item));
                break;
            case NotifyCollectionChangedAction.Move:
                patch.Action = CollectionPatchAction.Move;
                patch.Index = e.NewStartingIndex;
                patch.FromIndex = e.OldStartingIndex;
                patch.Count = e.OldItems!.Count;
                break;
        }
        return ModelProtocol.Encode(new WebMessage { Patch = patch });
    }

    /// <summary>把集合整体编码成 Reset 补丁信封（Items 承载全量，前端整体替换）。</summary>
    private static byte[] BuildPatchEnvelope(string propertyName, CollectionPatchAction action, object? value)
    {
        var patch = new CollectionPatch { Property = propertyName, Action = action };
        if (value is IEnumerable enumerable)
        {
            foreach (var item in enumerable)
                patch.Items.Add(ModelProtocol.ToModelValue(item));
        }
        return ModelProtocol.Encode(new WebMessage { Patch = patch });
    }

    /// <summary>把单个属性编码成增量 update 信封（本地属性变化 / 远程回写后广播共用）。</summary>
    private byte[] BuildUpdateEnvelope(string propertyName, object? value)
    {
        var payload = EncodePropertyUpdate(propertyName, value);
        var msg = new WebMessage
        {
            Update = new ModelUpdate { ModelId = ModelId, Payload = payload },
        };
        return ModelProtocol.Encode(msg);
    }

    /// <summary>
    /// 远程回写（前端 ModelSet）应用成功后，把结果广播给除源窗口外的所有绑定窗口，
    /// 让共享同一模型实例的多窗口保持同步。单窗口模型的订阅者唯一 = 源窗口 → 排除后无人接收（等价不回显）。
    /// </summary>
    internal void BroadcastPropertyUpdate(string propertyName, Action<byte[]> exclude)
    {
        if (ModelId == 0)
            return;
        // 读值走生成代码（无反射）；未命中（属性非生成/非公开）不广播。
        if (TryGetGeneratedProperty(propertyName, out object? value))
            PushEnvelope(BuildUpdateEnvelope(propertyName, value), exclude);
    }

    /// <summary>生成完整快照信封（页面加载完成 / 前端就绪时发送）。</summary>
    internal byte[] BuildSnapshotEnvelope()
    {
        // 首次推送前武装集合订阅（字段初始化器在基类构造后执行，基类 ctor 看不到）。
        ArmCollectionSubscriptions();

        var msg = new WebMessage();
        if (ModelId != 0)
        {
            msg.Full = new GeneratedModel { ModelId = ModelId, Payload = EncodeFullSnapshot() };
        }
        else
        {
            msg.Snapshot = BuildGenericSnapshot();
        }
        return ModelProtocol.Encode(msg);
    }

    /// <summary>通用完整快照：property → ModelValue（无生成编码器时的回退）。</summary>
    private ModelSnapshot BuildGenericSnapshot()
    {
        var snapshot = new ModelSnapshot();
        foreach (var prop in GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!prop.CanRead || prop.GetIndexParameters().Length > 0)
                continue;
            snapshot.Data[prop.Name] = ModelProtocol.ToModelValue(prop.GetValue(this));
        }
        return snapshot;
    }

    /// <summary>
    /// 执行前端发来的命令调用（ModelInvoke { commandId, value }，MVVM Command）。
    /// commandId 为命令序号（[RelayCommand] 方法声明序），由生成代码直接命中
    /// 「命令名 + Command」的 ICommand（如 OpenWindowCommand）并执行：有参命令按方法参数类型
    /// 转换，无参命令按 object 透传，CanExecute 不满足时拒绝执行（MVVM 门控，如
    /// [RelayCommand(CanExecute = ...)]）。未由生成器收集的命令返回 false（不抛异常）。
    /// 命令方法内部的属性变化照常走增量推送（Invoke 不在回写抑制期间）。
    /// </summary>
    internal bool TryInvokeCommand(int commandId, ModelValue? value)
        => TryInvokeGeneratedCommand(commandId, value);

    /// <summary>前端回传的属性写入：由生成代码按名写回（无反射）。找不到可写属性或值类型不匹配时返回 false（不抛异常）。</summary>
    internal bool TrySetProperty(string name, ModelValue? value)
        => TrySetGeneratedProperty(name, value);
}
