using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

// ModelParsed 嵌套在 ModelProtoGenerator 内（与 ProtoField/ModelCommand 同层），别名避免每次全限定。
using ParsedModel = WebWindowUI.Generator.ModelProtoGenerator.ModelParsed;

namespace WebWindowUI.Generator.SourceGen;

/// <summary>
/// 为每个 <c>WebWindowModel</c> 子类在内存内产出 <c>{Model}Proto.g.cs</c> partial：快照 DTO
/// <c>{Model}Snapshot</c>、增量 DTO <c>{Model}Update</c>、以及 partial class 的
/// <c>FullMessageName</c>/<c>EncodeFullSnapshot</c>/<c>UpdateMessageName</c>/<c>EncodePropertyUpdate</c> override。
///
/// 纯逻辑在 <see cref="ModelProtoGenerator.Generate"/>（本程序集内，namespace WebWindowUI.Generator），
/// 与 descriptor JSON + TS 镜像同源同逻辑——前端解码与 .NET 编码共用同一份字段映射。
///
/// 与 WriteBack 生成器共存：两者都产出 <c>partial class {Model}</c>，合并进同一编译、同一类型，无冲突。
/// descriptor/TS 是前端源码文件（src/bridge、src/models），源生成器写不了非 C# 文件，
/// 仍由 console（WebWindowUI.Generator，经 GenerateModelProto 目标）落盘。
/// </summary>
[Generator]
public sealed class ProtoGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var models = context.SyntaxProvider.CreateSyntaxProvider(
                predicate: static (node, _) => IsCandidate(node),
                transform: static (ctx, ct) => Transform(ctx, ct))
            .Where(static m => m is not null);

        // 全模型「类名 → 命名空间」值相等图（EquatableArray 序列相等）：改**其它**模型字段（命名空间不变）时
        // 该值相等 → 下游短路，使每个模型的 ParseModel 保持独立缓存（#6：不再一改全量重算解析）。
        var allNamespaces = models.Collect()
            .Select(static (list, ct) => BuildNamespaceMap(list));

        // 按模型解析一次：键 = 本模型源码 + 全模型命名空间图。只有本模型源码或命名空间图真正变化才重算，
        // 其余模型复用缓存的 ModelParsed（ParseModel 不再在 emit 阶段重新解析任何源码）。
        var parsed = models.Combine(allNamespaces)
            .Select(static (pair, ct) => pair.Left is null ? null : ParseModel(pair.Left, pair.Right))
            .Where(static m => m is not null);

        // 全模型已解析表 → 一次产出全部 {Model}Proto.g.cs（各模型 GenerateParsed 用缓存解析，仅字符串拼接）。
        context.RegisterSourceOutput(parsed.Collect(), static (spc, list) => BuildEmits(spc, list));
    }

    /// <summary>纯语法预筛：带基类列表的类（模型必须有基类）。比 WriteBack 宽——显式属性模型也要覆盖。</summary>
    private static bool IsCandidate(SyntaxNode node)
        => node is Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax { BaseList: not null };

    /// <summary>transform：只留纯数据（类名/命名空间/源码文本），不保留 ISymbol。</summary>
    private static ModelSourceInfo? Transform(GeneratorSyntaxContext ctx, CancellationToken ct)
    {
        var cds = (Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax)ctx.Node;
        if (ctx.SemanticModel.GetDeclaredSymbol(cds, ct) is not INamedTypeSymbol sym)
            return null;
        if (!WriteBackGenerator.IsDerivedFromWebWindowModel(sym))
            return null;
        string ns = sym.ContainingNamespace.IsGlobalNamespace ? "" : sym.ContainingNamespace.ToDisplayString();
        return new ModelSourceInfo(sym.Name, ns, ctx.Node.SyntaxTree.ToString());
    }

    /// <summary>全模型已解析表 → 每个模型的 (hintName, CsCode)。GenerateParsed 直接用缓存的
    /// ModelParsed，不重新解析任何源码——全量重算成本从「N 次 Roslyn 解析」降为「N 次字符串拼接」。</summary>
    private static void BuildEmits(SourceProductionContext spc, ImmutableArray<ParsedModel?> parsedModels)
    {
        var all = new Dictionary<string, ParsedModel>(StringComparer.Ordinal);
        foreach (ParsedModel? m in parsedModels)
        {
            if (m is null)
                continue;
            all[m.ClassName] = m;
        }

        foreach (ParsedModel m in all.Values)
        {
            ModelProtoResult result = ModelProtoGenerator.GenerateParsed(m, all, "");
            spc.AddSource($"{m.ClassName}Proto.g.cs", SourceText.From(result.CsCode, Encoding.UTF8));
        }
    }

    /// <summary>全模型清单 → 「类名 → 命名空间」序列（排序保序，EquatableArray 值相等供增量缓存）。</summary>
    private static EquatableArray<KeyValuePair<string, string>> BuildNamespaceMap(ImmutableArray<ModelSourceInfo?> models)
    {
        var pairs = new List<KeyValuePair<string, string>>();
        foreach (ModelSourceInfo? m in models)
        {
            if (m is null)
                continue;
            pairs.Add(new(m.ClassName, m.Namespace));
        }
        pairs.Sort(static (a, b) => StringComparer.Ordinal.Compare(a.Key, b.Key));
        return new EquatableArray<KeyValuePair<string, string>>(pairs.ToArray());
    }

    /// <summary>用「类名 → 命名空间」图解析单个模型；解析失败返回 null（防御，缺该消息时 typed 引用无法解析）。</summary>
    private static ParsedModel? ParseModel(ModelSourceInfo m, EquatableArray<KeyValuePair<string, string>> nsPairs)
    {
        if (nsPairs.Length == 0)
            return null; // 无其它模型：单模型用法，typed repeated 退化 ModelValue 兜底（与 Generate 语义一致）
        var ns = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, string> kv in nsPairs)
            ns[kv.Key] = kv.Value;
        try
        {
            return ModelProtoGenerator.ParseModel(m.SourceText, m.ClassName, ns);
        }
        catch (ArgumentException)
        {
            return null; // 防御：解析失败跳过该模型（与 Generate 的 BuildAllModelFields 容错一致）
        }
    }

    /// <summary>纯数据：类名、命名空间、该类的源码文本（供 ParseModel 解析；transform 阶段只收集不解析）。</summary>
    private sealed record ModelSourceInfo(string ClassName, string Namespace, string SourceText);
}
