namespace WebWindowUI.Generator;

/// <summary>
/// 不可变数组，值相等（SequenceEqual）——Roslyn 增量管线的缓存键。数组默认引用相等会让任何变化
/// 波及全下游，包一层值相等后仅当内容真正变化才重算。
/// </summary>
internal readonly struct EquatableArray<T> : IEquatable<EquatableArray<T>>
{
    private readonly T[]? _array;

    /// <summary>
    /// 包装数组。
    /// </summary>
    /// <param name="array">数组。</param>
    public EquatableArray(T[] array) => _array = array;

    /// <summary>
    /// 元素数。
    /// </summary>
    public int Length => _array?.Length ?? 0;

    /// <summary>
    /// 按索引取元素。
    /// </summary>
    /// <param name="index">索引。</param>
    public T this[int index] => _array![index];

    /// <summary>
    /// 枚举元素。
    /// </summary>
    public IEnumerator<T> GetEnumerator()
    {
        for (int i = 0; i < Length; i++)
            yield return _array![i];
    }

    /// <summary>
    /// 值相等：长度一致且逐元素相等。
    /// </summary>
    /// <param name="other">另一数组。</param>
    /// <returns>是否相等。</returns>
    public bool Equals(EquatableArray<T> other)
    {
        if (Length != other.Length)
            return false;
        for (int i = 0; i < Length; i++)
            if (!EqualityComparer<T>.Default.Equals(_array![i], other._array![i]))
                return false;
        return true;
    }

    /// <summary>
    /// 装箱等值比较。
    /// </summary>
    public override bool Equals(object? obj) => obj is EquatableArray<T> other && Equals(other);

    /// <summary>
    /// 内容哈希。
    /// </summary>
    public override int GetHashCode()
    {
        unchecked
        {
            int h = 17;
            if (_array is not null)
                foreach (T item in _array)
                    h = h * 31 + (item is null ? 0 : item.GetHashCode());
            return h;
        }
    }
}
