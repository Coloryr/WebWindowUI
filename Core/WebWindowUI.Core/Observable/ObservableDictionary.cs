using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;

namespace WebWindowUI.Core.Observable;

/// <summary>
/// 可观察字典：原地改（dict[k]=v / Add / Remove / Clear）即抛变更事件，绑定后整属性重推；net10 无内置版本。
/// </summary>
/// <typeparam name="TKey">键类型（须 notnull）。</typeparam>
/// <typeparam name="TValue">值类型。</typeparam>
public class ObservableDictionary<TKey, TValue> : IDictionary<TKey, TValue>, IDictionary,
    INotifyCollectionChanged, INotifyPropertyChanged where TKey : notnull
{
    private readonly Dictionary<TKey, TValue> _inner;

    /// <summary>
    /// 创建空字典。
    /// </summary>
    public ObservableDictionary()
        => _inner = [];

    /// <summary>
    /// 用指定比较器创建空字典。
    /// </summary>
    /// <param name="comparer">键比较器。</param>
    public ObservableDictionary(IEqualityComparer<TKey> comparer)
        => _inner = new Dictionary<TKey, TValue>(comparer);

    /// <summary>
    /// 集合变更事件（Add/Remove/Replace/Reset）。
    /// </summary>
    public event NotifyCollectionChangedEventHandler? CollectionChanged;

    /// <summary>
    /// 属性变更事件（Count/Values/Item[]）。
    /// </summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// 索引器：读/写键值，原地改触发变更事件。
    /// </summary>
    /// <param name="key">键。</param>
    /// <returns>键对应的值。</returns>
    public TValue this[TKey key]
    {
        get => _inner[key];
        set
        {
            var exists = _inner.TryGetValue(key, out TValue? old);
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

    /// <summary>
    /// 键集合。
    /// </summary>
    public ICollection<TKey> Keys => _inner.Keys;

    /// <summary>
    /// 值集合。
    /// </summary>
    public ICollection<TValue> Values => _inner.Values;

    /// <summary>
    /// 元素个数。
    /// </summary>
    public int Count => _inner.Count;

    /// <summary>
    /// 是否只读（恒 false）。
    /// </summary>
    public bool IsReadOnly => false;

    /// <summary>
    /// 添加键值对；键已存在抛异常。
    /// </summary>
    /// <param name="key">键。</param>
    /// <param name="value">值。</param>
    public void Add(TKey key, TValue value)
    {
        _inner.Add(key, value);
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, value));
        OnPropertyChanged(nameof(Count));
        OnPropertyChanged(nameof(Values));
        OnPropertyChanged("Item[]");
    }

    /// <summary>
    /// 添加键值对。
    /// </summary>
    /// <param name="item">键值对。</param>
    public void Add(KeyValuePair<TKey, TValue> item) => Add(item.Key, item.Value);

    /// <summary>
    /// 是否包含指定键。
    /// </summary>
    /// <param name="key">键。</param>
    /// <returns>是否包含。</returns>
    public bool ContainsKey(TKey key) => _inner.ContainsKey(key);

    /// <summary>
    /// 是否包含指定键值对。
    /// </summary>
    /// <param name="item">键值对。</param>
    /// <returns>是否包含。</returns>
    public bool Contains(KeyValuePair<TKey, TValue> item)
        => _inner.TryGetValue(item.Key, out TValue? v) && EqualityComparer<TValue>.Default.Equals(v, item.Value);

    /// <summary>
    /// 移除指定键；返回是否移除成功。
    /// </summary>
    /// <param name="key">键。</param>
    /// <returns>是否移除成功。</returns>
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

    /// <summary>
    /// 移除指定键值对。
    /// </summary>
    /// <param name="item">键值对。</param>
    /// <returns>是否移除成功。</returns>
    public bool Remove(KeyValuePair<TKey, TValue> item) => Remove(item.Key);

    /// <summary>
    /// 尝试读取键值。
    /// </summary>
    /// <param name="key">键。</param>
    /// <param name="value">读取到的值；失败为 default。</param>
    /// <returns>是否读取成功。</returns>
    public bool TryGetValue(TKey key, out TValue value) => _inner.TryGetValue(key, out value!);

    /// <summary>
    /// 清空全部元素。
    /// </summary>
    public void Clear()
    {
        _inner.Clear();
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        OnPropertyChanged(nameof(Count));
        OnPropertyChanged(nameof(Values));
        OnPropertyChanged("Item[]");
    }

    /// <summary>
    /// 复制到数组。
    /// </summary>
    /// <param name="array">目标数组。</param>
    /// <param name="arrayIndex">起始下标。</param>
    public void CopyTo(KeyValuePair<TKey, TValue>[] array, int arrayIndex)
        => ((ICollection<KeyValuePair<TKey, TValue>>)_inner).CopyTo(array, arrayIndex);

    /// <summary>
    /// 枚举键值对。
    /// </summary>
    /// <returns>键值对枚举器。</returns>
    public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator() => _inner.GetEnumerator();

    /// <summary>
    /// IDictionary 契约：枚举结果为 DictionaryEntry（ModelProtocol.ToModelValue 的 foreach 用）。
    /// </summary>
    IDictionaryEnumerator IDictionary.GetEnumerator() => new DictionaryEntryEnumerator(_inner);

    /// <summary>
    /// 非泛型 IEnumerable（经 IDictionary.GetEnumerator 承载）。
    /// </summary>
    IEnumerator IEnumerable.GetEnumerator() => ((IDictionary)this).GetEnumerator();

    /// <summary>
    /// 把 KeyValuePair 枚举包装成 IDictionaryEnumerator。_inner 不能 readonly（readonly 访问防御性复制 → 无限循环）。
    /// </summary>
    private sealed class DictionaryEntryEnumerator : IDictionaryEnumerator
    {
        private Dictionary<TKey, TValue>.Enumerator _inner;

        /// <summary>
        /// 用字典枚举器初始化。
        /// </summary>
        /// <param name="dict">字典。</param>
        public DictionaryEntryEnumerator(Dictionary<TKey, TValue> dict) => _inner = dict.GetEnumerator();

        /// <summary>
        /// 当前项（DictionaryEntry）。
        /// </summary>
        public DictionaryEntry Entry => new(_inner.Current.Key, _inner.Current.Value!);

        /// <summary>
        /// 当前键。
        /// </summary>
        public object Key => _inner.Current.Key!;

        /// <summary>
        /// 当前值。
        /// </summary>
        public object? Value => _inner.Current.Value;

        /// <summary>
        /// 当前项。
        /// </summary>
        public object Current => Entry;

        /// <summary>
        /// 移动到下一项。
        /// </summary>
        /// <returns>是否有下一项。</returns>
        public bool MoveNext() => _inner.MoveNext();

        /// <summary>
        /// 不支持重置。
        /// </summary>
        public void Reset() => throw new NotSupportedException();
    }

    // ---- 非泛型 IDictionary（显式实现）：ModelProtocol 的 `value is IDictionary` 分支、回写重建共用 ----

    /// <summary>
    /// 非泛型索引器。
    /// </summary>
    /// <param name="key">键。</param>
    /// <returns>值。</returns>
    object? IDictionary.this[object key]
    {
        get => this[(TKey)key];
        set => this[(TKey)key] = (TValue)value!;
    }

    /// <summary>
    /// 非泛型键集合。
    /// </summary>
    ICollection IDictionary.Keys => _inner.Keys;

    /// <summary>
    /// 非泛型值集合。
    /// </summary>
    ICollection IDictionary.Values => _inner.Values;

    /// <summary>
    /// 是否固定大小（恒 false）。
    /// </summary>
    bool IDictionary.IsFixedSize => false;

    /// <summary>
    /// 是否只读（恒 false）。
    /// </summary>
    bool IDictionary.IsReadOnly => false;

    /// <summary>
    /// 是否同步（恒 false）。
    /// </summary>
    bool ICollection.IsSynchronized => ((ICollection)_inner).IsSynchronized;

    /// <summary>
    /// 同步根。
    /// </summary>
    object ICollection.SyncRoot => ((ICollection)_inner).SyncRoot;

    /// <summary>
    /// 非泛型添加。
    /// </summary>
    /// <param name="key">键。</param>
    /// <param name="value">值。</param>
    void IDictionary.Add(object key, object? value) => Add((TKey)key, (TValue)value!);

    /// <summary>
    /// 非泛型是否包含键。
    /// </summary>
    /// <param name="key">键。</param>
    /// <returns>是否包含。</returns>
    bool IDictionary.Contains(object key) => key is TKey k && ContainsKey(k);

    /// <summary>
    /// 非泛型移除。
    /// </summary>
    /// <param name="key">键。</param>
    void IDictionary.Remove(object key) => Remove((TKey)key);

    /// <summary>
    /// 非泛型复制到数组。
    /// </summary>
    /// <param name="array">目标数组。</param>
    /// <param name="index">起始下标。</param>
    void ICollection.CopyTo(Array array, int index) => ((ICollection)_inner).CopyTo(array, index);

    /// <summary>
    /// 触发集合变更事件。
    /// </summary>
    /// <param name="e">变更事件参数。</param>
    private void OnCollectionChanged(NotifyCollectionChangedEventArgs e)
        => CollectionChanged?.Invoke(this, e);

    /// <summary>
    /// 触发属性变更事件。
    /// </summary>
    /// <param name="propertyName">属性名。</param>
    private void OnPropertyChanged(string propertyName)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
