using System.Collections.Immutable;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using ProtoBuf;
using WebWindowUI.Core;
using WebWindowUI.Generator.SourceGen;
using Xunit;

namespace WebWindowUI.Tests;

/// <summary>
/// 写回源生成器（WebWindowUI.Generator.SourceGen.WriteBackGenerator）单测：用 CSharpGeneratorDriver
/// 对样例真实模型源码跑生成器，断言产出的 partial（TrySetGeneratedProperty / TryInvokeGeneratedCommand /
/// TryGetGeneratedProperty / SubscribeGeneratedCollections / POCO 转换 + [ModuleInitializer]）。
/// 编译须带 CommunityToolkit.Mvvm + WebWindowUI 元数据引用，否则 [ObservableProperty]/WebWindowModel
/// 符号解析不到 → 无输出。
/// </summary>
public class WriteBackGeneratorTests
{
    [Fact]
    public void SampleModels_GenerateAllFiveMembers()
    {
        ImmutableDictionary<string, string> run = Driver.RunOnSampleModels();

        // ---- MainWindowModel：写回 + 读值 + 集合订阅 + POCO ----
        var main = run["MainWindowModel.WriteBack.g.cs"];
        Assert.Contains("protected override bool TrySetGeneratedProperty(string name, global::WebWindowUI.Core.Protocol.ModelValue? value)", main);
        Assert.Contains("case \"Name\":", main);
        Assert.Contains("ApplyRemoteWrite(() => Name = (string)c0!)", main);
        Assert.Contains("case \"Count\":", main);
        Assert.Contains("typeof(int)", main);
        Assert.Contains("case \"Extra\":", main);   // object 兜底属性
        Assert.Contains("typeof(object)", main);
        Assert.Contains("protected override bool TryGetGeneratedProperty(string name, out object? value)", main);
        Assert.Contains("case \"Name\": value = Name; return true;", main);
        Assert.Contains("foreach (var kv in v.OrdinalFields)", main); // POCO 序数键：遍历 OrdinalFields（int 键）
        Assert.Contains("switch (kv.Key)", main);                     // case 直接数字字面量

        // ---- LauncherModel：命令（无参 typeof(object) + 带参 string），CanExecute 门控 ----
        var launcher = run["LauncherModel.WriteBack.g.cs"];
        Assert.Contains("case \"OpenWindow\":", launcher);
        Assert.Contains("typeof(global::System.Object)", launcher); // 无参命令按 object 转
        Assert.Contains("if (!OpenWindowCommand.CanExecute(arg)) return false;", launcher);
        Assert.Contains("OpenWindowCommand.Execute(arg);", launcher);
        Assert.Contains("case \"CommandWithArg\":", launcher);
        Assert.Contains("typeof(string)", launcher);
        Assert.Contains("if (!CommandWithArgCommand.CanExecute(arg)) return false;", launcher);

        // ---- TodoListModel：ObservableCollection<TodoItemModel> 写回 + 单条集合订阅表达式 ----
        var todoList = run["TodoListModel.WriteBack.g.cs"];
        Assert.Contains("case \"Todos\":", todoList);
        Assert.Contains("EnsureCollectionSubscribed(\"Todos\", Todos)", todoList);
        Assert.Contains("=> EnsureCollectionSubscribed(\"Todos\", Todos);", todoList);

        // ---- TodoItemModel：POCO 转换 + 反向序列化 + [ModuleInitializer] 注册 ----
        var todoItem = run["TodoItemModel.WriteBack.g.cs"];
        Assert.Contains("[global::System.Runtime.CompilerServices.ModuleInitializer]", todoItem);
        Assert.Contains("internal static void __WWUI_RegisterPocoConverter()", todoItem);
        Assert.Contains("RegisterPocoConverter(typeof(TodoItemModel), ConvertFromModelValue)", todoItem);
        Assert.Contains("RegisterPocoSerializer(typeof(TodoItemModel), ConvertToModelValue)", todoItem);
        // 序数键：todoItem 声明序 title=1、done=2（与 ModelProtoGenerator 编号一致），case 用数字字面量
        Assert.Contains("case 1:", todoItem);
        Assert.Contains("case 2:", todoItem);
        Assert.Contains("instance.Title = (string)c0!;", todoItem);
        Assert.DoesNotContain("case \"title\":", todoItem); // 不再按属性名匹配
        Assert.DoesNotContain("case \"1\":", todoItem);     // 不用字符串字面量键
        // 反向序列化器：实例 → object map（序数键，全可读属性含只读）
        Assert.Contains("internal static bool ConvertToModelValue(object value, out global::WebWindowUI.Core.Protocol.ModelValueMap? map)", todoItem);
        Assert.Contains("m.OrdinalFields[1] = global::WebWindowUI.Core.Protocol.ModelProtocol.ToModelValue(instance.Title);", todoItem);

        // ---- MultiWindowModel：有参构造 → 不生成 POCO 转换/注册 ----
        var multi = run["MultiWindowModel.WriteBack.g.cs"];
        Assert.DoesNotContain("ModuleInitializer", multi);
        Assert.DoesNotContain("RegisterPocoConverter", multi);
        // 命令/写回/读值照常
        Assert.Contains("case \"Name\":", multi);
    }

