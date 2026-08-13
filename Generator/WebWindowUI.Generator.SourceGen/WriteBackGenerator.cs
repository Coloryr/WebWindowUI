using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using System.Text;

namespace WebWindowUI.Generator.SourceGen;

/// <summary>
/// 为每个 <c>WebWindowModel</c> 子类产出「写回」partial 代码，替代运行时反射
/// （前端 → .NET 方向的属性写回、命令调用、POCO 重建、集合订阅）。
///
/// 生成代码引用 CommunityToolkit.Mvvm 的 <c>[ObservableProperty]</c>/<c>[RelayCommand]</c>
/// 产物属性（如 <c>Name</c>/<c>OpenWindowCommand</c>）——本生成器跑在**未加生成源码的初始编译**
/// 上，看不到那些属性，所以属性名只能从**字段符号**（剥一个前导 <c>_</c> 再 PascalCase）推、
/// 命令属性名只能从**方法符号**推；生成代码引用它们是合法的：全部生成器产物最终合并进同一编译一起编译。
///
/// 每个模型产出 <c>{Model}.WriteBack.g.cs</c>：
///   - <c>TrySetGeneratedProperty</c>        属性写回 switch（只写非只读 [ObservableProperty] 字段）
///   - <c>TryInvokeGeneratedCommand</c>      命令调用 switch（CanExecute 门控语义与现运行时一致）
///   - <c>TryGetGeneratedProperty</c>        按名读值 switch（服务集合重订阅/集合推送/广播）
///   - <c>SubscribeGeneratedCollections</c>  集合订阅（implements INotifyCollectionChanged 的属性）
///   - <c>ConvertFromModelValue</c> + <c>[ModuleInitializer]</c>  POCO 重建（注册进 ModelProtocol 注册表）
/// </summary>
[Generator]
public sealed class WriteBackGenerator : IIncrementalGenerator
{
    private const string ObservablePropertyAttribute = "CommunityToolkit.Mvvm.ComponentModel.ObservablePropertyAttribute";
    private const string RelayCommandAttribute = "CommunityToolkit.Mvvm.Input.RelayCommandAttribute";
    private const string InccMetadataName = "System.Collections.Specialized.INotifyCollectionChanged";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var models = context.SyntaxProvider.CreateSyntaxProvider(
                predicate: static (node, _) => IsCandidate(node),
                transform: static (ctx, ct) => Transform(ctx, ct))
            .Where(static m => m is not null);

