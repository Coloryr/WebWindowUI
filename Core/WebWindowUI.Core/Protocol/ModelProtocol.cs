using System.Collections;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Reflection;
using ProtoBuf;
using WebWindowUI.Core.Observable;

namespace WebWindowUI.Core.Protocol;

/// <summary>
/// 通用模型值（单字段增量/通用快照用）。oneof kind 未设置的成员 = null。
/// </summary>
[ProtoContract]
public sealed class ModelValue
{
    [ProtoMember(1)] public double? Number { get; set; }
    [ProtoMember(2)] public string? Text { get; set; }
    [ProtoMember(3)] public bool? Flag { get; set; }
    [ProtoMember(4)] public ModelValueList? List { get; set; }
    [ProtoMember(5)] public ModelValueMap? ObjectValue { get; set; }
    [ProtoMember(6)] public byte[]? Blob { get; set; }
}

[ProtoContract]
public sealed class ModelValueList
{
    [ProtoMember(1)] public List<ModelValue> Items { get; set; } = [];
}

[ProtoContract]
public sealed class ModelValueMap
{
    /// <summary>
    /// name 键（generic object/Dictionary、反射回退）：map&lt;string, ModelValue&gt;。
    /// </summary>
    [ProtoMember(1)] public Dictionary<string, ModelValue> Fields { get; set; } = [];

    /// <summary>
    /// 序数键（typed POCO 元素，键为 proto 字段号）：map&lt;int32, ModelValue&gt;。
    /// </summary>
    [ProtoMember(2)] public Dictionary<int, ModelValue> OrdinalFields { get; set; } = [];
}

/// <summary>
/// 前端 → .NET：页面就绪，请求完整快照。
/// </summary>
[ProtoContract]
public sealed class ModelReady
{
}

/// <summary>
/// 单字段增量更新。payload 是生成器产出的 update 消息字节，modelId = 模型序号；载荷只含被修改的字段。
/// </summary>
[ProtoContract]
public sealed class ModelUpdate
{
    [ProtoMember(1)] public int ModelId { get; set; }
    [ProtoMember(2)] public byte[]? Payload { get; set; }
}

/// <summary>
/// 前端 → .NET：单属性回写。
/// </summary>
[ProtoContract]
public sealed class ModelSet
{
    [ProtoMember(1)] public string Property { get; set; } = "";
    [ProtoMember(2)] public ModelValue? Value { get; set; }
}

/// <summary>
/// 前端 → .NET：执行模型命令（MVVM Command）。commandId = [RelayCommand] 声明序，
/// 命中「命令名 + Command」的 ICommand；value 为命令参数，按方法参数类型转换。
/// </summary>
[ProtoContract]
public sealed class ModelInvoke
{
    [ProtoMember(1)] public int CommandId { get; set; }
    [ProtoMember(2)] public ModelValue? Value { get; set; }
}

/// <summary>
/// 完整模型（通用回退）：property → ModelValue。
/// </summary>
[ProtoContract]
public sealed class ModelSnapshot
{
    [ProtoMember(1)] public Dictionary<string, ModelValue> Data { get; set; } = [];
}

/// <summary>
/// 完整模型（生成器产出）：payload 是生成消息的 protobuf 字节，modelId = 模型序号。
/// </summary>
[ProtoContract]
public sealed class GeneratedModel
{
    [ProtoMember(1)] public int ModelId { get; set; }
    [ProtoMember(2)] public byte[]? Payload { get; set; }
}

/// <summary>
/// 集合差量操作类别。序号是线缆契约，与前端 descriptor 的枚举一致。
/// </summary>
[ProtoContract]
public enum CollectionPatchAction
{
    Insert = 1, // .NET Add：在 Index 插入 Items
    Remove = 2, // .NET Remove：删除 Index 起 Count 个元素
    Replace = 3, // .NET Replace：以 Items 替换 Index 起 Count 个元素
    Move = 4, // .NET Move：把 FromIndex 起 Count 个元素移到 Index
    Reset = 5, // .NET Reset：事件不带新旧元素，无法差量——Items 承载整列表，前端整体替换
}

/// <summary>
/// .NET → 前端：集合属性的增删差量补丁，前端对响应式数组原地 splice，
/// 比整列表增量省流量；Reset 无法编码差量时回退整列表（Items 承载全量）。
/// </summary>
[ProtoContract]
public sealed class CollectionPatch
{
    [ProtoMember(1)] public CollectionPatchAction Action { get; set; }

