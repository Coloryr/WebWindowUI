namespace WebWindowUI.Generator;

/// <summary>
/// 不可变数组，值相等（SequenceEqual）——Roslyn 增量管线的缓存键：管线下游比较「本次算出的值」
/// 与「上次缓存的值」时用 EqualityComparer.Default.Equals，数组/字典默认引用相等会让任何变化
/// 波及全下游；包一层值相等后，仅当内容真正变化才重算，实现按模型/按上下文增量缓存。
/// </summary>
internal readonly struct EquatableArray<T> : IEquatable<EquatableArray<T>>
{
    private readonly T[]? _array;

    public EquatableArray(T[] array) => _array = array;

    public int Length => _array?.Length ?? 0;

    public T this[int index] => _array![index];

    public IEnumerator<T> GetEnumerator()
    {
        for (int i = 0; i < Length; i++)
            yield return _array![i];
    }

    public bool Equals(EquatableArray<T> other)
    {
        if (Length != other.Length)
            return false;
        for (int i = 0; i < Length; i++)
            if (!EqualityComparer<T>.Default.Equals(_array![i], other._array![i]))
                return false;
        return true;
    }

    public override bool Equals(object? obj) => obj is EquatableArray<T> other && Equals(other);

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