    [Fact]
    public void PlainClass_NoOutput()
    {
        ImmutableDictionary<string, string> run = Driver.RunOnSource("""
        namespace WebWindowUI.Sample;

        public class NotAModel
        {
            public int X { get; set; }
        }
        """);
        Assert.Empty(run);
    }

    [Fact]
    public void WebWindowModel_WithoutAttributes_NoOutput()
    {
        // 继承 WebWindowModel 但无 [ObservableProperty]/[RelayCommand]：WriteBack 语法预筛即排除，无输出；
        // 但 ProtoGenerator 按「派生自 WebWindowModel」仍会为它产出 {Model}Proto.g.cs（快照/DTO + partial override），
        // 故只断言 WriteBack 产物缺席。
        ImmutableDictionary<string, string> run = Driver.RunOnSource("""
        using WebWindowUI.Core;

        namespace WebWindowUI.Sample;

        public partial class EmptyModel : WebWindowModel
        {
        }
        """);
        Assert.False(run.ContainsKey("EmptyModel.WriteBack.g.cs"));
        Assert.True(run.ContainsKey("EmptyModelProto.g.cs"));
    }

    [Fact]
    public void ExplicitPublicProperties_GetWriteAndReadCases()
    {
        // 反射兜底已移除：源码显式 public 属性（非 [ObservableProperty] 字段）也必须进生成 switch，
        // 否则写回/读值静默失效。可写属性进 TrySet + TryGet；只读 expression-bodied 属性只进 TryGet。
        ImmutableDictionary<string, string> run = Driver.RunOnSource("""
        using CommunityToolkit.Mvvm.ComponentModel;
        using WebWindowUI.Core;

        namespace WebWindowUI.Sample;

        public partial class ExplicitModel : WebWindowModel
        {
            [ObservableProperty]
            private string _name = "";

            public int ExplicitWritable { get; set; }

            public string ExplicitReadOnly => Name + "!";
        }
        """);

        var src = run["ExplicitModel.WriteBack.g.cs"];

        // [ObservableProperty] 字段照常（从字段符号推属性名）
        Assert.Contains("case \"Name\":", src);
        // 显式可写属性：写回 + 读值都有 case
        Assert.Contains("case \"ExplicitWritable\":", src);
        Assert.Contains("ApplyRemoteWrite(() => ExplicitWritable = (int)c1!)", src);
        Assert.Contains("case \"ExplicitWritable\": value = ExplicitWritable; return true;", src);
        // 显式只读属性：只读值 case，不生成写回
        Assert.Contains("case \"ExplicitReadOnly\": value = ExplicitReadOnly; return true;", src);
        Assert.DoesNotContain("ExplicitReadOnly = ", src); // 无 setter case（set 不到）
    }

    [Fact]
    public void GetOnlyCollectionAndDictionary_EmitsInPlaceWriteback()
    {
        // 显式 get-only 集合/字典属性（不加 [ObservableProperty]）：照常收集（读值/集合订阅），
        // TrySet 走原地清空重建（IsReadOnly 也放行），保留实例与订阅 → 双向全通。
        ImmutableDictionary<string, string> run = Driver.RunOnSource("""
        using System.Collections.ObjectModel;
        using CommunityToolkit.Mvvm.ComponentModel;
        using WebWindowUI.Core;

        namespace WebWindowUI.Sample;

        public partial class DictModel : WebWindowModel
        {
            [ObservableProperty]
            private string _name = "";

            public ObservableCollection<string> Items { get; } = new();

            public ObservableDictionary<string, int> Counts { get; } = new();
        }
        """);

        var src = run["DictModel.WriteBack.g.cs"];

        // get-only ObservableCollection：有 TrySet case，原地清空 + 逐项 Add（列表 Add 元素）
        Assert.Contains("case \"Items\":", src);
        Assert.Contains("Items.Clear()", src);
        Assert.Contains("foreach (var item in incoming", src);
        Assert.Contains("Items.Add(item)", src);
        // get-only ObservableDictionary：同款原地重建（字典 Add(KeyValuePair)）
        Assert.Contains("case \"Counts\":", src);
        Assert.Contains("Counts.Clear()", src);
        Assert.Contains("Counts.Add(item)", src);
        // 读值 case 照常（读值只看可读性，不看 IsReadOnly setter）
        Assert.Contains("case \"Items\": value = Items; return true;", src);
        Assert.Contains("case \"Counts\": value = Counts; return true;", src);
        // 集合订阅照常（ObservableDictionary 实现 INotifyCollectionChanged → EnsureCollectionSubscribed）
        Assert.Contains("EnsureCollectionSubscribed(\"Items\", Items)", src);
        Assert.Contains("EnsureCollectionSubscribed(\"Counts\", Counts)", src);
    }