    /// <summary>
    /// 集合属性名（PascalCase，前端 toCamelCase 后定位数组）。
    /// </summary>
    [ProtoMember(2)] public string Property { get; set; } = "";

    /// <summary>
    /// Insert/Remove/Replace/Move 的起始索引。
    /// </summary>
    [ProtoMember(3)] public int Index { get; set; }

    /// <summary>
    /// Remove/Replace/Move 的元素个数（Insert 与 Reset 用不到，0）。
    /// </summary>
    [ProtoMember(4)] public int Count { get; set; }

    /// <summary>
    /// Insert/Replace 的新元素 / Reset 的整列表（ModelValue，typed 元素为序数键 map）。
    /// </summary>
    [ProtoMember(5)] public List<ModelValue> Items { get; set; } = [];

    /// <summary>
    /// Move 的源索引（其余操作 0）。
    /// </summary>
    [ProtoMember(6)] public int FromIndex { get; set; }
}

/// <summary>
/// 外层信封：一次 postMessage 只承载一种消息。
/// </summary>
[ProtoContract]
public sealed class WebMessage
{
    [ProtoMember(1)] public ModelReady? Ready { get; set; }
    [ProtoMember(2)] public ModelUpdate? Update { get; set; }
    [ProtoMember(3)] public ModelSet? Set { get; set; }
    [ProtoMember(4)] public ModelSnapshot? Snapshot { get; set; }
    [ProtoMember(5)] public GeneratedModel? Full { get; set; }
    [ProtoMember(6)] public ModelInvoke? Invoke { get; set; }
    [ProtoMember(7)] public CollectionPatch? Patch { get; set; }

    /// <summary>
    /// 实例唯一 ID（见 WebWindowModel.ModelInstanceId），统一信封 header，不进 oneof payload。
    /// .NET→JS 全部携带；JS→.NET 回传同字段，本侧校验来源实例（0 = 未携带，容忍）。
    /// </summary>
    [ProtoMember(8)] public long ModelInstanceId { get; set; }
}

/// <summary>
/// Model 协议编解码与值转换。
/// </summary>
public static class ModelProtocol
{
    /// <summary>
    /// POCO 重建转换器（ModelValueMap → 实例）：成功返回 true 且 result 非 null，失败返回 false。
    /// </summary>
    public delegate bool PocoConvertFunc(ModelValueMap value, out object? result);

    /// <summary>
    /// 源生成器注册的 POCO 重建转换器（替代反射）。
    /// </summary>
    internal static readonly Dictionary<Type, PocoConvertFunc> _pocoConverters = [];

    /// <summary>
    /// 注册 POCO 重建转换器（源生成器 [ModuleInitializer] 调用）。
    /// </summary>
    public static void RegisterPocoConverter(Type type, PocoConvertFunc converter)
        => _pocoConverters[type] = converter;

    /// <summary>
    /// POCO 序列化转换器（实例 → 序数键 map），与 PocoConvertFunc 对称，替换反射。
    /// </summary>
    public delegate bool PocoToModelValueFunc(object value, out ModelValueMap? map);

    /// <summary>
    /// 源生成器注册的 POCO 序列化转换器（替代反射；键用 proto 字段号）。
    /// </summary>
    internal static readonly Dictionary<Type, PocoToModelValueFunc> _pocoSerializers = [];

    /// <summary>
    /// 注册 POCO 序列化转换器（源生成器 [ModuleInitializer] 调用）。
    /// </summary>
    public static void RegisterPocoSerializer(Type type, PocoToModelValueFunc serializer)
        => _pocoSerializers[type] = serializer;

    public static byte[] Encode(WebMessage msg)
    {
        using var ms = new MemoryStream();
        Serializer.Serialize(ms, msg);
        return ms.ToArray();
    }

    public static WebMessage? Decode(byte[] bytes)
    {
        if (bytes is null || bytes.Length == 0)
            return null;
        using var ms = new MemoryStream(bytes);
        return Serializer.Deserialize<WebMessage>(ms);
    }

    /// <summary>
    /// 把任意属性值转成 ModelValue（复杂值递归展开）。
    /// </summary>
    public static ModelValue ToModelValue(object? value)
        => ToModelValue(value, new HashSet<object>(ReferenceEqualityComparer.Instance));

