using System.Text.Json;
using WebWindowUI.Generator;
using Xunit;

namespace WebWindowUI.Tests;

/// <summary>生成器测试：对样例的真实模型源码跑 ModelProtoGenerator，断言输出的 .proto / C# / descriptor。</summary>
public class GeneratorTests
{
    [Fact]
    public void SettingsModel_GeneratesFullTypeMap()
    {
        string? src = ReadRepoSource("SettingsModel.cs");
        Assert.NotNull(src); // 测试运行于仓库内，样例源码必须存在

        ModelProtoResult result = ModelProtoGenerator.Generate(src!, "SettingsModel");

        // .proto：标量原生类型 + 时间/Guid 转 string + 枚举/object 兜底 ModelValue + 集合 repeated
        Assert.Contains("message SettingsModel {", result.ProtoText);
        Assert.Contains("string theme = 1;", result.ProtoText);
        Assert.Contains("bool autoSave = 2;", result.ProtoText);
        Assert.Contains("int32 maxItems = 3;", result.ProtoText);
        Assert.Contains("double progress = 4;", result.ProtoText);
        Assert.Contains("int64 totalBytes = 5;", result.ProtoText);          // long
        Assert.Contains("string instanceId = 6;", result.ProtoText);        // Guid
        Assert.Contains("string lastBackup = 7;", result.ProtoText);        // DateTime
        Assert.Contains("string keepHistory = 8;", result.ProtoText);       // TimeSpan
        Assert.Contains("webwindowui.model.ModelValue syncMode = 9;", result.ProtoText); // 枚举兜底
        Assert.Contains("repeated string tags = 10;", result.ProtoText);    // List<string>
        Assert.Contains("webwindowui.model.ModelValue config = 11;", result.ProtoText); // object 兜底
        Assert.Contains("message SettingsModelUpdate {", result.ProtoText);

        // C#：可空 DTO、字段号与 proto 一致、更新编码器接线
        Assert.Contains("webwindowui.model.generated.SettingsModel", result.CsCode);
        Assert.Contains("webwindowui.model.generated.SettingsModelUpdate", result.CsCode);
        Assert.Contains("protected override string UpdateMessageName", result.CsCode);
        Assert.Contains("[ProtoMember(5)] public long TotalBytes", result.CsCode);
        Assert.Contains("[ProtoMember(9)] public ModelValue? SyncMode", result.CsCode);
        Assert.Contains("[ProtoMember(10)] public List<string> Tags", result.CsCode);
        Assert.Contains("case \"TotalBytes\": u.TotalBytes =", result.CsCode);

        // TS：camelCase 属性 + 类型映射 + 枚举 → number
        Assert.Contains("export class SettingsModel {", result.TsCode);
        Assert.Contains("theme: string = ''", result.TsCode);
        Assert.Contains("autoSave: boolean = false", result.TsCode);
        Assert.Contains("maxItems: number = 0", result.TsCode);
        Assert.Contains("totalBytes: number = 0", result.TsCode);
        Assert.Contains("instanceId: string = ''", result.TsCode);
        Assert.Contains("syncMode: number = 0", result.TsCode);              // 枚举 → ModelValue 兜底 → number
        Assert.Contains("tags: string[] = []", result.TsCode);
        Assert.Contains("config: Record<string, unknown> = {}", result.TsCode);
    }

    [Fact]
    public void AboutModel_GeneratesBytesArrayAndObject()
    {
        string? src = ReadRepoSource("AboutModel.cs");
        Assert.NotNull(src); // 测试运行于仓库内，样例源码必须存在

        ModelProtoResult result = ModelProtoGenerator.Generate(src!, "AboutModel");

        Assert.Contains("message AboutModel {", result.ProtoText);
        Assert.Contains("repeated string contributors = 5;", result.ProtoText); // List<string>
        Assert.Contains("repeated string features = 6;", result.ProtoText);     // string[]
        Assert.Contains("bytes iconHash = 7;", result.ProtoText);               // byte[]
        Assert.Contains("webwindowui.model.ModelValue metadata = 8;", result.ProtoText); // object 兜底
        Assert.Contains("webwindowui.model.generated.AboutModelUpdate", result.CsCode);
        Assert.Contains("protected override string UpdateMessageName", result.CsCode);
        Assert.Contains("[ProtoMember(7)] public byte[]? IconHash", result.CsCode);

        // TS：byte[] → Uint8Array、repeated → T[]、object 兜底 → Record<string, unknown>
        Assert.Contains("export class AboutModel {", result.TsCode);
        Assert.Contains("appName: string = ''", result.TsCode);
        Assert.Contains("contributors: string[] = []", result.TsCode);
        Assert.Contains("features: string[] = []", result.TsCode);
        Assert.Contains("iconHash: Uint8Array = new Uint8Array()", result.TsCode);
        Assert.Contains("metadata: Record<string, unknown> = {}", result.TsCode);

        // 生成的绑定助手：import bindModel + descriptor（自包含），页面只调 bindAboutModel()
        Assert.Contains("import { bindModel } from 'webwindowui-bridge';", result.TsCode);
        Assert.Contains("import descriptorJson from '../bridge/about_model.json';", result.TsCode);
        Assert.Contains("export function bindAboutModel(): AboutModel {", result.TsCode);
        Assert.Contains("return bindModel(new AboutModel(), descriptorJson);", result.TsCode);
    }

