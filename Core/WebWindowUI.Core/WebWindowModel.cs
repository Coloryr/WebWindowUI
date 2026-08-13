using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Reflection;
using WebWindowUI.Core.Protocol;

namespace WebWindowUI.Core;

/// <summary>
/// 窗口数据模型的基类。继承后绑定到 <see cref="WebWindow.Model"/>，与前端 Vue 双向绑定：
/// 属性变化自动推送增量、页面加载完成推完整快照、前端回写（ModelSet）写回属性。
/// 同一实例可绑多个窗口（共享广播），远程回写后排除源窗口广播，其余窗口同步。
/// 复杂值经 <see cref="ModelProtocol.ToModelValue"/> 递归展开并做环检测。
/// </summary>
public abstract partial class WebWindowModel : ObservableObject
{
    protected WebWindowModel()
    {
        PropertyChanged += OnModelPropertyChanged;
    }

    /// <summary>
    /// 实例唯一ID分配计数器
    /// </summary>
    private static long _nextModelInstanceId;

    /// <summary>
    /// 实例唯一ID
    /// </summary>
    public long ModelInstanceId { get; } = Interlocked.Increment(ref _nextModelInstanceId);

    /// <summary>
    /// 已绑定窗口的推送订阅
    /// </summary>
    private readonly List<Action<byte[]>> _pushed = [];

    /// <summary>
    /// 绑定窗口的推送回调
    /// </summary>
    /// <param name="handler">订阅器</param>
    internal void SubscribePushed(Action<byte[]> handler)
    {
        lock (_pushed)
        {
            if (!_pushed.Contains(handler))
                _pushed.Add(handler);
        }
    }

    /// <summary>
    /// 解绑窗口的推送回调
    /// </summary>
    /// <param name="handler">订阅器</param>
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

    /// <summary>
    /// 解除全部绑定
    /// </summary>
    internal void Unbind()
    {
        lock (_pushed)
            _pushed.Clear();
        UnbindCollections();
    }

    /// <summary>
    /// 退订全部集合属性的 CollectionChanged 与模型元素订阅（防泄漏；重绑时重新挂接）。
    /// </summary>
    private void UnbindCollections()
    {
        foreach (var kv in _collectionSubs)
            kv.Value.CollectionChanged -= OnCollectionChanged;
        _collectionSubs.Clear();
        foreach (var set in _itemSubs.Values)
            foreach (var m in set)
                m.PropertyChanged -= OnItemPropertyChanged;
        _itemSubs.Clear();
    }

    /// <summary>
    /// 前端回写期间置位，抑制 PropertyChanged 回声。
    /// </summary>
    private bool _isApplyingRemoteWrite;

    /// <summary>
    /// 向全部绑定窗口推送信封。单订阅者快路径（免 ToArray），无订阅者直接返回。
    /// </summary>
    private void PushEnvelope(byte[] bytes)
    {
        Action<byte[]>[]? snapshot = null;
        lock (_pushed)
        {
            if (_pushed.Count == 0)
                return;
            if (_pushed.Count == 1)
            {
                _pushed[0](bytes); // 单订阅者：免 ToArray 分配
                return;
            }
            snapshot = [.. _pushed];
        }
        foreach (var handler in snapshot)
            handler(bytes);
    }

    /// <summary>
    /// 向除源窗口（exclude）外的全部绑定窗口推送信封（远程回写后的跨窗口同步）。
    /// </summary>
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
    /// 模型序号（完整消息名 FNV-1a 哈希），线缆上代替消息名；0 = 未生成编码器，回退通用快照。
    /// </summary>
    protected virtual int ModelId => 0;

    /// <summary>
    /// 把整个模型序列化为生成消息的 protobuf 字节（仅当 <see cref="ModelId"/> 非 0 时调用）。
    /// </summary>
    protected virtual byte[] EncodeFullSnapshot()
        => throw new NotSupportedException($"模型 {GetType().Name} 未由 WebWindowUI.Generator 生成完整模型编码器。");

    /// <summary>
    /// 把单属性变化编码成增量 update 的 protobuf 字节（仅 <see cref="ModelId"/> 非 0 时调用），只含被修改的字段。
    /// </summary>
    protected virtual byte[] EncodePropertyUpdate(string propertyName, object? value)
        => throw new NotSupportedException($"模型 {GetType().Name} 未由 WebWindowUI.Generator 生成增量 update 编码器。");

    /// <summary>
    /// 生成代码的「前端回写」钩子：命中写值返回 true，未命中返回 false（不反射兜底）。
    /// </summary>
    protected virtual bool TrySetGeneratedProperty(string name, ModelValue? value) => false;