        // 按模型注册输出（不再 Collect 全量重算，见 #6）：单模型变化只重产该模型的 .g.cs，
        // 其余模型输出走增量缓存（ModelInfo 值相等 → 未变的模型不重新 emit）。
        context.RegisterSourceOutput(models, static (spc, m) => { if (m is not null) Emit(spc, m); });
    }

    // ---- 纯语法预筛：可能含 [ObservableProperty]/[RelayCommand] 的类（成员带属性列表） ----

    private static bool IsCandidate(SyntaxNode node)
        => node is Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax { BaseList: not null } cds
           && cds.Members.Any(m => m.AttributeLists.Count > 0);

    // ---- transform：语义解析，只留纯数据（record + EquatableArray，无 ISymbol），保增量缓存 ----

    private static ModelInfo? Transform(GeneratorSyntaxContext ctx, CancellationToken ct)
    {
        var cds = (Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax)ctx.Node;
        if (ctx.SemanticModel.GetDeclaredSymbol(cds, ct) is not INamedTypeSymbol sym)
            return null;
        if (!IsDerivedFromWebWindowModel(sym))
            return null;
        if (sym.IsGenericType)
            return null; // 罕见，暂不支持泛型模型

        INamedTypeSymbol? incc = ctx.SemanticModel.Compilation.GetTypeByMetadataName(InccMetadataName);

        // POCO 序数键：属性名 → proto 字段号（与 ModelProtoGenerator 声明序编号一致，单一来源防漂移）。
        // 生成器跑在初始编译上，此处从语法文本重解析（同 ProtoGenerator 的 allModelSources 做法）。
        IReadOnlyDictionary<string, int> fieldNumbers;
        try
        {
            fieldNumbers = ModelProtoGenerator.CollectFieldNumbers(ctx.Node.SyntaxTree.ToString(), sym.Name);
        }
        catch (ArgumentException)
        {
            fieldNumbers = new Dictionary<string, int>(); // 防御：解析失败无序号，Converter/Serializer 不产出序数 case
        }

        // 属性全集：写回/读值/集合订阅全部生成覆盖——[ObservableProperty] 字段 + 源码显式 public 可读
        // 非索引器属性。基类反射兜底已移除，分析器处理过的模型必须对**所有**公开属性都有生成 case，
        // 否则显式属性会静默失效（读值/写回/订阅都落空）。
        var props = new Dictionary<string, PropInfo>(StringComparer.Ordinal);
        foreach (var member in sym.GetMembers())
        {
            ct.ThrowIfCancellationRequested();
            if (member is IFieldSymbol f && HasAttribute(f, ObservablePropertyAttribute))
            {
                var pName = FieldToPropertyName(f.Name);
                var kind = GetCollectionKind(f.Type);
                props[pName] = new PropInfo(
                    pName,
                    f.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    f.IsReadOnly,
                    IsCollection(f.Type, incc),
                    fieldNumbers.TryGetValue(pName, out int n) ? n : 0,
                    kind,
                    IsModelElementCollection(f.Type) && kind == CollectionKind.List);
            }
        }
        foreach (var member in sym.GetMembers())
        {
            ct.ThrowIfCancellationRequested();
            if (member is IPropertySymbol pr
                && !pr.IsStatic
                && !pr.IsIndexer
                && !pr.IsImplicitlyDeclared
                && pr.DeclaredAccessibility == Accessibility.Public
                && pr.GetMethod is { IsStatic: false })
            {
                var writable = pr.SetMethod is { IsStatic: false } setter
                    && setter.DeclaredAccessibility == Accessibility.Public;
                var kind = GetCollectionKind(pr.Type);
                props[pr.Name] = new PropInfo(
                    pr.Name,
                    pr.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    !writable,
                    IsCollection(pr.Type, incc),
                    fieldNumbers.TryGetValue(pr.Name, out int n) ? n : 0,
                    kind,
                    IsModelElementCollection(pr.Type) && kind == CollectionKind.List);
            }
        }

        var commands = new List<CmdInfo>();
        foreach (var member in sym.GetMembers())
        {
            ct.ThrowIfCancellationRequested();
            if (member is IMethodSymbol m && m.MethodKind == MethodKind.Ordinary && HasAttribute(m, RelayCommandAttribute))
            {
                commands.Add(new CmdInfo(
                    m.Name,
                    m.Parameters.Length > 0
                        ? m.Parameters[0].Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                        : null));
            }
        }

        // POCO 可写属性 = 属性全集中非只读者（[ObservableProperty] + 显式 public 可写）。
        var propArray = props.Values.ToArray();
        var writableArray = props.Values.Where(p => !p.IsReadOnly).ToArray();

        var hasParameterlessCtor = !sym.IsAbstract && sym.InstanceConstructors.Any(c => c.Parameters.Length == 0);
        var ns = sym.ContainingNamespace.IsGlobalNamespace ? "" : sym.ContainingNamespace.ToDisplayString();

        return new ModelInfo(
            sym.Name,
            ns,
            new EquatableArray<PropInfo>(propArray),
            new EquatableArray<CmdInfo>(commands.ToArray()),
            new EquatableArray<PropInfo>(writableArray),
            hasParameterlessCtor);
    }

    /// <summary>
    /// 基类链是否落在 WebWindowUI.Core.WebWindowModel（WriteBack/Proto 两个生成器共用）。
    /// </summary>
    internal static bool IsDerivedFromWebWindowModel(INamedTypeSymbol sym)
    {
        for (INamedTypeSymbol? b = sym.BaseType; b is not null; b = b.BaseType)
            if (b.Name == "WebWindowModel" && b.ContainingNamespace.ToDisplayString() == "WebWindowUI.Core")
                return true;
        return false;
    }

    private static bool HasAttribute(ISymbol symbol, string attributeDisplayName)
        => symbol.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == attributeDisplayName);

    /// <summary>
    /// 精确复刻 CommunityToolkit 字段→属性名：剥**一个**前导 '_'，再首字母转大写（_name→Name、name→Name、__name→_Name）。
    /// </summary>
    private static string FieldToPropertyName(string fieldName)
    {
        var name = fieldName.Length > 0 && fieldName[0] == '_' ? fieldName.Substring(1) : fieldName;
        return name.Length > 0 && char.IsLower(name[0])
            ? char.ToUpperInvariant(name[0]) + name.Substring(1)
            : name;
    }

    private static bool IsCollection(ITypeSymbol type, INamedTypeSymbol? incc)
    {
        if (incc is null)
            return false;
        if (SymbolEqualityComparer.Default.Equals(type, incc))
            return true;
        return type.AllInterfaces.Contains(incc, SymbolEqualityComparer.Default);
    }

    /// <summary>
    /// 集合元素是否为 WebWindowModel 子类（元素级寻址/逐元素推送的前提：元素带 ModelInstanceId 与 PropertyChanged）。
    /// 泛型集合取第一个类型实参判定；非泛型/非模型元素返回 false。
    /// </summary>
    private static bool IsModelElementCollection(ITypeSymbol type)
    {
        if (type is not INamedTypeSymbol nt || !nt.IsGenericType || nt.TypeArguments.Length == 0)
            return false;
        return nt.TypeArguments[0] is INamedTypeSymbol elem && IsDerivedFromWebWindowModel(elem);
    }

    /// <summary>
    /// 集合类型分类（TrySet 原地清空重建用）：可原地重建的可变集合 → List/Dict；其余 None。
    /// ObservableCollection/ObservableDictionary 是框架的 INotifyCollectionChanged 集合，前端整列/整字典
    /// 回写不替换实例而是 Clear + 逐项 Add——get-only 只读属性也能写回，且保留实例与订阅。
    /// 用「命名空间 + 类名」判定，避免用户自定义同名类误判。
    /// </summary>
    private static CollectionKind GetCollectionKind(ITypeSymbol type)
    {
        if (type is not INamedTypeSymbol nt || !nt.IsGenericType)
            return CollectionKind.None;
        var ns = nt.ContainingNamespace.ToDisplayString();
        return (ns, nt.Name) switch
        {
            ("System.Collections.ObjectModel", "ObservableCollection") => CollectionKind.List,
            ("System.Collections.Generic", "List") => CollectionKind.List,
            ("System.Collections.Generic", "IList") => CollectionKind.List,
            ("WebWindowUI.Core.Observable", "ObservableDictionary") => CollectionKind.Dict,
            ("System.Collections.Generic", "Dictionary") => CollectionKind.Dict,
            ("System.Collections.Generic", "IDictionary") => CollectionKind.Dict,
            _ => CollectionKind.None,
        };
    }

    // ---- emit：生成五个成员 ----

    private static void Emit(SourceProductionContext spc, ModelInfo m)
        => spc.AddSource($"{m.ClassName}.WriteBack.g.cs", SourceText.From(BuildSource(m), Encoding.UTF8));

    private static string BuildSource(ModelInfo m)
    {
        var w = new CodeWriter();
        w.Line("// <auto-generated> Generated by WebWindowUI.Generator.SourceGen. Do not modify. </auto-generated>");
        w.Line("#nullable enable");
        w.Line();
        if (m.Namespace.Length > 0)
        {
            w.Line($"namespace {m.Namespace}");
            w.Open("{");
        }
        w.Line($"partial class {m.ClassName}");
        w.Open("{");
        EmitTrySetProperty(w, m);
        EmitTryInvokeCommand(w, m);
        EmitTryGetProperty(w, m);
        EmitSubscribeCollections(w, m);
        EmitPocoConverter(w, m);
        w.Close("}");
        if (m.Namespace.Length > 0)
            w.Close("}");
        return w.ToString();
    }

    private static void EmitTrySetProperty(CodeWriter w, ModelInfo m)
    {
        w.Line("protected override bool TrySetGeneratedProperty(string name, global::WebWindowUI.Core.Protocol.ModelValue? value)");
        w.Open("{");
        w.Line("switch (name)");
        w.Open("{");
        int i = 0;
        foreach (PropInfo p in m.Props)
        {
            // 集合属性（ObservableCollection / ObservableDictionary，implements INotifyCollectionChanged）：
            // 前端整列/整字典回写不替换实例而是原地清空重建——保留实例与订阅，get-only 只读集合也能写回。
            // Clear + 逐项 Add：列表 Add 元素、字典 Add KeyValuePair（IDictionary<K,V> 的 ICollection<KVP>.Add）。
            if (p.IsCollection && p.Kind != CollectionKind.None)
            {
                w.Line($"case \"{p.Name}\":");
                w.Open("{");
                w.Line("if (value is not null");
                w.Line($"    && global::WebWindowUI.Core.Protocol.ModelProtocol.TryFromModelValue(value, typeof({p.Type}), out object? c{i}))");
                w.Open("{");
                w.Line("ApplyRemoteWrite(() =>");
                w.Open("{");
                w.Line($"var incoming{i} = ({p.Type})c{i}!;");
                w.Line($"{p.Name}.Clear();");
                w.Line($"foreach (var item in incoming{i})");
                w.Line($"    {p.Name}.Add(item);");
                w.Close("}");
                w.Line(");");
                w.Line("return true;");
                w.Close("}");
                w.Line("return false;");
                w.Close("}");
                i++;
                continue;
            }
            if (p.IsReadOnly)
                continue;
            w.Line($"case \"{p.Name}\":");
            w.Open("{");
            w.Line("if (value is not null");
            w.Line($"    && global::WebWindowUI.Core.Protocol.ModelProtocol.TryFromModelValue(value, typeof({p.Type}), out object? c{i}))");
            w.Open("{");
            w.Line($"ApplyRemoteWrite(() => {p.Name} = ({p.Type})c{i}!);");
            w.Line("return true;");
            w.Close("}");
            w.Line("return false;");
            w.Close("}");
            i++;
        }
        w.Line("default:");
        w.Line("    return false;");
        w.Close("}");
        w.Close("}");
        w.Line();
    }

    private static void EmitTryInvokeCommand(CodeWriter w, ModelInfo m)
    {
        // commandId = [RelayCommand] 方法声明序（0 起），与 ModelProtoGenerator.CollectCommands 一致。
        w.Line("protected override bool TryInvokeGeneratedCommand(int commandId, global::WebWindowUI.Core.Protocol.ModelValue? value)");
        w.Open("{");
        w.Line("switch (commandId)");
        w.Open("{");
        int i = 0;
        foreach (CmdInfo c in m.Commands)
        {
            w.Line($"case {i}:");
            w.Open("{");
            w.Line("object? arg = null;");
            w.Line($"if (value is not null && global::WebWindowUI.Core.Protocol.ModelProtocol.TryFromModelValue(value, typeof({c.ParamType ?? "global::System.Object"}), out object? c{i}))");
            w.Line($"    arg = c{i};");
            w.Line($"if (!{c.Name}Command.CanExecute(arg)) return false;");
            w.Line($"{c.Name}Command.Execute(arg);");
            w.Line("return true;");
            w.Close("}");
            i++;
        }
        w.Line("default:");
        w.Line("    return false;");
        w.Close("}");
        w.Close("}");
        w.Line();
    }

    private static void EmitTryGetProperty(CodeWriter w, ModelInfo m)
    {
        w.Line("protected override bool TryGetGeneratedProperty(string name, out object? value)");
        w.Open("{");
        w.Line("switch (name)");
        w.Open("{");
        foreach (PropInfo p in m.Props)
            w.Line($"case \"{p.Name}\": value = {p.Name}; return true;");
        w.Line("default: value = null; return false;");
        w.Close("}");
        w.Close("}");
        w.Line();
    }

    private static void EmitSubscribeCollections(CodeWriter w, ModelInfo m)
    {
        var colls = new List<PropInfo>();
        foreach (PropInfo p in m.Props)
            if (p.IsCollection)
                colls.Add(p);
        var modelElems = colls.Where(p => p.IsModelElements).ToList();
        w.Line("protected override void SubscribeGeneratedCollections()");
        // 无集合属性 → 空实现；仅集合订阅 → 表达式体；模型元素集合还须挂元素订阅 → 块体。
        if (colls.Count == 0)
        {
            w.Line("{");
            w.Line("}");
        }
        else if (colls.Count == 1 && modelElems.Count == 0)
        {
            w.Line($"    => EnsureCollectionSubscribed(\"{colls[0].Name}\", {colls[0].Name});");
        }
        else
        {
            w.Open("{");
            foreach (PropInfo p in colls)
                w.Line($"EnsureCollectionSubscribed(\"{p.Name}\", {p.Name});");
            foreach (PropInfo p in modelElems)
                w.Line($"EnsureItemsSubscribed(\"{p.Name}\", {p.Name});");
            w.Close("}");
        }
        w.Line();
    }

    private static void EmitPocoConverter(CodeWriter w, ModelInfo m)
    {
        if (!m.HasParameterlessCtor || m.WritableProps.Length == 0)
            return;

        // 反序列化：序数对象 map（OrdinalFields，键 = proto 字段号 int）→ 实例。
        // 键是固定的协议序号（声明顺序 1..N），前端桥按同一编号序列化 typed 元素 → 不依赖命名一致。
        // 键是真实 int（map<int32,ModelValue>），switch (kv.Key) 直接 case 1: ——不需要字符串承载再解析。
        w.Line("internal static bool ConvertFromModelValue(global::WebWindowUI.Core.Protocol.ModelValueMap v, out object? result)");
        w.Open("{");
        w.Line("result = null;");
        w.Line("if (v is null) return false;");
        w.Line($"var instance = new {m.ClassName}();");
        w.Line("foreach (var kv in v.OrdinalFields)");
        w.Open("{");
        w.Line("switch (kv.Key)");
        w.Open("{");
        int i = 0;
        foreach (PropInfo p in m.WritableProps)
        {
            if (p.Number <= 0)
                continue; // 字段号未知（解析失败/漂移）：不产出序数 case
            w.Line($"case {p.Number}:");
            w.Open("{");
            w.Line($"if (!global::WebWindowUI.Core.Protocol.ModelProtocol.TryFromModelValue(kv.Value, typeof({p.Type}), out object? c{i})) return false;");
            w.Line($"instance.{p.Name} = ({p.Type})c{i}!;");
            w.Line("break;");
            w.Close("}");
            i++;
        }
        w.Line("default:");
        w.Line("    break;");
        w.Close("}");
        w.Close("}");
        w.Line("result = instance;");
        w.Line("return true;");
        w.Close("}");
        w.Line();

        // 序列化：实例 → object map（序数键，与 ConvertFromModelValue 对称；用全部可读属性
        // 含只读——与全量快照元素字段集一致，前端只读展示；反序列化侧未知序数键跳过）。
        EmitPocoSerializer(w, m);

        w.Line("[global::System.Runtime.CompilerServices.ModuleInitializer]");
        w.Line("internal static void __WWUI_RegisterPocoConverter()");
        w.Open("{");
        w.Line($"global::WebWindowUI.Core.Protocol.ModelProtocol.RegisterPocoConverter(typeof({m.ClassName}), ConvertFromModelValue);");
        w.Line($"global::WebWindowUI.Core.Protocol.ModelProtocol.RegisterPocoSerializer(typeof({m.ClassName}), ConvertToModelValue);");
        w.Close("}");
    }

    private static void EmitPocoSerializer(CodeWriter w, ModelInfo m)
    {
        w.Line("internal static bool ConvertToModelValue(object value, out global::WebWindowUI.Core.Protocol.ModelValueMap? map)");
        w.Open("{");
        w.Line("map = null;");
        w.Line($"if (value is not {m.ClassName} instance) return false;");
        w.Line("var m = new global::WebWindowUI.Core.Protocol.ModelValueMap();");
        foreach (PropInfo p in m.Props)
        {
            if (p.Number <= 0)
                continue;
            w.Line($"m.OrdinalFields[{p.Number}] = global::WebWindowUI.Core.Protocol.ModelProtocol.ToModelValue(instance.{p.Name});");
        }
        w.Line("map = m;");
        w.Line("return true;");
        w.Close("}");
        w.Line();
    }

    // ---- 数据模型（record + EquatableArray：值相等，供增量缓存） ----

    /// <summary>集合类型分类（TrySet 原地清空重建用）：List = 列表（ObservableCollection/List/IList），
    /// Dict = 字典（ObservableDictionary/Dictionary/IDictionary），None = 非可变集合类型。</summary>
    internal enum CollectionKind { None, List, Dict }

    /// <summary>属性元数据；Number = proto 字段号（声明顺序 1..N，来自 ModelProtoGenerator.CollectFieldNumbers；
    /// 0 = 未解析到序号，POCO 序数 case 跳过）。Kind = 集合类型分类（TrySet 原地清空重建用）。
    /// IsModelElements = 集合元素是 WebWindowModel 子类（元素级寻址/逐元素推送，产 EnsureItemsSubscribed）。</summary>
    internal sealed record PropInfo(string Name, string Type, bool IsReadOnly, bool IsCollection, int Number, CollectionKind Kind,
        bool IsModelElements = false);

    internal sealed record CmdInfo(string Name, string? ParamType);

    internal sealed record ModelInfo(
        string ClassName,
        string Namespace,
        EquatableArray<PropInfo> Props,
        EquatableArray<CmdInfo> Commands,
        EquatableArray<PropInfo> WritableProps,
        bool HasParameterlessCtor);

    private sealed class CodeWriter
    {
        private readonly StringBuilder _sb = new();
        private int _indent;

        public void Open(string token)
        {
            Line(token);
            _indent++;
        }

        public void Close(string token)
        {
            _indent--;
            Line(token);
        }

        public void Line(string text = "")
            => _sb.Append(' ', _indent * 4).AppendLine(text);

        public override string ToString() => _sb.ToString();
    }
}
