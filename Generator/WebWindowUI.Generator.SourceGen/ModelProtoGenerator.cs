using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Text;
using System.Text.Json;

namespace WebWindowUI.Generator;

/// <summary>
/// 生成结果：C# DTO 代码、.proto schema、protobufjs descriptor JSON、TS 模型镜像、命名空间。
/// </summary>
public sealed record ModelProtoResult(
    string CsCode,
    string ProtoText,
    string DescriptorJson,
    string TsCode,
    string Namespace);

/// <summary>
/// 模型 → proto 生成器。读取模型类源码（Roslyn 语法解析，无需编译），
/// 提取 [ObservableProperty] 字段与显式公开属性，按声明顺序生成：
///   - 完整模型消息（package webwindowui.model.generated，标量强类型，object/Dictionary/POCO 用 ModelValue 兜底）；
///   - 增量 update 消息 {模型名}Update：字段与完整模型同序同号，标量/字符串/字节保留原生类型，
///     其余（列表/object/Dictionary/POCO）用 ModelValue 兜底；增量载荷只编码被修改的字段，
///     .NET 侧用可空 DTO（非空即序列化，含 0/空串），前端按 hasOwnProperty 判断字段是否出现。
/// 同时生成对应的 [ProtoContract] 快照/增量 DTO、From(model)/EncodePropertyUpdate 映射
/// 与 {模型名}.EncodeFullSnapshot()/EncodePropertyUpdate() override。
/// 字段号与 DTO [ProtoMember] 号完全一致，前端 descriptor 与 .NET 契约共用同一份映射。
/// List&lt;已知模型&gt;（元素在全模型清单 allModelSources 内）生成强类型 repeated 元素消息
/// （快照 DTO 引用元素快照类型、TS 镜像 Elem[] + import），descriptor 在给出全模型清单时输出
/// 全量集合（任一模型都包含全部模型消息，typed 引用可解析）。
/// 另生成前端 TS 模型镜像（src/models，属性 camelCase、类型映射同 descriptor），
/// 子路径由命名空间去掉「全部模型命名空间的公共前缀」（--all-models 自动推断）经 TsSubPath 推导；
/// 亦可用 --root-namespace 显式覆盖根命名空间。
/// </summary>
public static class ModelProtoGenerator
{
    public const string GeneratedPackage = "webwindowui.model.generated";

    public static ModelProtoResult Generate(
        string sourceText,
        string modelClassName,
        IReadOnlyDictionary<string, string>? allModelSources = null,
        string? rootNs = null)
    {
        IReadOnlyDictionary<string, string>? allNamespaces = BuildNamespaceMap(allModelSources);
        var all = new Dictionary<string, ModelParsed>(StringComparer.Ordinal);
        ModelParsed parsed;
        if (allModelSources is not null)
        {
            // 全模型解析（含目标模型）：typed repeated 元素命名空间 / 全量 descriptor 需要；
            // 单模型解析失败跳过（防御，不影响其余）。
            foreach (KeyValuePair<string, string> kv in allModelSources)
            {
                try
                {
                    all[kv.Key] = ParseModel(kv.Value, kv.Key, allNamespaces);
                }
                catch (ArgumentException)
                {
                }
            }
            parsed = all.TryGetValue(modelClassName, out ModelParsed? p)
                ? p
                : ParseModel(sourceText, modelClassName, allNamespaces);
        }
        else
        {
            parsed = ParseModel(sourceText, modelClassName, null);
        }
        return GenerateParsed(parsed, all, rootNs ?? "");
    }

    /// <summary>
    /// 一次解析的模型元数据（字段/命令/命名空间）。增量管线在 transform 阶段按模型产出并缓存
    /// （键 = 本模型源码 + 全模型「类名→命名空间」值相等图），emit 阶段复用不再重新解析源码（#6）。
    /// </summary>
    internal sealed record ModelParsed(string ClassName, string Namespace, List<ProtoField> Fields, List<ModelCommand> Commands);

    /// <summary>解析一次模型源码 → 轻量元数据。allNamespaces = 全模型「类名 → 命名空间」表
    /// （List&lt;已知模型&gt; typed repeated 检测用；null = 单模型用法，typed repeated 退化 ModelValue 兜底）。</summary>
    internal static ModelParsed ParseModel(string sourceText, string modelClassName,
        IReadOnlyDictionary<string, string>? allNamespaces)
    {
        (string ns, List<ProtoField> fields) = CollectFields(sourceText, modelClassName, allNamespaces);
        return new ModelParsed(modelClassName, ns, fields, CollectCommands(sourceText, modelClassName));
    }

    /// <summary>已解析元数据 + 全模型已解析表 → 生成结果（不重新解析源码）。descriptor 在 all.Count&gt;0 时
    /// 输出全量集合（任一模型都内联全部模型消息，typed 引用可解析），否则只含本模型（兼容单模型用法）。</summary>
    internal static ModelProtoResult GenerateParsed(ModelParsed model, IReadOnlyDictionary<string, ModelParsed> all, string rootNs)
    {
        var fullMessageName = $"{GeneratedPackage}.{model.ClassName}";
        var modelId = ModelIdFor(fullMessageName);
        var descriptorJson = all.Count > 0
            ? BuildDescriptor(BuildAllModelFields(all))
            : BuildDescriptor(new[] { new KeyValuePair<string, List<ProtoField>>(model.ClassName, model.Fields) });

        var allNamespaces = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var kv in all)
            allNamespaces[kv.Key] = kv.Value.Namespace;