    [Fact]
    public void ProtoGenerator_EmitsModelProtoForEachModel()
    {
        // ProtoGenerator（与 WriteBack 同跑 CSharpGeneratorDriver）：为每个 WebWindowModel 子类内存产出
        // {Model}Proto.g.cs（快照/增量 DTO + partial override）。
        ImmutableDictionary<string, string> run = Driver.RunOnSampleModels();

        var main = run["MainWindowModelProto.g.cs"];
        Assert.Contains("protected override string FullMessageName", main);
        Assert.Contains("webwindowui.model.generated.MainWindowModel", main);
        Assert.Contains("[ProtoMember(1)] public string Name", main);
        Assert.Contains("case \"Name\": u.Name =", main); // 增量编码器 switch

        // typed repeated：全模型清单（allModelSources 由编译内全部模型类构建）解析出元素模型 → 快照 DTO 引用其快照类型
        var todoList = run["TodoListModelProto.g.cs"];
        Assert.Contains("List<WebWindowUI.Sample.Items.TodoItemModelSnapshot> Todos", todoList);

        // 全部 7 个模型都有 Proto 输出（与 WriteBack 并列，partial 合并进同一类型）
        Assert.Contains(run.Keys, k => k == "TodoItemModelProto.g.cs");
        Assert.Contains(run.Keys, k => k == "MultiWindowModelProto.g.cs");
    }

    /// <summary>CSharpGeneratorDriver 驱动：编译真实/内联源码 + 元数据引用，返回 hintName → 生成源码。</summary>
    private static class Driver
    {
        public static ImmutableDictionary<string, string> RunOnSampleModels()
        {
            string? dir = FindRepoRoot();
            Assert.NotNull(dir);
            var backendDir = Path.Combine(dir!, "Sample", "WebWindowUI.Sample.Backend");

            // 全部 .cs（递归含 Items\ 子目录的嵌套模型，互相引用须一起编；DataProvider.cs 非模型，生成器按基类过滤）。
            // 排除 obj\bin 下的中间产物（EmitCompilerGeneratedFiles 落盘的 .g.cs / GlobalUsings / AssemblyInfo，
            // 混进驱动编译会重复定义类型）。
            var sources = new Dictionary<string, string>();
            foreach (string file in Directory.GetFiles(backendDir, "*.cs", SearchOption.AllDirectories)
                         .Where(p => !p.Contains("\\obj\\") && !p.Contains("\\bin\\")))
                sources[Path.GetFileName(file)] = File.ReadAllText(file);
            return Run(sources);
        }

        public static ImmutableDictionary<string, string> RunOnSource(string source)
            => Run(new Dictionary<string, string> { ["Test.cs"] = source });

        private static ImmutableDictionary<string, string> Run(Dictionary<string, string> sources)
        {
            var refs = new List<MetadataReference>();
            foreach (string tpa in ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator))
                refs.Add(MetadataReference.CreateFromFile(tpa));
            refs.Add(MetadataReference.CreateFromFile(typeof(WebWindowModel).Assembly.Location));
            refs.Add(MetadataReference.CreateFromFile(typeof(ObservableObject).Assembly.Location));
            refs.Add(MetadataReference.CreateFromFile(typeof(Serializer).Assembly.Location));

            var parseOptions = new CSharpParseOptions(LanguageVersion.Preview);
            IEnumerable<SyntaxTree> trees = sources.Select(kv =>
                CSharpSyntaxTree.ParseText(kv.Value, parseOptions, kv.Key));
            var compilation = CSharpCompilation.Create(
                "WriteBackGeneratorTests",
                trees,
                refs,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            // parseOptions 必须与输入树一致：生成器产出的树按它创建，不一致会抛
            // "inconsistent language versions"（驱动默认 Latest）。IIncrementalGenerator 须 AsSourceGenerator() 包装。
            GeneratorDriver driver = CSharpGeneratorDriver.Create(
                new[] { new WriteBackGenerator().AsSourceGenerator(), new ProtoGenerator().AsSourceGenerator() },
                parseOptions: parseOptions);
            return driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _)
                .GetRunResult().Results
                .SelectMany(r => r.GeneratedSources)
                .ToImmutableDictionary(s => s.HintName, s => s.SourceText.ToString());
        }

        /// <summary>从测试 bin 向上找仓库根。</summary>
        private static string? FindRepoRoot()
        {
            DirectoryInfo? d = new(AppContext.BaseDirectory);
            while (d is not null)
            {
                if (File.Exists(Path.Combine(d.FullName, "WebWindowUI.slnx")))
                    return d.FullName;
                d = d.Parent;
            }
            return null;
        }
    }
}
