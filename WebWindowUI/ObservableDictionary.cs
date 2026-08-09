using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;

namespace WebWindowUI;

/// <summary>
/// 可观察字典：.NET 侧原地改（<c>dict["k"]=v</c> / Add / Remove / Clear）即抛
/// <see cref="INotifyCollectionChanged.CollectionChanged"/> 与
/// <see cref="INotifyPropertyChanged.PropertyChanged"/>。绑定到 <see cref="WebWindowModel"/>
/// 后经 <see cref="WebWindowModel"/> 的集合订阅自动挂接，原地变更由框架对字典 sender 做
/// 「整属性重推」（键值语义无索引差量，复用增量 update 消息，ModelValue 对象 map 整体替换前端对象）。
///
/// 前端侧字典属性为 <c>Record&lt;string, unknown&gt;</c>：前端原地改经深 watch 整字典 name 键回写 .NET。
/// net10 无内置 ObservableDictionary（System.ObjectModel 无该类型），本类型补足「像 ObservableCollection
/// 一样」的原地变更自动推送能力。
/// </summary>
/// <typeparam name="TKey">键类型（须 notnull，字符串最常见）。</typeparam>
/// <typeparam name="TValue">值类型（可为标量/对象/模型值）。</typeparam>
public class ObservableDictionary<TKey, TValue> : IDictionary<TKey, TValue>, IDictionary,
    INotifyCollectionChanged, INotifyPropertyChanged where TKey : notnull
{
    private readonly Dictionary<TKey, TValue> _inner;

    public ObservableDictionary()
        => _inner = [];

    public ObservableDictionary(IEqualityComparer<TKey> comparer)
        => _inner = new Dictionary<TKey, TValue>(comparer);

    /// <summary>集合变更：Add（newItems）、Remove（oldItems）、Replace（this[key]=v 覆盖已有键）、Reset（Clear）。</summary>
    public event NotifyCollectionChangedEventHandler? CollectionChanged;

    public event PropertyChangedEventHandler? PropertyChanged;

    public TValue this[TKey key]
    {
        get => _inner[key];
        set
        {
            bool exists = _inner.TryGetValue(key, out TValue? old);
            _inner[key] = value;
            if (exists)
                OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Replace, value, old));
            else
                OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, value));
            OnPropertyChanged(nameof(Count));
            OnPropertyChanged(nameof(Values));
            OnPropertyChanged("Item[]");
        }
    }

    public ICollection<TKey> Keys => _inner.Keys;
    public ICollection<TValue> Values => _inner.Values;
    public int Count => _inner.Count;
    public bool IsReadOnly => false;

    public void Add(TKey key, TValue value)
    {
        _inner.Add(key, value);
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, value));
        OnPropertyChanged(nameof(Count));
        OnPropertyChanged(nameof(Values));
        OnPropertyChanged("Item[]");
    }

    public void Add(KeyValuePair<TKey, TValue> item) => Add(item.Key, item.Value);

    public bool ContainsKey(TKey key) => _inner.ContainsKey(key);

    public bool Contains(KeyValuePair<TKey, TValue> item)
        => _inner.TryGetValue(item.Key, out TValue? v) && EqualityComparer<TValue>.Default.Equals(v, item.Value);

    public bool Remove(TKey key)
    {
        if (_inner.Remove(key, out TValue? old))
        {
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove, old));
            OnPropertyChanged(nameof(Count));
            OnPropertyChanged(nameof(Values));
            OnPropertyChanged("Item[]");
            return true;
        }
        return false;
    }

    public bool Remove(KeyValuePair<TKey, TValue> item) => Remove(item.Key);

    public bool TryGetValue(TKey key, out TValue value) => _inner.TryGetValue(key, out value!);

    public void Clear()
    {
        _inner.Clear();
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        OnPropertyChanged(nameof(Count));
        OnPropertyChanged(nameof(Values));
        OnPropertyChanged("Item[]");
    }

    public void CopyTo(KeyValuePair<TKey, TValue>[] array, int arrayIndex)
        => ((ICollection<KeyValuePair<TKey, TValue>>)_inner).CopyTo(array, arrayIndex);

    public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator() => _inner.GetEnumerator();

    /// <summary>IDictionary 契约：枚举结果为 DictionaryEntry（ModelProtocol.ToModelValue 的 foreach 用）。</summary>
    IDictionaryEnumerator IDictionary.GetEnumerator() => new DictionaryEntryEnumerator(_inner);

    /// <summary>非泛型 IEnumerable（经 IDictionary.GetEnumerator 承载）。</summary>
    IEnumerator IEnumerable.GetEnumerator() => ((IDictionary)this).GetEnumerator();

    /// <summary>把 Dictionary 的 KeyValuePair 枚举包装成 IDictionaryEnumerator（Current = DictionaryEntry）。
    /// 注意：_inner 是 struct 字段，<b>不能 readonly</b>——readonly struct 字段访问会防御性复制，
    /// MoveNext() 改的是副本，存储的枚举器永远停在起点 → 无限循环。非 readonly 就地推进。</summary>
    private sealed class DictionaryEntryEnumerator : IDictionaryEnumerator
    {
        private Dictionary<TKey, TValue>.Enumerator _inner;
        public DictionaryEntryEnumerator(Dictionary<TKey, TValue> dict) => _inner = dict.GetEnumerator();
        public DictionaryEntry Entry => new(_inner.Current.Key, _inner.Current.Value!);
        public object Key => _inner.Current.Key!;
        public object? Value => _inner.Current.Value;
        public object Current => Entry;
        public bool MoveNext() => _inner.MoveNext();
        public void Reset() => throw new NotSupportedException();
    }

    // ---- 非泛型 IDictionary（显式实现）：ModelProtocol 的 `value is IDictionary` 分支、回写重建共用 ----

    object? IDictionary.this[object key]
    {
        get => this[(TKey)key];
        set => this[(TKey)key] = (TValue)value!;
    }

    ICollection IDictionary.Keys => _inner.Keys;
    ICollection IDictionary.Values => _inner.Values;
    bool IDictionary.IsFixedSize => false;
    bool IDictionary.IsReadOnly => false;
    bool ICollection.IsSynchronized => ((ICollection)_inner).IsSynchronized;
    object ICollection.SyncRoot => ((ICollection)_inner).SyncRoot;

    void IDictionary.Add(object key, object? value) => Add((TKey)key, (TValue)value!);
    bool IDictionary.Contains(object key) => key is TKey k && ContainsKey(k);
    void IDictionary.Remove(object key) => Remove((TKey)key);
    void ICollection.CopyTo(Array array, int index) => ((ICollection)_inner).CopyTo(array, index);

    private void OnCollectionChanged(NotifyCollectionChangedEventArgs e)
        => CollectionChanged?.Invoke(this, e);

    private void OnPropertyChanged(string propertyName)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
