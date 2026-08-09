using System.Reflection;
using System.Text.Json;
using ProtoBuf;
using WebWindowUI.Generator;
using WebWindowUI.Sample;
using Xunit;

namespace WebWindowUI.Tests;

/// <summary>
/// 协议层测试：WebView2StringCodec（NUL 转义线缆编解码）、WebMessage 信封往返，
/// 以及「生成器产出的前端 descriptor JSON ↔ 编译进 .NET 的 DTO」字段号/类型锁。
/// </summary>
public class ModelProtoTests
{
    // ---- WebView2StringCodec ----

    [Fact]
    public void Codec_RoundTrip_AllByteValues()
    {
        byte[] bytes = Enumerable.Range(0, 256).Select(i => (byte)i).ToArray();

        string s = WebView2StringCodec.Encode(bytes);

        Assert.Equal(bytes, WebView2StringCodec.Decode(s));
    }

    [Fact]
    public void Codec_NulDense_PayloadIsNulFree()
    {
        byte[] bytes = { 0x00, 0x00, 0x00, 0x01, 0x00 }; // NUL 密集（protobuf 常见）

        string s = WebView2StringCodec.Encode(bytes);

        Assert.DoesNotContain('\0', s); // WebView2 字符串通道在 NUL 处截断，绝不允许出现
        Assert.Equal(bytes, WebView2StringCodec.Decode(s));
    }

    [Fact]
    public void Codec_Backslash_EscapesItself()
    {
        byte[] bytes = { 0x5C, 0x5C, 0x41, 0x5C }; // 转义符自身（0x5C）必须成对转义

        string s = WebView2StringCodec.Encode(bytes);

        Assert.Equal(bytes, WebView2StringCodec.Decode(s));
    }

    // ---- WebMessage 信封往返 ----

    [Fact]
    public void Envelope_RoundTrip_Ready()
    {
        var msg = new WebMessage { Ready = new ModelReady() };

        WebMessage? back = ModelProtocol.Decode(ModelProtocol.Encode(msg));

        Assert.NotNull(back?.Ready);
    }

    [Fact]
    public void Envelope_RoundTrip_Update()
    {
        var msg = new WebMessage
        {
            Update = new ModelUpdate { MessageName = "x.Y", Payload = new byte[] { 1, 2, 3 } },
        };

        WebMessage? back = ModelProtocol.Decode(ModelProtocol.Encode(msg));

        Assert.NotNull(back?.Update);
        Assert.Equal("x.Y", back.Update.MessageName);
        Assert.Equal(new byte[] { 1, 2, 3 }, back.Update.Payload);
    }

    [Fact]
    public void Envelope_RoundTrip_Set()
    {
        var msg = new WebMessage
        {
            Set = new ModelSet { Property = "Theme", Value = new ModelValue { Text = "dark" } },
        };

        WebMessage? back = ModelProtocol.Decode(ModelProtocol.Encode(msg));

        Assert.NotNull(back?.Set);
        Assert.Equal("Theme", back.Set!.Property);
        Assert.Equal("dark", back.Set.Value!.Text);
    }

    [Fact]
    public void Envelope_RoundTrip_Invoke()
    {
        var msg = new WebMessage
        {
            Invoke = new ModelInvoke { Command = "CommandWithArg", Value = new ModelValue { Text = "todos" } },
        };

        WebMessage? back = ModelProtocol.Decode(ModelProtocol.Encode(msg));

        Assert.NotNull(back?.Invoke);
        Assert.Equal("CommandWithArg", back.Invoke!.Command);
        Assert.Equal("todos", back.Invoke.Value!.Text);
    }

    [Fact]
    public void Envelope_RoundTrip_Snapshot()
    {
        var msg = new WebMessage
        {
            Snapshot = new ModelSnapshot { Data = new Dictionary<string, ModelValue> { ["A"] = new() { Number = 1 } } },
        };

        WebMessage? back = ModelProtocol.Decode(ModelProtocol.Encode(msg));

        Assert.NotNull(back?.Snapshot);
        Assert.Equal(1.0, back.Snapshot.Data["A"].Number);
    }