    /// <summary>
    /// 把任意属性值转成 ModelValue。object/Dictionary/POCO 等复杂值递归展开；
    /// 环检测基于引用相等，递归前加入、递归后移除（允许 DAG，拦截真环）。
    /// </summary>
    public static ModelValue ToModelValue(object? value, HashSet<object> seen)
    {
        var v = new ModelValue();
        if (value is null)
            return v; // oneof 未设置 = null

        if (!seen.Add(value))
            throw new InvalidOperationException($"Model 值存在循环引用，无法转换为 protobuf ModelValue：{value.GetType().Name}。");

        try
        {
            switch (value)
            {
                case byte[] bytes:
                    v.Blob = bytes;
                    break;
                case MemoryStream ms:
                    v.Blob = ms.ToArray();
                    break;
                case string s:
                    v.Text = s;
                    break;
                case char c:
                    v.Text = c.ToString();
                    break;
                case bool b:
                    v.Flag = b;
                    break;
                case DateTime dt:
                    v.Text = dt.ToString("O", CultureInfo.InvariantCulture);
                    break;
                case DateTimeOffset dto:
                    v.Text = dto.ToString("O", CultureInfo.InvariantCulture);
                    break;
                case TimeSpan ts:
                    v.Text = ts.ToString("c", CultureInfo.InvariantCulture);
                    break;
                case Guid g:
                    v.Text = g.ToString();
                    break;
                case int or long or short or byte or sbyte or uint or ulong or ushort or float or double or decimal:
                    v.Number = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                    break;
                default:
                    if (value.GetType().IsEnum)
                    {
                        v.Number = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                    }
                    else if (value is IDictionary dict)
                    {
                        var map = new ModelValueMap();
                        foreach (DictionaryEntry entry in dict)
                            map.Fields[entry.Key?.ToString() ?? ""] = ToModelValue(entry.Value, seen);
                        v.ObjectValue = map;
                    }
                    else if (value is IEnumerable items)
                    {
                        var list = new ModelValueList();
                        foreach (var item in items)
                            list.Items.Add(ToModelValue(item, seen));
                        v.List = list;
                    }
                    else
                    {
                        // POCO：优先用源生成器注册的序数序列化器（键 = proto 字段号），miss 走反射（camelCase 键）。
                        if (_pocoSerializers.TryGetValue(value.GetType(), out PocoToModelValueFunc? serializer)
                            && serializer(value, out ModelValueMap? smap)
                            && smap is not null)
                        {
                            v.ObjectValue = smap;
                        }
                        else
                        {
                            var map = new ModelValueMap();
                            foreach (var p in value.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
                            {
                                if (!p.CanRead || p.GetIndexParameters().Length > 0)
                                    continue;
                                map.Fields[ToCamelCase(p.Name)] = ToModelValue(p.GetValue(value), seen);
                            }
                            v.ObjectValue = map;
                        }
                    }
                    break;
            }
        }
        finally
        {
            seen.Remove(value);
        }
        return v;
    }

    /// <summary>
    /// PascalCase → camelCase（与前端 TS 属性名约定一致，仅首字母小写）。
    /// </summary>
    private static string ToCamelCase(string name)
        => string.IsNullOrEmpty(name) ? name : char.ToLowerInvariant(name[0]) + name[1..];

    /// <summary>
    /// 把 ModelValue 转换回目标类型的值。类型不匹配返回 false（TrySetProperty 语义）。
    /// </summary>
    public static bool TryFromModelValue(ModelValue? value, Type targetType, out object? result)
    {
        result = null;

        // ModelSet 可能不带 value 字段，防御性处理。
        if (value is null)
            return false;

        var empty = value.Number is null && value.Text is null && value.Flag is null
            && value.List is null && value.ObjectValue is null && value.Blob is null;
        if (empty)
        {
            // 非空值类型不能接受 null
            if (targetType.IsValueType && Nullable.GetUnderlyingType(targetType) is null)
                return false;
            return true;
        }

        if (value.Number is not null)
        {
            result = ConvertNumber(value.Number.Value, targetType);
            return result is not null;
        }
        if (value.Text is not null)
        {
            if (targetType == typeof(string))
            {
                result = value.Text;
                return true;
            }
            if (targetType == typeof(object))
            {
                result = value.Text;
                return true;
            }
            return TryParseText(value.Text, targetType, out result);
        }
        if (value.Flag is not null)
        {
            if (targetType == typeof(bool) || targetType == typeof(object))
            {
                result = value.Flag.Value;
                return true;
            }
            return false;
        }
        if (value.Blob is not null)
        {
            if (targetType == typeof(byte[]) || targetType == typeof(object))
            {
                result = value.Blob;
                return true;
            }
            return false;
        }
        if (value.List is not null)
            return TryConvertList(value.List, targetType, out result);
        if (value.ObjectValue is not null)
            return TryConvertObject(value.ObjectValue, targetType, out result);

        return false;
    }

    /// <summary>
    /// 泛型包装：成功返回 true 并输出转换值；失败返回 false（result 为 default）。
    /// </summary>
    public static bool TryFromModelValue<T>(ModelValue? value, out T? result)
    {
        if (TryFromModelValue(value, typeof(T), out object? converted))
        {
            result = (T)converted!;
            return true;
        }
        result = default;
        return false;
    }

    private static object? ConvertNumber(double d, Type targetType)
    {
        var t = Nullable.GetUnderlyingType(targetType) ?? targetType;
        if (t == typeof(object))
        {
            // 前端 number 无类型信息：整数值还原成 long 保留整数语义，否则保留 double。
            return d == Math.Floor(d) && d >= long.MinValue && d <= long.MaxValue ? (long)d : d;
        }
        if (t == typeof(int)) return checked((int)d);
        if (t == typeof(long)) return checked((long)d);
        if (t == typeof(short)) return checked((short)d);
        if (t == typeof(byte)) return checked((byte)d);
        if (t == typeof(sbyte)) return checked((sbyte)d);
        if (t == typeof(uint)) return checked((uint)d);
        if (t == typeof(ulong)) return checked((ulong)d);
        if (t == typeof(ushort)) return checked((ushort)d);
        if (t == typeof(float)) return (float)d;
        if (t == typeof(double)) return d;
        if (t == typeof(decimal)) return (decimal)d;
        if (t.IsEnum)
        {
            var under = Enum.GetUnderlyingType(t);
            return Enum.ToObject(t, Convert.ChangeType(d, under, CultureInfo.InvariantCulture));
        }
        return null;
    }

    private static bool TryParseText(string text, Type targetType, out object? result)
    {
        var t = Nullable.GetUnderlyingType(targetType) ?? targetType;
        if (t == typeof(DateTime))
        {
            result = DateTime.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
            return true;
        }
        if (t == typeof(DateTimeOffset))
        {
            result = DateTimeOffset.Parse(text, CultureInfo.InvariantCulture);
            return true;
        }
        if (t == typeof(TimeSpan))
        {
            result = TimeSpan.Parse(text, CultureInfo.InvariantCulture);
            return true;
        }
        if (t == typeof(Guid))
        {
            result = Guid.Parse(text);
            return true;
        }
        if (t == typeof(char))
        {
            result = text.Length == 1 ? text[0] : null;
            return result is not null;
        }
        result = null;
        return false;
    }

    private static bool TryConvertList(ModelValueList list, Type targetType, out object? result)
    {
        result = null;
        var t = Nullable.GetUnderlyingType(targetType) ?? targetType;

        if (t == typeof(object))
        {
            var objs = new List<object?>();
            foreach (var item in list.Items)
            {
                if (!TryFromModelValue(item, typeof(object), out object? r))
                    return false;
                objs.Add(r);
            }
            result = objs;
            return true;
        }

        var elemType = t.IsArray ? t.GetElementType()
            : IsGenericCollection(t) ? t.GetGenericArguments()[0] : null;
        if (elemType is null)
            return false;

        var arr = Array.CreateInstance(elemType, list.Items.Count);
        for (var i = 0; i < list.Items.Count; i++)
        {
            if (!TryFromModelValue(list.Items[i], elemType, out object? item))
                return false;
            arr.SetValue(item, i);
        }

        if (t.IsArray)
        {
            result = arr;
            return true;
        }

        // List<T> 可赋给各集合接口；ObservableCollection<T> 须实例化同类型，否则原地改推送无从谈起。
        var listType = t.GetGenericTypeDefinition() == typeof(ObservableCollection<>)
            ? typeof(ObservableCollection<>).MakeGenericType(elemType)
            : typeof(List<>).MakeGenericType(elemType);
        var listT = (IList)Activator.CreateInstance(listType)!;
        foreach (var x in arr)
            listT.Add(x);
        result = listT;
        return true;
    }

    private static bool IsGenericCollection(Type t)
    {
        if (!t.IsGenericType)
            return false;
        Type def = t.GetGenericTypeDefinition();
        return def == typeof(List<>)
            || def == typeof(IList<>)
            || def == typeof(ICollection<>)
            || def == typeof(IReadOnlyList<>)
            || def == typeof(IReadOnlyCollection<>)
            || def == typeof(IEnumerable<>)
            || def == typeof(ObservableCollection<>);
    }

    /// <summary>
    /// 枚举 ModelValueMap 全部条目：name 键 Fields + 序数键 OrdinalFields（int → 字符串承载），
    /// object/Dictionary 消费路径经此合并。
    /// </summary>
    private static IEnumerable<KeyValuePair<string, ModelValue>> EnumerateMapEntries(ModelValueMap map)
    {
        foreach (var kv in map.Fields)
            yield return kv;
        foreach (var kv in map.OrdinalFields)
            yield return new KeyValuePair<string, ModelValue>(kv.Key.ToString(CultureInfo.InvariantCulture), kv.Value);
    }

    private static bool TryConvertObject(ModelValueMap map, Type targetType, out object? result)
    {
        result = null;
        var t = Nullable.GetUnderlyingType(targetType) ?? targetType;

        if (t == typeof(object) || t == typeof(Dictionary<string, object>))
        {
            var dict = new Dictionary<string, object?>();
            foreach (var kv in EnumerateMapEntries(map))
            {
                if (!TryFromModelValue(kv.Value, typeof(object), out object? r))
                    return false;
                dict[kv.Key] = r;
            }
            result = dict;
            return true;
        }

        if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(Dictionary<,>)
            && t.GetGenericArguments()[0] == typeof(string))
        {
            var valType = t.GetGenericArguments()[1];
            var dict = (IDictionary)Activator.CreateInstance(t)!;
            foreach (var kv in EnumerateMapEntries(map))
            {
                if (!TryFromModelValue(kv.Value, valType, out object? r))
                    return false;
                dict[kv.Key] = r;
            }
            result = dict;
            return true;
        }

        // ObservableDictionary<,>（string 键）：重建同类实例，保留原地改自动推送能力。
        if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(ObservableDictionary<,>)
            && t.GetGenericArguments()[0] == typeof(string))
        {
            var valType = t.GetGenericArguments()[1];
            var dict = (IDictionary)Activator.CreateInstance(t)!;
            foreach (var kv in EnumerateMapEntries(map))
            {
                if (!TryFromModelValue(kv.Value, valType, out object? r))
                    return false;
                dict[kv.Key] = r;
            }
            result = dict;
            return true;
        }