    /// <summary>
    /// 生成代码的「命令调用」钩子：commandId = [RelayCommand] 声明序；命中返回 true（含被 CanExecute 拒绝）。
    /// </summary>
    protected virtual bool TryInvokeGeneratedCommand(int commandId, ModelValue? value) => false;

    /// <summary>
    /// 生成代码的「按名读值」钩子：命中返回 true 并输出现值，未命中返回 false（不反射兜底）。
    /// </summary>
    protected virtual bool TryGetGeneratedProperty(string name, out object? value) { value = null; return false; }

    /// <summary>
    /// 源生成器产出的集合订阅：对每个 INotifyCollectionChanged 属性 EnsureCollectionSubscribed。
    /// </summary>
    protected virtual void SubscribeGeneratedCollections() { }

    /// <summary>
    /// 以前端回写语义应用属性 setter（期间抑制 PropertyChanged 回声；供生成代码调用）。
    /// </summary>
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

        // 未命中（模型未接生成器/属性非公开）直接跳过订阅与推送。
        if (!TryGetGeneratedProperty(e.PropertyName, out object? value))
            return;

        // 集合属性被替换时切换 CollectionChanged 订阅（须在回声抑制前，否则回写期间 .Add() 静默丢失）。
        EnsureCollectionSubscribed(e.PropertyName, value);

        // 模型元素集合属性被替换时切换元素订阅（同样在回声抑制前，远程整列回写也要跟上新元素）。
        if (_itemSubs.ContainsKey(e.PropertyName))
            EnsureItemsSubscribed(e.PropertyName, value as IEnumerable);

        // 前端回写引起的属性变化不再回传（冗余回声）。
        if (_isApplyingRemoteWrite)
            return;

        // 未生成 update 编码器的模型不推送增量。
        if (ModelId == 0)
            return;