    [Fact]
    public void Envelope_RoundTrip_Full()
    {
        var msg = new WebMessage
        {
            Full = new GeneratedModel { MessageName = "webwindowui.model.generated.MainWindowModel", Payload = new byte[] { 0x0A, 0x03, 0x61, 0x62, 0x63 } },
        };

        WebMessage? back = ModelProtocol.Decode(ModelProtocol.Encode(msg));

        Assert.NotNull(back?.Full);
        Assert.Equal("webwindowui.model.generated.MainWindowModel", back.Full.MessageName);
        Assert.Equal(new byte[] { 0x0A, 0x03, 0x61, 0x62, 0x63 }, back.Full.Payload);
    }

    [Fact]
    public void Envelope_RoundTrip_Patch()
    {
        var msg = new WebMessage
        {
            Patch = new CollectionPatch
            {
                Action = CollectionPatchAction.Insert,
                Property = "Todos",
                Index = 2,
                Items = { new ModelValue { Text = "new item" } },
            },
        };

        WebMessage? back = ModelProtocol.Decode(ModelProtocol.Encode(msg));

        Assert.NotNull(back?.Patch);
        Assert.Equal(CollectionPatchAction.Insert, back.Patch!.Action);
        Assert.Equal("Todos", back.Patch.Property);
        Assert.Equal(2, back.Patch.Index);
        Assert.Single(back.Patch.Items);
        Assert.Equal("new item", back.Patch.Items[0].Text);
    }

    [Fact]
    public void Envelope_RoundTrip_PatchMove()
    {
        var msg = new WebMessage
        {
            Patch = new CollectionPatch
            {
                Action = CollectionPatchAction.Move,
                Property = "Todos",
                FromIndex = 3,
                Index = 0,
                Count = 1,
            },
        };

        WebMessage? back = ModelProtocol.Decode(ModelProtocol.Encode(msg));

        Assert.NotNull(back?.Patch);
        Assert.Equal(CollectionPatchAction.Move, back.Patch!.Action);
        Assert.Equal(3, back.Patch.FromIndex);
        Assert.Equal(0, back.Patch.Index);
        Assert.Equal(1, back.Patch.Count);
    }

    // ---- descriptor ↔ .NET DTO 锁 ----

    /// <summary>
    /// 生成器为 SettingsModel 产出的前端 descriptor（settings_model.json）字段号/名，
    /// 必须与编译进 Sample 的 SettingsModelSnapshot DTO 的 [ProtoMember] 完全一致——
    /// 这是 protobuf-net（.NET 编码）与 protobufjs（前端解码）能互解的锁。
    /// </summary>
    [Fact]
    public void SettingsDescriptor_MatchesCompiledDto()
    {
        string? jsonPath = FindRepoFile("Sample", "WebWindowUI.Sample.Frontend", "src", "bridge", "settings_model.json");
        Assert.NotNull(jsonPath); // 测试运行于仓库内，生成器产出的 descriptor 必须存在

        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(jsonPath));
        JsonElement settings = doc.RootElement
            .GetProperty("nested").GetProperty("webwindowui").GetProperty("nested")
            .GetProperty("model").GetProperty("nested").GetProperty("generated")
            .GetProperty("nested").GetProperty("SettingsModel");

        // .NET 侧：字段号 → PascalCase 属性名（编译进 Sample 的生成 DTO）
        var dto = typeof(SettingsModelSnapshot).GetProperties()
            .Where(p => p.GetCustomAttribute<ProtoMemberAttribute>() is not null)
            .ToDictionary(p => p.GetCustomAttribute<ProtoMemberAttribute>()!.Tag, p => p.Name);

        Assert.Equal(dto.Count, settings.GetProperty("fields").EnumerateObject().Count());