    [Fact]
    public void LauncherModel_GeneratesCommandMethods()
    {
        string? src = ReadRepoSource("LauncherModel.cs");
        Assert.NotNull(src); // 测试运行于仓库内，样例源码必须存在

        ModelProtoResult result = ModelProtoGenerator.Generate(src!, "LauncherModel");

        // [RelayCommand] 方法 → TS 命令方法：无参 openWindow()、带参 commandWithArg(arg: string)，
        // 类继承 webwindowui-bridge 的 ModelCommandHost（命令通道类型契约在上层库，由 bindModel 注入为
        // 不可枚举实例属性，不再重复声明在模型里）；线缆 command id = .NET 方法名 PascalCase。
        Assert.Contains("import { bindModel, ModelCommandHost } from 'webwindowui-bridge';", result.TsCode);
        Assert.Contains("export class LauncherModel extends ModelCommandHost {", result.TsCode);
        Assert.DoesNotContain("private _commandChannel", result.TsCode); // 通道声明归上层库，模型不再重复
        Assert.Contains("openWindow(): void { this._commandChannel?.('OpenWindow') }", result.TsCode);
        Assert.Contains("commandWithArg(arg: string): void { this._commandChannel?.('CommandWithArg', arg) }", result.TsCode);

        // OpenRequested 事件不是模型字段：不进 TS 镜像（非 [ObservableProperty] 字段、非公开只读属性）
        Assert.DoesNotContain("openRequested", result.TsCode);

        // 普通字段照常（命令不挤占字段号；可空 string 落 TS string 默认 ''）
        Assert.Contains("request: string = ''", result.TsCode);
        Assert.Contains("buttonEnable: boolean = false", result.TsCode);
    }

    [Fact]
    public void ModelWithoutCommands_OmitsCommandChannel()
    {
        string? src = ReadRepoSource("SettingsModel.cs");
        Assert.NotNull(src);

        ModelProtoResult result = ModelProtoGenerator.Generate(src!, "SettingsModel");

        Assert.DoesNotContain("_commandChannel", result.TsCode);
    }

    [Fact]
    public void TsSubPath_MapsNamespaceToFolder()
    {
        // 与根命名空间相同 → 落在 src/models 根
        Assert.Equal("", ModelProtoGenerator.TsSubPath("WebWindowUI.Sample", "WebWindowUI.Sample"));
        // 前缀之外剩余段小写、'.'→'/'（命名空间段 Users → users/）
        Assert.Equal("users", ModelProtoGenerator.TsSubPath("WebWindowUI.Sample.Users", "WebWindowUI.Sample"));
        Assert.Equal("admin/roles", ModelProtoGenerator.TsSubPath("WebWindowUI.Sample.Admin.Roles", "WebWindowUI.Sample"));
        // 前缀不匹配 → 安全回退（不生成子目录）
        Assert.Equal("", ModelProtoGenerator.TsSubPath("Other.Ns", "WebWindowUI.Sample"));
        // 空根命名空间 → 根
        Assert.Equal("", ModelProtoGenerator.TsSubPath("A.B", ""));
    }

    [Theory]
    [InlineData(new[] { "WebWindowUI.Sample", "WebWindowUI.Sample.Users" }, "WebWindowUI.Sample")] // 公共前缀 = 较短者
    [InlineData(new[] { "WebWindowUI.Sample" }, "WebWindowUI.Sample")]                             // 单模型 = 其自身命名空间
    [InlineData(new[] { "A.B", "A.C" }, "A")]                                                     // 跨段取公共段前缀
    [InlineData(new[] { "A.B", "C.D" }, "")]                                                      // 无公共前缀 → 回退根
    [InlineData(new[] { "WebWindowUI.Sample", "WebWindowUI.Sample.Users", "WebWindowUI.Sample.Admin" }, "WebWindowUI.Sample")]
    [InlineData(new[] { "" }, "")]                                                                 // 空命名空间输入
    public void CommonNamespacePrefix_FindsLongestCommonSegmentPrefix(string[] namespaces, string expected)
    {
        Assert.Equal(expected, ModelProtoGenerator.CommonNamespacePrefix(namespaces));
    }