        // 源生成器注册的转换器优先，miss 走反射兜底。
        if (_pocoConverters.TryGetValue(t, out PocoConvertFunc? converter))
            return converter(map, out result);

        // POCO：反射构造目标类型，按属性名（忽略大小写）匹配写入；未知键跳过。
        // 支撑 List<SomeModel> 回写——TryConvertList 按元素类型调到这里实例化。
        if (t.IsClass && !t.IsAbstract && t.GetConstructor(Type.EmptyTypes) is not null)
        {
            PropertyInfo[] props = [.. t.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.CanWrite && p.SetMethod is not null && !p.SetMethod.IsStatic && p.GetIndexParameters().Length == 0)];
            if (props.Length > 0)
            {
                var instance = Activator.CreateInstance(t);
                if (instance is not null)
                {
                    foreach (var kv in map.Fields)
                    {
                        // 前端发来的是 camelCase 键（TS 属性名），与 .NET PascalCase 属性忽略大小写匹配
                        var prop = props.FirstOrDefault(p => string.Equals(p.Name, kv.Key, StringComparison.OrdinalIgnoreCase));
                        if (prop is null)
                            continue;
                        if (!TryFromModelValue(kv.Value, prop.PropertyType, out object? v))
                            return false;
                        prop.SetValue(instance, v);
                    }
                    result = instance;
                    return true;
                }
            }
        }

        return false;
    }
}