        PushEnvelope(BuildUpdateEnvelope(e.PropertyName, value));
    }

    /// <summary>
    /// 模型元素订阅：集合属性（ObservableCollection&lt;WebWindowModel&gt;）每个元素挂 PropertyChanged，
    /// 元素属性变化 → 逐元素 ElementSet 补丁推前端。差量增删：新项挂接、离开集合的退订。
    /// 生成的 SubscribeGeneratedCollections 对模型元素集合调用；结构变化/整体替换由
    /// <see cref="OnCollectionChanged"/> / <see cref="OnModelPropertyChanged"/> 重同步。
    /// </summary>
    private readonly Dictionary<string, HashSet<WebWindowModel>> _itemSubs = [];

    /// <summary>
    /// 确保集合的每个元素都订阅 PropertyChanged（差量增删；items 为 null/空集合时退订全部）。
    /// 供生成的 SubscribeGeneratedCollections 调用。
    /// </summary>
    protected void EnsureItemsSubscribed(string propertyName, IEnumerable? items)
    {
        var wanted = new HashSet<WebWindowModel>();
        if (items is not null)
        {
            foreach (var item in items)
            {
                if (item is WebWindowModel m)
                    wanted.Add(m);
            }
        }

        if (!_itemSubs.TryGetValue(propertyName, out HashSet<WebWindowModel>? cur))
        {
            cur = new HashSet<WebWindowModel>();
            _itemSubs[propertyName] = cur;
        }

        foreach (var m in wanted)
        {
            if (cur.Add(m))
                m.PropertyChanged += OnItemPropertyChanged;
        }

        var stale = new List<WebWindowModel>();
        foreach (var m in cur)
            if (!wanted.Contains(m))
                stale.Add(m);
        foreach (var m in stale)
        {
            cur.Remove(m);
            m.PropertyChanged -= OnItemPropertyChanged;
        }
    }

    /// <summary>
    /// 元素属性变化 → 逐元素 ElementSet 补丁推全部绑定窗口（.NET 侧原地改元素属性的推送方向）。
    /// 远程元素级回写期间不推（回声抑制）。元素可出现在多个集合 → 每个包含它的集合各推一条。
    /// </summary>
    private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_isApplyingRemoteWrite)
            return;
        if (e.PropertyName is null || sender is not WebWindowModel item)
            return;

        foreach (var kv in _itemSubs)
        {
            if (!kv.Value.Contains(item))
                continue;
            if (!item.TryGetGeneratedProperty(e.PropertyName, out object? value))
                continue;
            TraceElem($"OnItemPropertyChanged push {kv.Key} id={item.ModelInstanceId} {e.PropertyName}={value}");
            PushEnvelope(BuildElementUpdateEnvelope(kv.Key, item.ModelInstanceId, e.PropertyName, value));
        }
    }

    private static void TraceElem(string message)
    {
        try
        {
            System.IO.File.AppendAllText(
                System.IO.Path.Combine(System.IO.Path.GetTempPath(), "wwui_trace.txt"),
                $"{System.DateTime.Now:HH:mm:ss.fff} T{Environment.CurrentManagedThreadId} elem: {message}\r\n");
        }
        catch { }
    }

    /// <summary>
    /// 集合属性（ObservableCollection 等 INotifyCollectionChanged）→ 当前订阅实例。
    /// </summary>
    private readonly Dictionary<string, INotifyCollectionChanged> _collectionSubs = [];

    /// <summary>
    /// 挂接全部集合属性的 CollectionChanged。字段初始化器在基类构造后才执行，基类 ctor 看不到
    /// 初始集合，须在首次快照时武装；集合被替换由 <see cref="OnModelPropertyChanged"/> 切换订阅。
    /// </summary>
    internal void ArmCollectionSubscriptions()
        => SubscribeGeneratedCollections();

    /// <summary>
    /// 确保集合属性的 CollectionChanged 挂到当前实例；值非集合或换了实例时切换订阅。
    /// </summary>
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
    /// ObservableCollection 增删 → 差量补丁推送：按事件 Action 编码 CollectionPatch，前端原地
    /// splice，比整列表更新省流量。Reset 不带新旧元素，回退整列表补丁。远程回写期间不推送。
    /// </summary>
    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        foreach (var kv in _collectionSubs)
        {
            if (!ReferenceEquals(kv.Value, sender))
                continue;
            // 模型元素订阅随结构变化重同步（须在回声抑制前：远程整列回写 Clear+Add 也要跟上新元素）。
            if (_itemSubs.ContainsKey(kv.Key) && sender is IEnumerable items)
                EnsureItemsSubscribed(kv.Key, items);
            if (_isApplyingRemoteWrite)
                return;
            if (sender is IDictionary)
            {
                // 字典（ObservableDictionary 等）：键值语义无索引差量，原地改 → 整属性重推，前端整体替换。
                if (ModelId == 0)
                    return;
                if (TryGetGeneratedProperty(kv.Key, out object? value))
                    PushEnvelope(BuildUpdateEnvelope(kv.Key, value));
                return;
            }
            if (e.Action == NotifyCollectionChangedAction.Reset)
            {
                // Reset 不带元素，回退整列表补丁（重读属性取全量）。
                if (TryGetGeneratedProperty(kv.Key, out object? value))
                    PushEnvelope(BuildPatchEnvelope(kv.Key, CollectionPatchAction.Reset, value));
                return;
            }
            PushEnvelope(BuildPatchEnvelope(kv.Key, e));
            return;
        }
    }

    /// <summary>
    /// 把集合变更事件编码成差量补丁信封（Insert/Remove/Replace/Move）。
    /// </summary>
    private byte[] BuildPatchEnvelope(string propertyName, NotifyCollectionChangedEventArgs e)
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
        return ModelProtocol.Encode(new WebMessage { ModelInstanceId = ModelInstanceId, Patch = patch });
    }

    /// <summary>
    /// 把集合整体编码成 Reset 补丁信封（Items 承载全量，前端整体替换）。
    /// </summary>
    private byte[] BuildPatchEnvelope(string propertyName, CollectionPatchAction action, object? value)
    {
        var patch = new CollectionPatch { Property = propertyName, Action = action };
        if (value is IEnumerable enumerable)
        {
            foreach (var item in enumerable)
                patch.Items.Add(ModelProtocol.ToModelValue(item));
        }
        return ModelProtocol.Encode(new WebMessage { ModelInstanceId = ModelInstanceId, Patch = patch });
    }

    /// <summary>
    /// 把单个属性编码成增量 update 信封（本地属性变化 / 远程回写后广播共用）。
    /// </summary>
    private byte[] BuildUpdateEnvelope(string propertyName, object? value)
    {
        var payload = EncodePropertyUpdate(propertyName, value);
        var msg = new WebMessage
        {
            ModelInstanceId = ModelInstanceId,
            Update = new ModelUpdate { ModelId = ModelId, Payload = payload },
        };
        return ModelProtocol.Encode(msg);
    }

    /// <summary>
    /// 把远程回写结果广播给除源窗口外的所有绑定窗口（共享模型实例跨窗口同步）。
    /// </summary>
    internal void BroadcastPropertyUpdate(string propertyName, Action<byte[]> exclude)
    {
        if (ModelId == 0)
            return;
        // 未命中（属性非生成）不广播。
        if (TryGetGeneratedProperty(propertyName, out object? value))
            PushEnvelope(BuildUpdateEnvelope(propertyName, value), exclude);
    }

    /// <summary>
    /// 前端元素级写回：ModelSet{ElementProperty 非空}。按 ModelInstanceId 在集合里找元素，
    /// 在回声抑制下原地写该元素属性（复用 item.TrySetProperty，保实例）。ElementProperty 为空 =
    /// 旧整属性回写（回退 TrySetProperty）。命中返回 true，未命中（元素不存在/属性不可写）返回 false。
    /// </summary>
    internal bool TrySetElementProperty(string collection, long elementInstanceId, string? elementProperty, ModelValue? value)
    {
        if (string.IsNullOrEmpty(elementProperty))
            return TrySetProperty(collection, value);
        if (!TryGetGeneratedProperty(collection, out object? collValue) || collValue is not IEnumerable items)
            return false;

        WebWindowModel? target = null;
        foreach (var item in items)
        {
            if (item is WebWindowModel m && m.ModelInstanceId == elementInstanceId)
            {
                target = m;
                break;
            }
        }
        if (target is null)
            return false;

        bool ok = false;
        _isApplyingRemoteWrite = true; // 抑制本侧 OnItemPropertyChanged 回声
        try
        {
            ok = target.TrySetProperty(ModelProtocol.ToPascalCase(elementProperty), value);
        }
        finally
        {
            _isApplyingRemoteWrite = false;
        }
        return ok;
    }

    /// <summary>
    /// 把元素级写回结果广播给除源窗口外的所有绑定窗口（共享模型实例跨窗口逐元素同步）。
    /// elementProperty 是线缆上的 camelCase；按 ID 重读权威值后推 ElementSet 补丁。
    /// </summary>
    internal void BroadcastElementUpdate(string collection, long elementInstanceId, string elementProperty, Action<byte[]> exclude)
    {
        if (!TryGetGeneratedProperty(collection, out object? collValue) || collValue is not IEnumerable items)
            return;
        foreach (var item in items)
        {
            if (item is WebWindowModel m && m.ModelInstanceId == elementInstanceId)
            {
                if (!m.TryGetGeneratedProperty(ModelProtocol.ToPascalCase(elementProperty), out object? value))
                    return;
                PushEnvelope(BuildElementUpdateEnvelope(collection, elementInstanceId, ModelProtocol.ToPascalCase(elementProperty), value), exclude);
                return;
            }
        }
    }

    /// <summary>
    /// 把单个元素的属性变更编码成 ElementSet 补丁信封（元素级推送 / 元素级写回后跨窗口广播共用）。
    /// elementProperty 为 .NET 侧 PascalCase 名，线缆上转 camelCase；元素由 ModelInstanceId 寻址。
    /// </summary>
    private byte[] BuildElementUpdateEnvelope(string collection, long elementInstanceId, string elementProperty, object? value)
    {
        var patch = new CollectionPatch
        {
            Action = CollectionPatchAction.ElementSet,
            Property = collection,
            ElementInstanceId = elementInstanceId,
            ElementProperty = ModelProtocol.ToCamelCase(elementProperty),
            ElementValue = ModelProtocol.ToModelValue(value),
        };
        return ModelProtocol.Encode(new WebMessage { ModelInstanceId = ModelInstanceId, Patch = patch });
    }

    /// <summary>
    /// 生成完整快照信封（页面加载完成 / 前端就绪时发送）。
    /// </summary>
    internal byte[] BuildSnapshotEnvelope()
    {
        // 首次推送前武装集合订阅（字段初始化器晚于基类 ctor）。
        ArmCollectionSubscriptions();

        var msg = new WebMessage { ModelInstanceId = ModelInstanceId };
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

    /// <summary>
    /// 通用完整快照：property → ModelValue（无生成编码器时的回退）。
    /// </summary>
    private ModelSnapshot BuildGenericSnapshot()
    {
        var snapshot = new ModelSnapshot();
        foreach (var prop in GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!prop.CanRead || prop.GetIndexParameters().Length > 0)
                continue;
            if (prop.Name == nameof(ModelInstanceId))
                continue; // 框架元数据（实例唯一 ID），非模型数据，不进通用快照
            snapshot.Data[prop.Name] = ModelProtocol.ToModelValue(prop.GetValue(this));
        }
        return snapshot;
    }

    /// <summary>
    /// 执行前端命令调用（ModelInvoke { commandId, value }）。commandId = [RelayCommand] 声明序，
    /// 由生成代码命中对应 ICommand；CanExecute 不满足时拒绝执行；未收集的命令返回 false。
    /// </summary>
    internal bool TryInvokeCommand(int commandId, ModelValue? value)
        => TryInvokeGeneratedCommand(commandId, value);

    /// <summary>
    /// 前端回传的属性写入：按名写回（无反射），失败返回 false（不抛异常）。
    /// </summary>
    internal bool TrySetProperty(string name, ModelValue? value)
        => TrySetGeneratedProperty(name, value);
}