    [Fact]
    public void SettingsDescriptor_UpdateCarrier_UsesMessageNamePayload()
    {
        string? src = ReadRepoSource("SettingsModel.cs");
        Assert.NotNull(src); // 测试运行于仓库内，样例源码必须存在

        ModelProtoResult result = ModelProtoGenerator.Generate(src!, "SettingsModel");

        // 增量 update 消息与完整模型同序同号（前端按字段出现应用增量）
        Assert.Contains("// 增量 update：字段与完整模型同序同号", result.ProtoText);
        Assert.Contains("string theme = 1;", result.ProtoText);
        Assert.Contains("int64 totalBytes = 5;", result.ProtoText);
        Assert.Contains("repeated string", result.ProtoText);
    }

    [Fact]
    public void ListOfModel_GeneratesTypedRepeatedAndImport()
    {
        const string mainSrc = """
        using CommunityToolkit.Mvvm.ComponentModel;
        using WebWindowUI;

        namespace WebWindowUI.Sample;

        public partial class MainWindowModel : WebWindowModel
        {
            [ObservableProperty]
            private string name = "";

            [ObservableProperty]
            private List<TodoItemModel> todos = new();
        }
        """;

        const string todoSrc = """
        using CommunityToolkit.Mvvm.ComponentModel;
        using WebWindowUI;

        namespace WebWindowUI.Sample;

        public partial class TodoItemModel : WebWindowModel
        {
            [ObservableProperty]
            private string title = "";

            [ObservableProperty]
            private bool done;
        }
        """;

        var all = new Dictionary<string, string> { ["MainWindowModel"] = mainSrc, ["TodoItemModel"] = todoSrc };
        ModelProtoResult result = ModelProtoGenerator.Generate(mainSrc, "MainWindowModel", all, "WebWindowUI.Sample");

        // .proto：typed repeated 元素消息（而非 ModelValue 兜底）
        Assert.Contains("repeated TodoItemModel todos = 2;", result.ProtoText);

        // C#：快照 DTO 引用元素模型快照类型 + From 映射
        Assert.Contains("List<WebWindowUI.Sample.TodoItemModelSnapshot> Todos", result.CsCode);
        Assert.Contains("Select(x => WebWindowUI.Sample.TodoItemModelSnapshot.From(x)).ToList()", result.CsCode);

        // TS：强类型数组 + 相对 import
        Assert.Contains("import { TodoItemModel } from './TodoItemModel';", result.TsCode);
        Assert.Contains("todos: TodoItemModel[] = []", result.TsCode);

        // descriptor：repeated + 元素类型，且全量集合包含元素消息（typed 引用可被 protobufjs 解析）
        using JsonDocument json = JsonDocument.Parse(result.DescriptorJson);
        JsonElement gen = json.RootElement.GetProperty("nested").GetProperty("webwindowui")
            .GetProperty("nested").GetProperty("model").GetProperty("nested")
            .GetProperty("generated").GetProperty("nested");
        JsonElement todosField = gen.GetProperty("MainWindowModel").GetProperty("fields").GetProperty("todos");
        Assert.Equal("repeated", todosField.GetProperty("rule").GetString());
        Assert.Equal("TodoItemModel", todosField.GetProperty("type").GetString());
        Assert.True(gen.TryGetProperty("TodoItemModel", out _), "全量 descriptor 应包含元素模型消息");
    }

    [Fact]
    public void ObservableCollectionOfModel_GeneratesTypedRepeated()
    {
        const string mainSrc = """
        using System.Collections.ObjectModel;
        using CommunityToolkit.Mvvm.ComponentModel;
        using WebWindowUI;

        namespace WebWindowUI.Sample;

        public partial class MainWindowModel : WebWindowModel
        {
            [ObservableProperty]
            private string name = "";

            [ObservableProperty]
            private ObservableCollection<TodoItemModel> todos = new();
        }
        """;

        const string todoSrc = """
        using CommunityToolkit.Mvvm.ComponentModel;
        using WebWindowUI;

        namespace WebWindowUI.Sample;

        public partial class TodoItemModel : WebWindowModel
        {
            [ObservableProperty]
            private string title = "";

            [ObservableProperty]
            private bool done;
        }
        """;

        var all = new Dictionary<string, string> { ["MainWindowModel"] = mainSrc, ["TodoItemModel"] = todoSrc };
        ModelProtoResult result = ModelProtoGenerator.Generate(mainSrc, "MainWindowModel", all, "WebWindowUI.Sample");

        // ObservableCollection<T> 与 List<T> 同样走 typed repeated：元素消息引用而非 ModelValue 兜底
        Assert.Contains("repeated TodoItemModel todos = 2;", result.ProtoText);
        Assert.Contains("List<WebWindowUI.Sample.TodoItemModelSnapshot> Todos", result.CsCode);
        Assert.Contains("todos: TodoItemModel[] = []", result.TsCode);
    }

    /// <summary>从测试 bin 向上找仓库根，读样例模型源码。</summary>
    private static string? ReadRepoSource(string modelFile)
    {
        DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            string full = Path.Combine(dir.FullName, "Sample", "WebWindowUI.Sample.Backend", modelFile);
            if (File.Exists(full))
                return File.ReadAllText(full);
            dir = dir.Parent;
        }
        return null;
    }
}