        // 完整模型消息 = 数据字段 + 框架保留 modelInstanceId（元素寻址）；Update 消息/TS 镜像/__repeatedFields
        // 只含数据字段（id 永不变化，不进增量载荷与数据契约；序数键仍从 CollectFields 的纯数据字段来）。
        var fullFields = WithInstanceId(model.Fields);
        return new ModelProtoResult(
            CsCode: BuildCs(model.Namespace, model.ClassName, modelId, fullFields, model.Fields),
            ProtoText: BuildProto(model.ClassName, fullFields, model.Fields),
            DescriptorJson: descriptorJson,
            TsCode: BuildTs(model.ClassName, model.Fields, model.Commands, model.Namespace, rootNs, allNamespaces, all, modelId, fullMessageName),
            Namespace: model.Namespace);
    }

    /// <summary>给模型数据字段追加框架保留的 modelInstanceId（int64，字段号 = 数据字段数 + 1）。
    /// 只供「完整模型消息」消费（descriptor 完整消息 + 快照 DTO + .proto 完整消息）：
    /// 前端从线缆拿到每个元素的唯一 ID 用于元素级寻址；Update/TS/序数契约一律不含。
    /// WebWindowModel.ModelInstanceId 是 get-only 非 [ObservableProperty]，永不触发 PropertyChanged。</summary>
    private static List<ProtoField> WithInstanceId(List<ProtoField> fields)
    {
        var full = new List<ProtoField>(fields)
        {
            new("ModelInstanceId", "modelInstanceId", fields.Count + 1, "int64", false,
                "long", "", "model.ModelInstanceId", "long?", "int64", "(long?)value"),
        };
        return full;
    }

    /// <summary>模型序号：完整消息名（package + 类名）的 FNV-1a 32 位哈希，掩到非负 int32。
    /// 线缆上代替冗长的消息名——.NET 的 ModelUpdate/GeneratedModel 只发它，前端经生成器烘焙进
    /// TS 镜像的 __protocol 校验并解码。两侧都由此函数产出（同一生成器），一致性在本模型内即可
    /// （每窗口单模型，前端按自己烘焙的 modelId 解码），跨模型唯一性不要求。</summary>
    private static int ModelIdFor(string fullMessageName)
    {
        uint hash = 2166136261;
        foreach (byte b in Encoding.UTF8.GetBytes(fullMessageName))
        {
            hash ^= b;
            hash *= 16777619;
        }
        return (int)(hash & 0x7FFFFFFF);
    }

    /// <summary>
    /// 全模型「类名 → 命名空间」表（typed repeated 元素解析用）。null 输入 → null。
    /// </summary>
    private static IReadOnlyDictionary<string, string>? BuildNamespaceMap(IReadOnlyDictionary<string, string>? allModelSources)
    {
        if (allModelSources is null)
            return null;
        var ns = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var kv in allModelSources)
            if (GetNamespace(kv.Value) is { } n)
                ns[kv.Key] = n;
        return ns;
    }

    /// <summary>属性名 → proto 字段号（声明顺序 1..N）。POCO 对象 map 的序数键用：WriteBack 生成器
    /// 与前端桥都读同一份编号，避免两生成器各自枚举漂移（与 descriptor 元素消息 field id 同源）。</summary>
    internal static IReadOnlyDictionary<string, int> CollectFieldNumbers(string sourceText, string modelClassName)
        => CollectFields(sourceText, modelClassName, null).Fields.ToDictionary(f => f.CsName, f => f.Number);

    /// <summary>收集一个模型的字段（[ObservableProperty] 只读字段 + 显式公开可读属性，声明序为字段号）。
    /// allNamespaces = 全模型「类名 → 命名空间」表（List&lt;已知模型&gt; typed repeated 元素解析用）。</summary>
    private static (string Namespace, List<ProtoField> Fields) CollectFields(
        string sourceText, string modelClassName, IReadOnlyDictionary<string, string>? allNamespaces)
    {
        var root = CSharpSyntaxTree.ParseText(sourceText).GetRoot();

        ClassDeclarationSyntax? classDecl = root.DescendantNodes()
            .OfType<ClassDeclarationSyntax>()
            .FirstOrDefault(c => c.Identifier.Text == modelClassName)
            ?? throw new ArgumentException($"在源代码中找不到类 {modelClassName}。");

        var ns = FindNamespace(root) ?? "WebWindowUI.Sample";

        // 同文件声明的枚举：ModelValue 兜底字段若是枚举，前端以 number 呈现（而非 object）
        var enumNames = new HashSet<string>(root.DescendantNodes()
            .OfType<EnumDeclarationSyntax>()
            .Select(e => e.Identifier.Text));

        var names = new List<string>();
        var types = new List<string>();
        var docs = new List<string>();

        foreach (var field in classDecl.Members.OfType<FieldDeclarationSyntax>())
        {
            var observable = field.AttributeLists
                .SelectMany(al => al.Attributes)
                .Any(IsObservablePropertyAttribute);
            if (!observable)
                continue;
            var doc = GetDocSummary(field);
            foreach (var v in field.Declaration.Variables)
            {
                var propName = ToPascalCase(v.Identifier.Text.TrimStart('_'));
                if (!names.Contains(propName))
                {
                    names.Add(propName);
                    types.Add(field.Declaration.Type.ToString());
                    docs.Add(doc);
                }
            }
        }

        foreach (var prop in classDecl.Members.OfType<PropertyDeclarationSyntax>())
        {
            var isPublic = prop.Modifiers.Any(m => m.IsKind(SyntaxKind.PublicKeyword));
            var hasGetter = prop.AccessorList?.Accessors.Any(a => a.IsKind(SyntaxKind.GetAccessorDeclaration)) ?? false;
            if (!isPublic || !hasGetter)
                continue;
            if (!names.Contains(prop.Identifier.Text))
            {
                names.Add(prop.Identifier.Text);
                types.Add(prop.Type.ToString());
                docs.Add(GetDocSummary(prop));
            }
        }

        var fields = names.Select((n, i) => Map(n, types[i], i + 1, enumNames, allNamespaces) with { Doc = docs[i] }).ToList();
        return (ns, fields);
    }

    /// <summary>模型上的一个 MVVM 命令：[RelayCommand] 方法 → 前端 TS 方法。Name = .NET 方法名
    /// （线缆 command id），ParamType = 命令方法参数类型（无参为 null）。internal：被 ModelParsed 暴露给
    /// ProtoGenerator（GenerateParsed），须与内部可见性一致。</summary>
    internal sealed record ModelCommand(string Name, string? ParamType, string Doc);

    /// <summary>
    /// 收集模型的 [RelayCommand] 命令方法（MVVM Command）。命令按源声明序编号（0 起，即线缆
    /// commandId——与 WriteBackGenerator 的 switch 同序）；.NET 侧由源生成器产出同名 ICommand
    /// 属性「{方法名}Command」，本生成器把方法映射成前端 TS 方法（camelCase，经桥发 ModelInvoke
    /// 调用 .NET 命令）。
    /// </summary>
    private static List<ModelCommand> CollectCommands(string sourceText, string modelClassName)
    {
        var root = CSharpSyntaxTree.ParseText(sourceText).GetRoot();
        ClassDeclarationSyntax? classDecl = root.DescendantNodes()
            .OfType<ClassDeclarationSyntax>()
            .FirstOrDefault(c => c.Identifier.Text == modelClassName);
        if (classDecl is null)
            return new List<ModelCommand>();

        var commands = new List<ModelCommand>();
        foreach (var method in classDecl.Members.OfType<MethodDeclarationSyntax>())
        {
            bool relay = method.AttributeLists
                .SelectMany(al => al.Attributes)
                .Any(IsRelayCommandAttribute);
            if (!relay)
                continue;
            string? paramType = method.ParameterList.Parameters.Count > 0
                ? method.ParameterList.Parameters[0].Type?.ToString()
                : null;
            commands.Add(new ModelCommand(method.Identifier.Text, paramType, GetDocSummary(method)));
        }
        return commands;
    }

    /// <summary>
    /// 把全模型已解析表收敛成 类名 → 字段 表，供全量 descriptor 使用（无需重新解析源码）。
    /// </summary>
    private static IReadOnlyList<KeyValuePair<string, List<ProtoField>>> BuildAllModelFields(
        IReadOnlyDictionary<string, ModelParsed> allParsed)
    {
        var all = new List<KeyValuePair<string, List<ProtoField>>>();
        foreach (KeyValuePair<string, ModelParsed> kv in allParsed)
            all.Add(new(kv.Key, kv.Value.Fields));
        return all;
    }

    // ---- 类型映射 ----

    /// <summary>
    /// 字段映射元数据。internal：被 ModelParsed 暴露给 ProtoGenerator（GenerateParsed/BuildEmits）。
    /// </summary>
    internal sealed record ProtoField(
        string CsName,     // C# DTO 属性名（PascalCase）
        string WireName,   // proto/descriptor 字段名（camelCase，与 TS 模型一致）
        int Number,
        string ProtoType,  // string/int32/int64/double/float/bool/bytes/ModelValue/元素模型名（完整模型）
        bool IsRepeated,
        string DtoType,    // 完整快照 DTO 属性类型（含可空）
        string DtoInit,    // 完整快照初始化后缀（"" | " = \"\";" | " = new();" | " = null;"）
        string MapExpr,    // From() 的映射表达式（不含 "属性名 = " 前缀）
        string UpdDtoType, // 增量 update DTO 属性类型（可空 → 显式 presence）
        string UpdProtoType, // 增量 update 消息字段类型（ModelValue 表示兜底）
        string UpdSetExpr, // EncodePropertyUpdate 的赋值表达式（不含 "属性名 = " 前缀）
        string Doc = "",   // C# XML <summary> 摘要（TS 属性注释）
        bool IsEnum = false, // C# 类型是枚举 → TS 以 number 呈现（ModelValue 兜底字段）
        string? TsElem = null); // List<已知模型> → 元素 TS 类名（强类型 repeated，TS 侧 Elem[] + import）

    private static ProtoField Map(string csName, string csType, int number, IReadOnlyCollection<string> enumNames,
        IReadOnlyDictionary<string, string>? allNamespaces)
    {
        var t = csType.Trim();
        var bare = t.TrimEnd('?');
        var nullable = t.EndsWith("?");
        var wire = ToCamelCase(csName);
        var (updDto, updProto, updSet) = UpdateVariant(bare);

        switch (bare)
        {
            case "string":
                return new ProtoField(csName, wire, number, "string", false,
                    nullable ? "string?" : "string", nullable ? " = null;" : " = \"\";",
                    $"model.{csName}", updDto, updProto, updSet);
            case "int": return Scalar(csName, wire, number, "int32", "int", updDto, updProto, updSet);
            case "long": return Scalar(csName, wire, number, "int64", "long", updDto, updProto, updSet);
            case "double": return Scalar(csName, wire, number, "double", "double", updDto, updProto, updSet);
            case "float": return Scalar(csName, wire, number, "float", "float", updDto, updProto, updSet);
            case "bool": return Scalar(csName, wire, number, "bool", "bool", updDto, updProto, updSet);
            case "byte[]":
                return new ProtoField(csName, wire, number, "bytes", false, "byte[]?", " = null;",
                    $"model.{csName}", updDto, updProto, updSet);
            case "DateTime":
                return new ProtoField(csName, wire, number, "string", false, "string?", " = null;",
                    $"model.{csName}.ToString(\"O\", System.Globalization.CultureInfo.InvariantCulture)", updDto, updProto, updSet);
            case "DateTimeOffset":
                return new ProtoField(csName, wire, number, "string", false, "string?", " = null;",
                    $"model.{csName}.ToString(\"O\", System.Globalization.CultureInfo.InvariantCulture)", updDto, updProto, updSet);
            case "TimeSpan":
                return new ProtoField(csName, wire, number, "string", false, "string?", " = null;",
                    $"model.{csName}.ToString(\"c\", System.Globalization.CultureInfo.InvariantCulture)", updDto, updProto, updSet);
            case "Guid":
                return new ProtoField(csName, wire, number, "string", false, "string?", " = null;",
                    $"model.{csName}.ToString()", updDto, updProto, updSet);
            case "char":
                return new ProtoField(csName, wire, number, "string", false, "string?", " = null;",
                    $"model.{csName}.ToString()", updDto, updProto, updSet);
            case "decimal":
                return Scalar(csName, wire, number, "double", "decimal", updDto, updProto, updSet);
        }

        // List<T> / ObservableCollection<T> / T[] / 常见集合接口：标量元素 → repeated；object 元素 → repeated ModelValue
        string? listElem = null;
        if (bare.EndsWith(">"))
        {
            foreach (var prefix in new[] { "List<", "ObservableCollection<", "IList<", "ICollection<", "IReadOnlyList<", "IReadOnlyCollection<", "IEnumerable<" })
            {
                if (bare.StartsWith(prefix))
                {
                    listElem = bare.Substring(prefix.Length, bare.Length - prefix.Length - 1);
                    break;
                }
            }
        }
        if (listElem is null && bare.EndsWith("]") && bare != "byte[]")
            listElem = bare.Substring(0, bare.Length - 2);

        if (listElem is not null)
        {
            var elemBare = listElem.TrimEnd('?');
            var (pt, dtoElem, isModelValue) = ElemMap(elemBare);
            if (!isModelValue)
                return new ProtoField(csName, wire, number, pt, true, $"List<{dtoElem}>", " = new();",
                    $"model.{csName}?.ToList() ?? new()", updDto, updProto, updSet);
            // List<已知模型> → typed repeated（强类型）：完整快照走 repeated 元素模型消息，
            // 快照 DTO 引用元素模型的快照类型（全限定跨命名空间），TS 镜像 Elem[] + import。
            // 命名空间从「类名 → 命名空间」表取（值相等缓存：改其它模型字段不重算本模型解析）。
            if (allNamespaces is not null && allNamespaces.TryGetValue(elemBare, out var elemNs))
            {
                var snapType = $"{elemNs}.{elemBare}Snapshot";
                return new ProtoField(csName, wire, number, elemBare, true,
                    $"List<{snapType}>", " = new();",
                    $"(model.{csName} ?? new()).Select(x => {snapType}.From(x)).ToList()", updDto, updProto, updSet,
                    TsElem: elemBare);
            }
            var elemIsEnum = enumNames.Contains(elemBare.Split('.').Last());
            return new ProtoField(csName, wire, number, "ModelValue", true, "List<ModelValue>", " = new();",
                $"(model.{csName} ?? System.Linq.Enumerable.Empty<object>()).Select(x => ModelProtocol.ToModelValue(x)).ToList()", updDto, updProto, updSet,
                IsEnum: elemIsEnum);
        }

        // 其它（object/Dictionary/POCO/枚举 等）→ ModelValue 兜底（枚举 → TS number）
        return new ProtoField(csName, wire, number, "ModelValue", false, "ModelValue?", " = null;",
            $"ModelProtocol.ToModelValue(model.{csName})", updDto, updProto, updSet,
            IsEnum: enumNames.Contains(bare.Split('.').Last()));
    }

    private static ProtoField Scalar(string csName, string wire, int number, string protoType, string csType,
        string updDto, string updProto, string updSet)
        => new(csName, wire, number, protoType, false, csType, "", $"model.{csName}", updDto, updProto, updSet);

    private static (string ProtoType, string DtoElem, bool IsModelValue) ElemMap(string bare) => bare switch
    {
        "string" => ("string", "string", false),
        "int" => ("int32", "int", false),
        "long" => ("int64", "long", false),
        "double" => ("double", "double", false),
        "float" => ("float", "float", false),
        "bool" => ("bool", "bool", false),
        _ => ("ModelValue", "object", true),
    };

    /// <summary>
    /// 增量 update 消息的字段变体：标量/字符串/字节保留原生类型但改可空（显式 presence，
    /// 非空即序列化，含 0/空串）；其余（列表/object/Dictionary/POCO/DateTime 映射之外的复杂值）
    /// 一律 ModelValue 兜底（message 类型天然有 presence，且空列表也能表达"已清空"）。
    /// </summary>
    private static (string Dto, string Proto, string Set) UpdateVariant(string bare) => bare switch
    {
        "string" => ("string?", "string", "(string?)value"),
        "int" => ("int?", "int32", "(int?)value"),
        "long" => ("long?", "int64", "(long?)value"),
        "double" => ("double?", "double", "(double?)value"),
        "float" => ("float?", "float", "(float?)value"),
        "bool" => ("bool?", "bool", "(bool?)value"),
        "byte[]" => ("byte[]?", "bytes", "(byte[]?)value"),
        "DateTime" => ("string?", "string",
            "value is null ? null : ((DateTime)value).ToString(\"O\", System.Globalization.CultureInfo.InvariantCulture)"),
        "DateTimeOffset" => ("string?", "string",
            "value is null ? null : ((DateTimeOffset)value).ToString(\"O\", System.Globalization.CultureInfo.InvariantCulture)"),
        "TimeSpan" => ("string?", "string",
            "value is null ? null : ((TimeSpan)value).ToString(\"c\", System.Globalization.CultureInfo.InvariantCulture)"),
        "Guid" => ("string?", "string", "value is null ? null : ((Guid)value).ToString()"),
        "char" => ("string?", "string", "value is null ? null : ((char)value).ToString()"),
        "decimal" => ("decimal?", "double", "(decimal?)value"),
        _ => ("ModelValue?", "ModelValue", "ModelProtocol.ToModelValue(value)"),
    };

    // ---- 产出 ----

    private static string BuildProto(string modelClassName, List<ProtoField> fullFields, List<ProtoField> updateFields)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// 由 WebWindowUI.Generator 自动生成：完整模型消息 + 增量 update 消息。修改模型类后重新构建。");
        sb.AppendLine("// 参考文档（不落盘）：前端实际解析用的 protobufjs descriptor 把基础信封 WebMessage/ModelValue 内联进");
        sb.AppendLine("// 每个模型 descriptor（见 BuildDescriptor），前端 Root.fromJSON 自包含解析，无需独立的 model.proto。");
        sb.AppendLine("syntax = \"proto3\";");
        sb.AppendLine();
        sb.AppendLine($"package {GeneratedPackage};");
        sb.AppendLine();
        sb.AppendLine($"message {modelClassName} {{");
        foreach (var f in fullFields)
        {
            var typeRef = f.ProtoType == "ModelValue" ? "webwindowui.model.ModelValue" : f.ProtoType;
            var rep = f.IsRepeated ? "repeated " : "";
            sb.AppendLine($"  {rep}{typeRef} {f.WireName} = {f.Number};");
        }
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine($"// 增量 update：字段与完整模型同序同号（不含框架保留 modelInstanceId，它永不变更），");
        sb.AppendLine($"// 标量/字符串/字节保留原生类型，其余用 ModelValue 兜底。");
        sb.AppendLine($"// 载荷只编码被修改的字段（.NET 侧可空 DTO，非空即序列化），前端按字段是否出现做增量应用。");
        sb.AppendLine($"message {modelClassName}Update {{");
        foreach (var f in updateFields)
        {
            var typeRef = f.UpdProtoType == "ModelValue" ? "webwindowui.model.ModelValue" : f.UpdProtoType;
            sb.AppendLine($"  {typeRef} {f.WireName} = {f.Number};");
        }
        sb.AppendLine("}");
        return sb.ToString();
    }

    /// <summary>构建 protobufjs descriptor：把基础信封消息（WebMessage/ModelValue 等）与传入的每个模型
    /// （及其 Update 消息，塞进 webwindowui.model.generated 命名空间）一起写进 webwindowui.model 命名空间。
    /// 全模型清单时传全部模型 → 任一模型的 descriptor 都能解析 typed repeated 字段引用的元素消息。
    /// 基础信封内联进每个模型 descriptor → 前端解析自包含，不再需要单独的 model.json/model.proto。</summary>
    private static string BuildDescriptor(IEnumerable<KeyValuePair<string, List<ProtoField>>> models)
    {
        var nested = new Dictionary<string, object?>();
        foreach (KeyValuePair<string, List<ProtoField>> m in models)
        {
            // 完整消息带框架保留 modelInstanceId（元素寻址），Update 消息只含数据字段（id 永不变更）。
            nested[m.Key] = BuildMessageFields(WithInstanceId(m.Value));
            nested[m.Key + "Update"] = BuildUpdateFields(m.Value);
        }

        var generatedNs = new Dictionary<string, object?>
        {
            ["nested"] = nested,
        };
        var modelNsNested = new Dictionary<string, object?> { ["generated"] = generatedNs };
        foreach (KeyValuePair<string, object?> kv in BuildBaseEnvelopeMessages())
            modelNsNested[kv.Key] = kv.Value;
        var modelNs = new Dictionary<string, object?>
        {
            ["nested"] = modelNsNested,
        };
        var wwNs = new Dictionary<string, object?>
        {
            ["nested"] = new Dictionary<string, object?> { ["model"] = modelNs },
        };
        var root = new Dictionary<string, object?>
        {
            ["nested"] = new Dictionary<string, object?> { ["webwindowui"] = wwNs },
        };

        return JsonSerializer.Serialize(root, new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>基础信封消息（线缆骨架）：WebMessage 信封 + ModelValue 通用值 + 各信封成员。
    /// 字段号必须与 ModelProtocol.cs 的 [ProtoMember] 严格一致，由 ModelProtoTests 的漂移测试锁住。
    /// 内联进每个模型 descriptor 后前端用 Root.fromJSON 直接解析，不再需要单独的 model.json。</summary>
    private static Dictionary<string, object?> BuildBaseEnvelopeMessages()
    {
        // ModelValue：通用值，增量/快照/回写共用。oneof kind 同时只命中一个成员。
        var modelValue = new Dictionary<string, object?>
        {
            ["fields"] = new Dictionary<string, object?>
            {
                ["number"] = new Dictionary<string, object?> { ["type"] = "double", ["id"] = 1, ["oneof"] = "kind" },
                ["text"] = new Dictionary<string, object?> { ["type"] = "string", ["id"] = 2, ["oneof"] = "kind" },
                ["flag"] = new Dictionary<string, object?> { ["type"] = "bool", ["id"] = 3, ["oneof"] = "kind" },
                ["list"] = new Dictionary<string, object?> { ["type"] = "ModelValueList", ["id"] = 4, ["oneof"] = "kind" },
                ["object"] = new Dictionary<string, object?> { ["type"] = "ModelValueMap", ["id"] = 5, ["oneof"] = "kind" },
                ["blob"] = new Dictionary<string, object?> { ["type"] = "bytes", ["id"] = 6, ["oneof"] = "kind" },
            },
            ["oneofs"] = new Dictionary<string, object?>
            {
                ["kind"] = new Dictionary<string, object?> { ["oneof"] = new[] { "number", "text", "flag", "list", "object", "blob" } },
            },
        };
        var modelValueList = new Dictionary<string, object?>
        {
            ["fields"] = new Dictionary<string, object?>
            {
                ["items"] = new Dictionary<string, object?> { ["rule"] = "repeated", ["type"] = "ModelValue", ["id"] = 1 },
            },
        };
        var modelValueMap = new Dictionary<string, object?>
        {
            ["fields"] = new Dictionary<string, object?>
            {
                ["fields"] = new Dictionary<string, object?> { ["keyType"] = "string", ["type"] = "ModelValue", ["id"] = 1 },
                // typed POCO 序数键（proto 字段号 int）：与 ModelValueMap.OrdinalFields [ProtoMember(2)] 对拍
                ["ordinalFields"] = new Dictionary<string, object?> { ["keyType"] = "int32", ["type"] = "ModelValue", ["id"] = 2 },
            },
        };
        // 前端→.NET：页面脚本就绪，请求补发完整快照
        var modelReady = new Dictionary<string, object?> { ["fields"] = new Dictionary<string, object?>() };
        // .NET→前端：单属性增量。payload 是生成器为模型产出的 update 消息字节，modelId 是模型序号
        // （FNV-1a 哈希，前端经烘焙 __protocol 校验并解码类型）。
        var modelUpdate = new Dictionary<string, object?>
        {
            ["fields"] = new Dictionary<string, object?>
            {
                ["modelId"] = new Dictionary<string, object?> { ["type"] = "int32", ["id"] = 1 },
                ["payload"] = new Dictionary<string, object?> { ["type"] = "bytes", ["id"] = 2 },
            },
        };
        // 前端→.NET：回写单个属性；elementProperty 非空时是集合元素级回写
        // （property=集合、elementInstanceId=目标元素、value=该元素属性新值）
        var modelSet = new Dictionary<string, object?>
        {
            ["fields"] = new Dictionary<string, object?>
            {
                ["property"] = new Dictionary<string, object?> { ["type"] = "string", ["id"] = 1 },
                ["value"] = new Dictionary<string, object?> { ["type"] = "ModelValue", ["id"] = 2 },
                ["elementInstanceId"] = new Dictionary<string, object?> { ["type"] = "int64", ["id"] = 3 },
                ["elementProperty"] = new Dictionary<string, object?> { ["type"] = "string", ["id"] = 4 },
            },
        };
        // 前端→.NET：执行模型命令（MVVM Command，[RelayCommand] 生成的 ICommand）。
        // commandId = 命令序号（[RelayCommand] 方法声明序，.NET 与 TS 镜像一致）；value 为参数（可空）。
        var modelInvoke = new Dictionary<string, object?>
        {
            ["fields"] = new Dictionary<string, object?>
            {
                ["commandId"] = new Dictionary<string, object?> { ["type"] = "int32", ["id"] = 1 },
                ["value"] = new Dictionary<string, object?> { ["type"] = "ModelValue", ["id"] = 2 },
            },
        };
        // .NET→前端：无生成编码器模型的通用完整快照（property → ModelValue）
        var modelSnapshot = new Dictionary<string, object?>
        {
            ["fields"] = new Dictionary<string, object?>
            {
                ["data"] = new Dictionary<string, object?> { ["keyType"] = "string", ["type"] = "ModelValue", ["id"] = 1 },
            },
        };
        // .NET→前端：完整模型消息（初始快照用生成器产出的消息；payload = 生成消息的 protobuf 字节）
        var generatedModel = new Dictionary<string, object?>
        {
            ["fields"] = new Dictionary<string, object?>
            {
                ["modelId"] = new Dictionary<string, object?> { ["type"] = "int32", ["id"] = 1 },
                ["payload"] = new Dictionary<string, object?> { ["type"] = "bytes", ["id"] = 2 },
            },
        };
        // .NET→前端：集合增删差量补丁（前端对响应式数组原地 splice；Reset 时 Items 承载整列表整体替换）。
        // action 枚举取值与 ModelProtocol.CollectionPatchAction 严格一致（1=Insert 2=Remove 3=Replace 4=Move 5=Reset 6=ElementSet）。
        var collectionPatchAction = new Dictionary<string, object?>
        {
            ["values"] = new Dictionary<string, object?>
            {
                ["Insert"] = 1,
                ["Remove"] = 2,
                ["Replace"] = 3,
                ["Move"] = 4,
                ["Reset"] = 5,
                ["ElementSet"] = 6,
            },
        };
        var collectionPatch = new Dictionary<string, object?>
        {
            ["fields"] = new Dictionary<string, object?>
            {
                ["action"] = new Dictionary<string, object?> { ["type"] = "CollectionPatchAction", ["id"] = 1 },
                ["property"] = new Dictionary<string, object?> { ["type"] = "string", ["id"] = 2 },
                ["index"] = new Dictionary<string, object?> { ["type"] = "int32", ["id"] = 3 },
                ["count"] = new Dictionary<string, object?> { ["type"] = "int32", ["id"] = 4 },
                ["items"] = new Dictionary<string, object?> { ["rule"] = "repeated", ["type"] = "ModelValue", ["id"] = 5 },
                ["fromIndex"] = new Dictionary<string, object?> { ["type"] = "int32", ["id"] = 6 },
                // ElementSet：目标元素（ModelInstanceId）+ 被改属性 + 新值
                ["elementInstanceId"] = new Dictionary<string, object?> { ["type"] = "int64", ["id"] = 7 },
                ["elementProperty"] = new Dictionary<string, object?> { ["type"] = "string", ["id"] = 8 },
                ["elementValue"] = new Dictionary<string, object?> { ["type"] = "ModelValue", ["id"] = 9 },
            },
        };
        // 信封：所有 postMessage 载荷都是它，oneof payload 同时只命中一个成员
        var webMessage = new Dictionary<string, object?>
        {
            ["fields"] = new Dictionary<string, object?>
            {
                ["ready"] = new Dictionary<string, object?> { ["type"] = "ModelReady", ["id"] = 1, ["oneof"] = "payload" },
                ["update"] = new Dictionary<string, object?> { ["type"] = "ModelUpdate", ["id"] = 2, ["oneof"] = "payload" },
                ["set"] = new Dictionary<string, object?> { ["type"] = "ModelSet", ["id"] = 3, ["oneof"] = "payload" },
                ["snapshot"] = new Dictionary<string, object?> { ["type"] = "ModelSnapshot", ["id"] = 4, ["oneof"] = "payload" },
                ["full"] = new Dictionary<string, object?> { ["type"] = "GeneratedModel", ["id"] = 5, ["oneof"] = "payload" },
                ["invoke"] = new Dictionary<string, object?> { ["type"] = "ModelInvoke", ["id"] = 6, ["oneof"] = "payload" },
                ["patch"] = new Dictionary<string, object?> { ["type"] = "CollectionPatch", ["id"] = 7, ["oneof"] = "payload" },
                // 实例唯一 ID（int64，进程内单调自增）：统一信封 header，不进 oneof payload。
                // 前端桥从首个 full/snapshot 捕获并暴露为 model._modelInstanceId，对 update/patch 做
                // 防串守卫（旧实例在途消息丢弃）；ready/set/invoke 回传同字段，.NET 侧校验来源实例。
                ["modelInstanceId"] = new Dictionary<string, object?> { ["type"] = "int64", ["id"] = 8 },
            },
            ["oneofs"] = new Dictionary<string, object?>
            {
                ["payload"] = new Dictionary<string, object?> { ["oneof"] = new[] { "ready", "update", "set", "snapshot", "full", "invoke", "patch" } },
            },
        };
        return new Dictionary<string, object?>
        {
            ["ModelValue"] = modelValue,
            ["ModelValueList"] = modelValueList,
            ["ModelValueMap"] = modelValueMap,
            ["ModelReady"] = modelReady,
            ["ModelUpdate"] = modelUpdate,
            ["ModelSet"] = modelSet,
            ["ModelInvoke"] = modelInvoke,
            ["ModelSnapshot"] = modelSnapshot,
            ["GeneratedModel"] = generatedModel,
            ["CollectionPatch"] = collectionPatch,
            ["CollectionPatchAction"] = collectionPatchAction,
            ["WebMessage"] = webMessage,
        };
    }

    private static Dictionary<string, object?> BuildMessageFields(List<ProtoField> fields)
    {
        var fieldJson = new Dictionary<string, object?>();
        foreach (ProtoField f in fields)
        {
            var entry = new Dictionary<string, object?> { ["id"] = f.Number };
            if (f.IsRepeated)
                entry["rule"] = "repeated";
            entry["type"] = f.ProtoType == "ModelValue" ? "webwindowui.model.ModelValue" : f.ProtoType;
            fieldJson[f.WireName] = entry;
        }
        return new Dictionary<string, object?> { ["fields"] = fieldJson };
    }

    private static Dictionary<string, object?> BuildUpdateFields(List<ProtoField> fields)
    {
        var updFieldJson = new Dictionary<string, object?>();
        foreach (ProtoField f in fields)
        {
            updFieldJson[f.WireName] = new Dictionary<string, object?>
            {
                ["id"] = f.Number,
                ["type"] = f.UpdProtoType == "ModelValue" ? "webwindowui.model.ModelValue" : f.UpdProtoType,
            };
        }
        return new Dictionary<string, object?> { ["fields"] = updFieldJson };
    }

    /// <summary>生成 TS 模型镜像：与 src/bridge descriptor 同源（camelCase 属性名 + 相同类型映射）。
    /// 属性默认值为类型空值（快照到达前展示用）。List&lt;已知模型&gt; → 元素类型 Elem[]，并 import 元素模型的
    /// TS 文件（相对路径按子路径推导）。带 [RelayCommand] 方法的模型再产出命令方法（openWindow()/带参
    /// commandWithArg(arg)），类继承 webwindowui-bridge 的 ModelCommandHost 基类（命令通道类型契约在上层库，
    /// 由 bindModel 注入为不可枚举实例属性），命令方法经 this._commandChannel 发 ModelInvoke 调 .NET 命令。
    /// 末尾生成 bind{模型名}() 助手：把「创建实例 + 传 descriptor 给 webwindowui-bridge 的 bindModel」
    /// 封成一个函数，页面只需 import 并调用。
    /// typed-repeated 属性（List&lt;已知模型&gt;）再烘焙静态 ['__repeatedFields'] 序数键契约：元素消息的
    /// 「proto 字段号 → 字段名」表构建期定死进 TS 镜像，桥直接读取、不做运行时 constructor.name 反射
    /// （class 名会被 JS 压缩器改名，运行时反射必失真——Release 下 typed-repeated 补丁挂的根因）。</summary>
    private static string BuildTs(string modelClassName, List<ProtoField> fields, List<ModelCommand> commands,
        string ns, string rootNs, IReadOnlyDictionary<string, string>? allNamespaces,
        IReadOnlyDictionary<string, ModelParsed> all, int modelId, string fullMessageName)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated>");
        sb.AppendLine($"// 由 WebWindowUI.Generator 自动生成：.NET 模型 {modelClassName} 的前端 TS 镜像。");
        sb.AppendLine("// 类型映射：.NET string→string、int/long/double/float→number、bool→boolean、byte[]→Uint8Array、");
        sb.AppendLine("// DateTime/Guid/TimeSpan→string、List<T>/T[]→T[]、List<模型>→模型[]、object/Dictionary/枚举→Record<string, unknown>|number。");
        sb.AppendLine("// 修改模型类后重新构建即可更新，请勿手动编辑。");
        sb.AppendLine("// </auto-generated>");
        sb.AppendLine();
        var mySub = TsSubPath(ns, rootNs);
        // 桥绑定：bindModel / ModelCommandHost 来自 webwindowui-bridge（带命令的模型继承宿主基类承载命令通道类型契约，
        // 无命令模型只 import bindModel），descriptor（含基础信封，自包含）来自 src/bridge/<ProtoBase>.json
        var hasCommands = commands.Count > 0;
        sb.AppendLine(hasCommands
            ? "import { bindModel, ModelCommandHost } from 'webwindowui-bridge';"
            : "import { bindModel } from 'webwindowui-bridge';");
        sb.AppendLine($"import descriptorJson from '{RelativeBridgeJsonImport(mySub, modelClassName)}';");
        foreach (var elem in fields.Select(f => f.TsElem).Where(e => e is not null).Cast<string>().Distinct())
        {
            var elemNs = allNamespaces is not null && allNamespaces.TryGetValue(elem, out var en) ? en : ns;
            sb.AppendLine($"import {{ {elem} }} from '{RelativeTsImport(mySub, TsSubPath(elemNs, rootNs), elem)}';");
        }
        if (fields.Any(f => f.TsElem is not null))
            sb.AppendLine();
        sb.AppendLine(hasCommands
            ? $"export class {modelClassName} extends ModelCommandHost {{"
            : $"export class {modelClassName} {{");
        // 线缆协议契约：modelId 代替消息名（ModelUpdate/GeneratedModel 只发序号），full/update 是
        // descriptor 消息类型名（桥解码用）。构建期定死、桥直接读取，字符串字面量键压缩器不改写。
        sb.AppendLine("  /** 线缆协议契约（构建期定死、桥直接读取）：modelId = 模型序号（线缆上代替消息名），");
        sb.AppendLine("      full/update = descriptor 消息类型名（解码用）。字符串字面量键：压缩器不改写。 */");
        sb.AppendLine($"  static ['__protocol'] = {{ modelId: {modelId}, full: '{fullMessageName}', update: '{fullMessageName}Update' }}");
        // typed-repeated 序数键烘焙：属性名 → { 元素 proto 字段号: 元素属性名 }（元素字段号 = 元素模型声明序）。
        // 元素模型不在全模型表（单模型用法）或非 typed repeated → 不烘焙（typed repeated 已退化 ModelValue 兜底）。
        var repeatedByNumber = new List<(string Prop, List<KeyValuePair<int, string>> Fields)>();
        foreach (var f in fields)
        {
            if (f.TsElem is null || !all.TryGetValue(f.TsElem, out ModelParsed? elem))
                continue;
            var byNumber = new List<KeyValuePair<int, string>>();
            foreach (var ef in elem.Fields)
                byNumber.Add(new(ef.Number, ef.WireName));
            repeatedByNumber.Add((f.WireName, byNumber));
        }
        if (repeatedByNumber.Count > 0)
        {
            sb.AppendLine("  /** typed-repeated 序数键契约：属性名 → { proto 字段号: 元素属性名 }（与 .NET");
            sb.AppendLine("      ConvertToModelValue/ConvertFromModelValue 的 ordinalFields int 键对称）。构建期");
            sb.AppendLine("     烘焙、桥直接读取，不做运行时 constructor.name 反射（class 名会被压缩器改名）。");
            sb.AppendLine("     声明与访问均用字符串字面量键：minifier 不改写字面量。 */");
            sb.AppendLine("  static ['__repeatedFields'] = {");
            foreach (var (prop, byNumber) in repeatedByNumber)
            {
                var inner = string.Join(", ", byNumber.Select(kv => $"{kv.Key}: '{kv.Value}'"));
                sb.AppendLine($"    {prop}: {{ {inner} }},");
            }
            sb.AppendLine("  }");
            sb.AppendLine();
        }
        foreach (var f in fields)
        {
            if (string.IsNullOrEmpty(f.Doc))
                sb.AppendLine($"  /** {f.WireName} */");
            else
                sb.AppendLine($"  /** {f.WireName}：{f.Doc} */");
            sb.AppendLine($"  {f.WireName}: {TsType(f)} = {TsInit(f)}");
            sb.AppendLine();
        }
        int cmdId = 0;
        foreach (var c in commands)
        {
            if (string.IsNullOrEmpty(c.Doc))
                sb.AppendLine($"  /** {c.Name} */");
            else
                sb.AppendLine($"  /** {c.Name}：{c.Doc} */");
            var tsName = ToCamelCase(c.Name);
            sb.AppendLine(c.ParamType is null
                ? $"  {tsName}(): void {{ this._commandChannel?.({cmdId}) }}"
                : $"  {tsName}(arg: {TsCommandParamType(c.ParamType)}): void {{ this._commandChannel?.({cmdId}, arg) }}");
            cmdId++;
        }
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine($"/** 绑定 {modelClassName}：创建实例并经 webwindowui-bridge 连接 .NET 双向绑定（descriptor 已含基础信封）。 */");
        sb.AppendLine($"export function bind{modelClassName}(): {modelClassName} {{");
        sb.AppendLine($"  return bindModel(new {modelClassName}(), descriptorJson);");
        sb.AppendLine("}");
        return sb.ToString();
    }

    /// <summary>
    /// TS 属性类型：repeated → T[]；List&lt;模型&gt; → 元素模型[]；ModelValue 兜底按是否枚举取 number / Record&lt;string, unknown&gt;。
    /// </summary>
    private static string TsType(ProtoField f)
    {
        if (f.IsRepeated)
        {
            if (f.TsElem is not null)
                return f.TsElem + "[]";
            return f.ProtoType == "ModelValue"
                ? (f.IsEnum ? "number[]" : "unknown[]")
                : TsScalar(f.ProtoType) + "[]";
        }
        if (f.ProtoType == "ModelValue")
            return f.IsEnum ? "number" : "Record<string, unknown>";
        return TsScalar(f.ProtoType);
    }

    /// <summary>
    /// 命令方法参数类型 → TS 参数类型（标量映射，其它复杂参数按 unknown 透传）。
    /// </summary>
    private static string TsCommandParamType(string? csType)
    {
        var bare = (csType ?? "").Trim().TrimEnd('?');
        return bare switch
        {
            "string" => "string",
            "int" or "long" or "short" or "byte" or "sbyte" or "uint" or "ulong" or "ushort"
                or "double" or "float" or "decimal" => "number",
            "bool" => "boolean",
            "byte[]" => "Uint8Array",
            _ => "unknown",
        };
    }

    private static string TsScalar(string protoType) => protoType switch
    {
        "string" => "string",
        "int32" or "int64" or "double" or "float" => "number",
        "bool" => "boolean",
        "bytes" => "Uint8Array",
        _ => "unknown",
    };

    private static string TsInit(ProtoField f)
    {
        if (f.IsRepeated) return "[]";
        return f.ProtoType switch
        {
            "string" => "''",
            "int32" or "int64" or "double" or "float" => "0",
            "bool" => "false",
            "bytes" => "new Uint8Array()",
            _ => f.IsEnum ? "0" : "{}",
        };
    }

    /// <summary>
    /// 提取 C# XML 文档注释的 &lt;summary&gt; 摘要（供 TS 属性注释用）。取声明节点 leading trivia 里的
    /// 单行文档注释，无则返回空串。
    /// </summary>
    private static string GetDocSummary(SyntaxNode node)
    {
        foreach (var trivia in node.GetLeadingTrivia())
        {
            if (!trivia.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia))
                continue;
            var text = trivia.ToFullString();
            var start = text.IndexOf("<summary>", StringComparison.Ordinal);
            var end = text.IndexOf("</summary>", StringComparison.Ordinal);
            if (start < 0 || end <= start)
                continue;
            var body = text.Substring(start + "<summary>".Length, end - start - "<summary>".Length);
            var lines = body
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.TrimStart().TrimStart('/').Trim());
            return string.Join(" ", lines).Trim();
        }
        return "";
    }

    /// <summary>
    /// 命名空间 → TS 模型子路径：去掉根命名空间前缀，剩余段小写后以 '/' 连接。
    /// 与根命名空间相同 → ""（落在 src/models 根）；前缀不匹配 → ""（安全回退，不生成子目录）。
    /// </summary>
    public static string TsSubPath(string ns, string rootNs)
    {
        if (string.IsNullOrEmpty(rootNs) || string.IsNullOrEmpty(ns))
            return "";
        if (ns == rootNs)
            return "";
        if (ns.StartsWith(rootNs + ".", StringComparison.Ordinal))
            return string.Join("/", ns.Substring(rootNs.Length + 1).Split('.').Select(s => s.ToLowerInvariant()));
        return "";
    }

    /// <summary>
    /// 两个 TS 模型子路径（src/models 下相对目录）之间的相对 import 路径：同目录 ./X、跨目录 ../ 补全。
    /// </summary>
    private static string RelativeTsImport(string fromSubPath, string toSubPath, string className)
    {
        var from = fromSubPath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
        var to = toSubPath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
        int common = 0;
        while (common < from.Length && common < to.Length && from[common] == to[common])
            common++;
        var parts = new List<string>();
        for (int i = common; i < from.Length; i++)
            parts.Add("..");
        for (int i = common; i < to.Length; i++)
            parts.Add(to[i]);
        parts.Add(className);
        return "./" + string.Join("/", parts);
    }

    /// <summary>src/models/&lt;子路径&gt;/ 下模型 TS 文件 → src/bridge/&lt;ProtoBase&gt;.json 的相对 import 路径：
    /// 根子路径 → ../bridge/…，嵌套子路径每层加一层 ../。descriptor 与 TS 是 src/ 下的兄弟目录。</summary>
    private static string RelativeBridgeJsonImport(string subPath, string modelClassName)
    {
        int depth = subPath.Length == 0 ? 0 : subPath.Split('/').Length;
        return string.Concat(Enumerable.Repeat("../", depth + 1)) + "bridge/" + ProtoBase(modelClassName) + ".json";
    }

    /// <summary>类名 → proto 文件基名（PascalCase → snake_case，与 targets 的 ProtoBase 推导同规则：
    /// TodoItemModel → todo_item_model）。</summary>
    private static string ProtoBase(string className)
        => System.Text.RegularExpressions.Regex.Replace(className, "([a-z0-9])([A-Z])", "$1_$2").ToLowerInvariant();

    /// <summary>提取源码里的命名空间（复用 FindNamespace 的解析），供 --all-models 公共前缀推断用。
    /// 文件级/块级命名空间都支持；无命名空间返回 null。</summary>
    public static string? GetNamespace(string sourceText)
    {
        var root = CSharpSyntaxTree.ParseText(sourceText).GetRoot();
        return FindNamespace(root);
    }

    /// <summary>
    /// 全部模型命名空间的最长公共前缀（按段边界）：作为 TS 模型子路径的根命名空间基准，
    /// 配合 TsSubPath 推导「命名空间 − 公共前缀」的子路径。空集合返回 ""。
    /// 例：["WebWindowUI.Sample","WebWindowUI.Sample.Users"] → "WebWindowUI.Sample"（Users 模型 → users/）；
    ///     ["A.B","A.C"] → "A"；["A.B","C.D"] → ""（无公共段 → 全部落根）。
    /// </summary>
    public static string CommonNamespacePrefix(IEnumerable<string> namespaces)
    {
        var list = namespaces.Where(n => !string.IsNullOrWhiteSpace(n)).ToList();
        if (list.Count == 0)
            return "";
        var segments = list[0].Split('.');
        var keep = new List<string>();
        for (int i = 0; i < segments.Length; i++)
        {
            var candidate = string.Join(".", segments.Take(i + 1));
            if (list.All(n => n == candidate || n.StartsWith(candidate + ".", StringComparison.Ordinal)))
                keep.Add(segments[i]);
            else
                break;
        }
        return string.Join(".", keep);
    }

    private static string BuildCs(string ns, string modelClassName, int modelId,
        List<ProtoField> fullFields, List<ProtoField> updateFields)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated>");
        sb.AppendLine("// 由 WebWindowUI.Generator 自动生成，请勿手动修改。修改模型类后重新构建即可。");
        sb.AppendLine("// </auto-generated>");
        sb.AppendLine();
        sb.AppendLine("#nullable enable");
        sb.AppendLine("using System.IO;");
        sb.AppendLine("using System.Linq;");
        sb.AppendLine("using ProtoBuf;");
        sb.AppendLine("using WebWindowUI.Core.Protocol;");
        sb.AppendLine();
        sb.AppendLine($"namespace {ns};");
        sb.AppendLine();
        sb.AppendLine("[ProtoContract]");
        sb.AppendLine($"public sealed class {modelClassName}Snapshot");
        sb.AppendLine("{");
        foreach (ProtoField f in fullFields)
            sb.AppendLine($"    [ProtoMember({f.Number})] public {f.DtoType} {f.CsName} {{ get; set; }}{f.DtoInit}");
        sb.AppendLine();
        sb.AppendLine($"    public static {modelClassName}Snapshot From({modelClassName} model) => new()");
        sb.AppendLine("    {");
        foreach (ProtoField f in fullFields)
            sb.AppendLine($"        {f.CsName} = {f.MapExpr},");
        sb.AppendLine("    };");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("[ProtoContract]");
        sb.AppendLine($"public sealed class {modelClassName}Update");
        sb.AppendLine("{");
        foreach (ProtoField f in updateFields)
            sb.AppendLine($"    [ProtoMember({f.Number})] public {f.UpdDtoType} {f.CsName} {{ get; set; }}");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine($"public partial class {modelClassName}");
        sb.AppendLine("{");
        sb.AppendLine($"    protected override int ModelId => {modelId};");
        sb.AppendLine("    protected override byte[] EncodeFullSnapshot()");
        sb.AppendLine("    {");
        sb.AppendLine("        using var ms = new MemoryStream();");
        sb.AppendLine($"        Serializer.Serialize(ms, {modelClassName}Snapshot.From(this));");
        sb.AppendLine("        return ms.ToArray();");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    protected override byte[] EncodePropertyUpdate(string propertyName, object? value)");
        sb.AppendLine("    {");
        sb.AppendLine($"        var u = new {modelClassName}Update();");
        sb.AppendLine("        switch (propertyName)");
        sb.AppendLine("        {");
        foreach (ProtoField f in updateFields)
            sb.AppendLine($"            case \"{f.CsName}\": u.{f.CsName} = {f.UpdSetExpr}; break;");
        sb.AppendLine("        }");
        sb.AppendLine("        using var ms = new MemoryStream();");
        sb.AppendLine("        Serializer.Serialize(ms, u);");
        sb.AppendLine("        return ms.ToArray();");
        sb.AppendLine("    }");
        sb.AppendLine("}");
        return sb.ToString();
    }

    // ---- 工具 ----

    /// <summary>
    /// 精确识别 CommunityToolkit.Mvvm 的 [RelayCommand]（可带也可不带 "Attribute" 后缀、可限定命名空间）。
    /// 用名字段精确比对，避免把用户自定义的 xxxRelayCommand 属性误当生成目标。
    /// </summary>
    private static bool IsRelayCommandAttribute(AttributeSyntax a)
    {
        var full = a.Name.ToString();
        var dot = full.LastIndexOf('.');
        var name = dot >= 0 ? full.Substring(dot + 1) : full;
        return name is "RelayCommand" or "RelayCommandAttribute";
    }

    /// <summary>
    /// 精确识别 CommunityToolkit.Mvvm 的 [ObservableProperty]（可带也可不带 "Attribute" 后缀、可限定命名空间）。
    /// 用名字段精确比对，避免把用户自定义的 xxxObservableProperty 属性误当生成目标。
    /// </summary>
    private static bool IsObservablePropertyAttribute(AttributeSyntax a)
    {
        var full = a.Name.ToString();
        var dot = full.LastIndexOf('.');
        var name = dot >= 0 ? full.Substring(dot + 1) : full;
        return name is "ObservableProperty" or "ObservablePropertyAttribute";
    }

    private static string? FindNamespace(SyntaxNode root)
    {
        FileScopedNamespaceDeclarationSyntax? fsn = root.DescendantNodes()
            .OfType<FileScopedNamespaceDeclarationSyntax>().FirstOrDefault();
        if (fsn is not null)
            return fsn.Name.ToString();
        return root.DescendantNodes().OfType<NamespaceDeclarationSyntax>().FirstOrDefault()?.Name.ToString();
    }

    private static string ToPascalCase(string name)
        => string.IsNullOrEmpty(name) ? name : char.ToUpperInvariant(name[0]) + name.Substring(1);

    private static string ToCamelCase(string name)
        => string.IsNullOrEmpty(name) ? name : char.ToLowerInvariant(name[0]) + name.Substring(1);
}