        foreach (JsonProperty field in settings.GetProperty("fields").EnumerateObject())
        {
            int id = field.Value.GetProperty("id").GetInt32();
            string csName = dto[id]; // 字段号对上才有这个名字
            // proto 字段名 = .NET 属性名首字母小写
            Assert.Equal(char.ToLowerInvariant(csName[0]) + csName[1..], field.Name);
        }
    }

    /// <summary>int64 在 descriptor 里是 int64、枚举/object 落到 ModelValue、集合是 repeated string。</summary>
    [Fact]
    public void SettingsDescriptor_TypeMap()
    {
        string? jsonPath = FindRepoFile("Sample", "WebWindowUI.Sample.Frontend", "src", "bridge", "settings_model.json");
        Assert.NotNull(jsonPath); // 测试运行于仓库内，生成器产出的 descriptor 必须存在

        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(jsonPath));
        JsonElement fields = doc.RootElement
            .GetProperty("nested").GetProperty("webwindowui").GetProperty("nested")
            .GetProperty("model").GetProperty("nested").GetProperty("generated")
            .GetProperty("nested").GetProperty("SettingsModel").GetProperty("fields");

        Assert.Equal("string", fields.GetProperty("theme").GetProperty("type").GetString());
        Assert.Equal("bool", fields.GetProperty("autoSave").GetProperty("type").GetString());
        Assert.Equal("int32", fields.GetProperty("maxItems").GetProperty("type").GetString());
        Assert.Equal("double", fields.GetProperty("progress").GetProperty("type").GetString());
        Assert.Equal("int64", fields.GetProperty("totalBytes").GetProperty("type").GetString()); // long
        Assert.Equal("string", fields.GetProperty("instanceId").GetProperty("type").GetString()); // Guid
        Assert.Equal("string", fields.GetProperty("lastBackup").GetProperty("type").GetString()); // DateTime
        Assert.Equal("string", fields.GetProperty("keepHistory").GetProperty("type").GetString()); // TimeSpan
        Assert.Equal("webwindowui.model.ModelValue", fields.GetProperty("syncMode").GetProperty("type").GetString()); // 枚举兜底
        Assert.Equal("string", fields.GetProperty("tags").GetProperty("type").GetString()); // repeated string
        Assert.Equal("webwindowui.model.ModelValue", fields.GetProperty("config").GetProperty("type").GetString()); // object 兜底
    }

    /// <summary>
    /// 基础信封（WebMessage/ModelValue 等）被生成器内联进每个模型 descriptor → 前端自包含解析、
    /// 无独立 model.json/model.proto。这里锁「descriptor 字段号/名 ↔ ModelProtocol.cs 的 [ProtoMember]」，
    /// 是 protobuf-net（.NET 编码）与 protobufjs（前端解码）互解的基础，两侧任一侧漂移即失败。
    /// </summary>
    [Fact]
    public void BaseEnvelope_InlineInEveryDescriptor_MatchesCompiledDto()
    {
        const string src = """
            namespace WebWindowUI.Sample;
            public partial class MainWindowModel : WebWindowModel
            {
                [ObservableProperty] private int count = 0;
            }
            """;
        ModelProtoResult result = ModelProtoGenerator.Generate(src, "MainWindowModel");
        using JsonDocument doc = JsonDocument.Parse(result.DescriptorJson);
        JsonElement model = doc.RootElement.GetProperty("nested").GetProperty("webwindowui")
            .GetProperty("nested").GetProperty("model").GetProperty("nested");

        // 12 个基础信封消息都在每个模型 descriptor 里（与 generated.* 并列）
        foreach (string msg in new[] { "ModelValue", "ModelValueList", "ModelValueMap", "ModelReady",
            "ModelUpdate", "ModelSet", "ModelInvoke", "ModelSnapshot", "GeneratedModel",
            "CollectionPatch", "CollectionPatchAction", "WebMessage" })
            Assert.True(model.TryGetProperty(msg, out _), $"descriptor 应内联基础信封 {msg}");

        // WebMessage 信封 oneof payload 字段号/名 ↔ .NET [ProtoMember]
        JsonElement webMessage = model.GetProperty("WebMessage");
        AssertDescriptorMatchesDto(webMessage.GetProperty("fields"), typeof(WebMessage));
        Assert.Equal(new[] { "ready", "update", "set", "snapshot", "full", "invoke", "patch" },
            webMessage.GetProperty("oneofs").GetProperty("payload").GetProperty("oneof")
                .EnumerateArray().Select(e => e.GetString()).ToArray());

        // ModelValue 通用值 oneof kind 字段号/名 ↔ .NET [ProtoMember]（C# ObjectValue ↔ proto object）
        JsonElement modelValue = model.GetProperty("ModelValue");
        AssertDescriptorMatchesDto(modelValue.GetProperty("fields"), typeof(ModelValue),
            new Dictionary<int, string> { [5] = "object" });
        Assert.Equal(new[] { "number", "text", "flag", "list", "object", "blob" },
            modelValue.GetProperty("oneofs").GetProperty("kind").GetProperty("oneof")
                .EnumerateArray().Select(e => e.GetString()).ToArray());

        // 其余信封成员消息字段号/名
        AssertDescriptorMatchesDto(model.GetProperty("ModelUpdate").GetProperty("fields"), typeof(ModelUpdate));
        AssertDescriptorMatchesDto(model.GetProperty("ModelSet").GetProperty("fields"), typeof(ModelSet));
        AssertDescriptorMatchesDto(model.GetProperty("ModelInvoke").GetProperty("fields"), typeof(ModelInvoke));
        AssertDescriptorMatchesDto(model.GetProperty("ModelSnapshot").GetProperty("fields"), typeof(ModelSnapshot));
        AssertDescriptorMatchesDto(model.GetProperty("GeneratedModel").GetProperty("fields"), typeof(GeneratedModel));
        AssertDescriptorMatchesDto(model.GetProperty("ModelValueList").GetProperty("fields"), typeof(ModelValueList));
        AssertDescriptorMatchesDto(model.GetProperty("ModelValueMap").GetProperty("fields"), typeof(ModelValueMap));
        AssertDescriptorMatchesDto(model.GetProperty("CollectionPatch").GetProperty("fields"), typeof(CollectionPatch));
        // CollectionPatchAction 枚举取值 ↔ .NET（protobuf-net 3.2 直通为底层 int）：1=Insert … 5=Reset
        JsonElement patchAction = model.GetProperty("CollectionPatchAction").GetProperty("values");
        Assert.Equal(5, patchAction.EnumerateObject().Count());
        Assert.Equal(1, patchAction.GetProperty("Insert").GetInt32());
        Assert.Equal(2, patchAction.GetProperty("Remove").GetInt32());
        Assert.Equal(3, patchAction.GetProperty("Replace").GetInt32());
        Assert.Equal(4, patchAction.GetProperty("Move").GetInt32());
        Assert.Equal(5, patchAction.GetProperty("Reset").GetInt32());
        Assert.Empty(model.GetProperty("ModelReady").GetProperty("fields").EnumerateObject()); // 空消息
    }

    /// <summary>descriptor 字段 ↔ .NET DTO 的 [ProtoMember]：字段号必须对上，字段名 = 属性名首字母小写
    /// （nameExceptions 覆盖刻意改名，如 C# ObjectValue ↔ proto object）。</summary>
    private static void AssertDescriptorMatchesDto(
        JsonElement descriptorFields, Type dto, IReadOnlyDictionary<int, string>? nameExceptions = null)
    {
        var tags = dto.GetProperties()
            .Where(p => p.GetCustomAttribute<ProtoMemberAttribute>() is not null)
            .ToDictionary(p => p.GetCustomAttribute<ProtoMemberAttribute>()!.Tag, p => p.Name);

        Assert.Equal(tags.Count, descriptorFields.EnumerateObject().Count());
        foreach (JsonProperty f in descriptorFields.EnumerateObject())
        {
            int id = f.Value.GetProperty("id").GetInt32();
            string csName = tags[id]; // 字段号对上才有这个名字
            string expect = nameExceptions is not null && nameExceptions.TryGetValue(id, out string? over)
                ? over
                : char.ToLowerInvariant(csName[0]) + csName[1..];
            Assert.Equal(expect, f.Name);
        }
    }

    /// <summary>从测试 bin 向上找仓库根，再定位仓库里的文件。</summary>
    private static string? FindRepoFile(params string[] relative)
    {
        DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "WebWindowUI.slnx")))
            {
                string full = Path.Combine(new[] { dir.FullName }.Concat(relative).ToArray());
                return File.Exists(full) ? full : null;
            }
            dir = dir.Parent;
        }
        return null;
    }
}
