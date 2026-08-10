using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using ProtoBuf;
using WebWindowUI.Core;
using WebWindowUI.Core.Protocol;
using WebWindowUI.Generator;
using WebWindowUI.Sample;
using WebWindowUI.Sample.Items;
using Xunit;

namespace WebWindowUI.Tests;

/// <summary>无生成编码器的模型：完整快照回退到通用 ModelSnapshot，属性变化不推送增量。</summary>
public partial class GenericFallbackModel : WebWindowModel
{
    [ObservableProperty]
    private string _name = "x";
}

public class ModelTests
{
    [Fact]
    public void WireLock_SetNumber_ProducesExpectedBytes()
    {
        var msg = new WebMessage
        {
            Set = new ModelSet { Property = "Count", Value = new ModelValue { Number = 5.0 } },
        };

        var bytes = ModelProtocol.Encode(msg);

        // field3(set=0x1A, len18) → "Count" + field2(value) + field1(double 5.0 = fixed64 LE 00..001440)
        byte[] expected =
        {
            0x1A, 0x12,
            0x0A, 0x05, (byte)'C', (byte)'o', (byte)'u', (byte)'n', (byte)'t',
            0x12, 0x09,
            0x09, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x14, 0x40,
        };
        Assert.Equal(expected, bytes);
    }

    [Fact]
    public void Update_Carrier_CarriesModelIdAndPayload()
    {
        var msg = new WebMessage
        {
            Update = new ModelUpdate
            {
                ModelId = 42,
                Payload = new byte[] { 0x10, 0x05 }, // 字段2 varint 5（Count=5）
            },
        };

        WebMessage? back = ModelProtocol.Decode(ModelProtocol.Encode(msg));

        Assert.NotNull(back?.Update);
        Assert.Equal(42, back.Update.ModelId);
        Assert.Equal(new byte[] { 0x10, 0x05 }, back.Update.Payload);
    }

    [Fact]
    public void Snapshot_WithGeneratedCoder_UsesGeneratedModel()
    {
        var m = new MainWindowModel
        {
            Name = "abc",
            Count = 42,
            Extra = new Dictionary<string, object> { ["x"] = 1 },
        };

        WebMessage? msg = ModelProtocol.Decode(m.BuildSnapshotEnvelope());

        Assert.NotNull(msg);
        Assert.Null(msg.Snapshot);
        Assert.NotNull(msg.Full);
        Assert.NotEqual(0, msg.Full.ModelId); // 模型序号（完整消息名哈希）代替消息名

        var snap = Serializer.Deserialize<MainWindowModelSnapshot>(new MemoryStream(msg.Full.Payload!));
        Assert.Equal("abc", snap.Name);
        Assert.Equal(42, snap.Count);
        Assert.NotNull(snap.Extra);
        Assert.NotNull(snap.Extra.ObjectValue); // object 属性 → ModelValue
        Assert.Equal(1.0, snap.Extra.ObjectValue.Fields["x"].Number);
    }

    [Fact]
    public void Snapshot_WithoutCoder_UsesGenericSnapshot()
    {
        var m = new GenericFallbackModel { Name = "hello" };

        WebMessage? msg = ModelProtocol.Decode(m.BuildSnapshotEnvelope());

        Assert.NotNull(msg);
        Assert.Null(msg.Full);
        Assert.NotNull(msg.Snapshot);
        Assert.Equal("hello", msg.Snapshot.Data["Name"].Text);
    }

    [Fact]
    public void PropertyChange_PushesUpdateEnvelope()
    {
        var m = new MainWindowModel();
        byte[]? pushed = null;
        m.SubscribePushed(b => pushed = b);

        m.Name = "abc";

        Assert.NotNull(pushed);
        WebMessage? msg = ModelProtocol.Decode(pushed!);
        Assert.NotNull(msg?.Update);
        Assert.NotEqual(0, msg.Update.ModelId);

        var upd = Serializer.Deserialize<MainWindowModelUpdate>(new MemoryStream(msg.Update.Payload!));
        Assert.Equal("abc", upd.Name);
        Assert.Null(upd.Count); // 只编码被修改的字段：未修改的不在 wire 上
        Assert.Null(upd.Message);
        Assert.Null(upd.Extra);
    }

    [Fact]
    public void PropertyChange_IntValue_EncodesOnlyChangedField()
    {
        var m = new MainWindowModel();
        byte[]? pushed = null;
        m.SubscribePushed(b => pushed = b);

        m.Count = 5;

        Assert.NotNull(pushed);
        WebMessage? msg = ModelProtocol.Decode(pushed!);
        var upd = Serializer.Deserialize<MainWindowModelUpdate>(new MemoryStream(msg!.Update!.Payload!));
        Assert.Equal(5, upd.Count);
        Assert.Null(upd.Name); // presence：未改字段不在载荷
    }

    [Fact]
    public void PropertyChange_IntZero_StillOnWire()
    {
        // 前端靠 hasOwnProperty 判断字段是否被改（0 是合法值），所以 0 也必须在 wire 上。
        var m = new MainWindowModel { Count = 1 };
        byte[]? pushed = null;
        m.SubscribePushed(b => pushed = b);

        m.Count = 0;

        Assert.NotNull(pushed);
        WebMessage? msg = ModelProtocol.Decode(pushed!);
        var upd = Serializer.Deserialize<MainWindowModelUpdate>(new MemoryStream(msg!.Update!.Payload!));
        Assert.Equal(0, upd.Count); // 可空非 null 的 0 会序列化（presence 保持）
    }

    [Fact]
    public void PropertyChange_WithoutCoder_DoesNotPush()
    {
        var m = new GenericFallbackModel();
        byte[]? pushed = null;
        m.SubscribePushed(b => pushed = b);

        m.Name = "y";

        Assert.Null(pushed); // 无 update 编码器 → 不推送增量
    }

    [Fact]
    public void TrySetProperty_TextToProperty()
    {
        var m = new MainWindowModel();

        Assert.True(m.TrySetProperty("Name", new ModelValue { Text = "xyz" }));
        Assert.Equal("xyz", m.Name);
    }

    [Fact]
    public void TrySetProperty_NumberToIntProperty()
    {
        var m = new MainWindowModel();

        Assert.True(m.TrySetProperty("Count", new ModelValue { Number = 7 }));
        Assert.Equal(7, m.Count);
    }

    [Fact]
    public void TrySetProperty_UnknownProperty_ReturnsFalse()
    {
        var m = new MainWindowModel();
        Assert.False(m.TrySetProperty("NoSuchProperty", new ModelValue { Text = "1" }));
    }

    [Fact]
    public void TrySetProperty_TypeMismatch_ReturnsFalse()
    {
        var m = new MainWindowModel();
        Assert.False(m.TrySetProperty("Count", new ModelValue { Text = "abc" }));
    }

    [Fact]
    public void ObjectProperty_SerializableValue_Passes()
    {
        var m = new MainWindowModel();
        m.Extra = new Dictionary<string, object> { ["a"] = 1 };
        Assert.NotNull(m.Extra);
    }

    [Fact]
    public void ToModelValue_Cycle_Throws()
    {
        var cyclic = new Dictionary<string, object?>();
        cyclic["self"] = cyclic; // 自引用：环检测拦截

        Assert.Throws<InvalidOperationException>(() => ModelProtocol.ToModelValue(cyclic));
    }

    [Fact]
    public void ToModelValue_ObjectMap_Recurses()
    {
        ModelValue v = ModelProtocol.ToModelValue(new Dictionary<string, object?>
        {
            ["count"] = 3,
            ["name"] = "a",
            ["ok"] = true,
        });

        Assert.NotNull(v.ObjectValue);
        Assert.Equal(3.0, v.ObjectValue.Fields["count"].Number);
        Assert.Equal("a", v.ObjectValue.Fields["name"].Text);
        Assert.True(v.ObjectValue.Fields["ok"].Flag);
    }

    [Fact]
    public void ToModelValue_Blob()
    {
        byte[] blob = { 1, 2, 3 };

        ModelValue v = ModelProtocol.ToModelValue(blob);

        Assert.NotNull(v.Blob);
        Assert.Equal(blob, v.Blob);
    }

    [Fact]
    public void ToModelValue_List()
    {
        ModelValue v = ModelProtocol.ToModelValue(new object[] { 1, "x" });

        Assert.NotNull(v.List);
        Assert.Equal(2, v.List.Items.Count);
        Assert.Equal(1.0, v.List.Items[0].Number);
        Assert.Equal("x", v.List.Items[1].Text);
    }

    [Fact]
    public void ToModelValue_Null_ProducesEmpty()
    {
        ModelValue v = ModelProtocol.ToModelValue(null);

        Assert.True(v.Number is null && v.Text is null && v.Flag is null
            && v.List is null && v.ObjectValue is null && v.Blob is null);
    }

    [Fact]
    public void ModelValue_RoundTrip_ThroughWire()
    {
        var msg = new WebMessage
        {
            Set = new ModelSet
            {
                Property = "Extra",
                Value = new ModelValue
                {
                    ObjectValue = new ModelValueMap
                    {
                        Fields = new Dictionary<string, ModelValue>
                        {
                            ["a"] = new ModelValue { Number = 1 },
                            ["nested"] = new ModelValue { Text = "s" },
                        },
                    },
                },
            },
        };

        WebMessage? back = ModelProtocol.Decode(ModelProtocol.Encode(msg));

        Assert.NotNull(back?.Set?.Value?.ObjectValue);
        Assert.Equal(1.0, back.Set.Value.ObjectValue.Fields["a"].Number);
        Assert.Equal("s", back.Set.Value.ObjectValue.Fields["nested"].Text);
    }

    [Fact]
    public void Generator_ProducesExpectedProtoCsAndDescriptor()
    {
        const string src = """
        using CommunityToolkit.Mvvm.ComponentModel;
        using WebWindowUI.Core;

        namespace WebWindowUI.Sample;

        public partial class MainWindowModel : Model
        {
            [ObservableProperty]
            private string name = "小明";

            [ObservableProperty]
            private int count;

            [ObservableProperty]
            private string message = "hi";

            [ObservableProperty]
            private object? extra = new Dictionary<string, object>();
        }
        """;

        ModelProtoResult result = ModelProtoGenerator.Generate(src, "MainWindowModel");

        // .proto：标量强类型 + object 兜底 ModelValue
        Assert.Contains("message MainWindowModel {", result.ProtoText);
        Assert.Contains("string name = 1;", result.ProtoText);
        Assert.Contains("int32 count = 2;", result.ProtoText);
        Assert.Contains("string message = 3;", result.ProtoText);
        Assert.Contains("webwindowui.model.ModelValue extra = 4;", result.ProtoText);

        // C# DTO：字段号与 proto 一致；object → ModelValue；模型序号代替消息名
        Assert.Contains("[ProtoMember(1)] public string Name", result.CsCode);
        Assert.Contains("[ProtoMember(2)] public int Count", result.CsCode);
        Assert.Contains("[ProtoMember(4)] public ModelValue? Extra", result.CsCode);
        Assert.Contains("ModelProtocol.ToModelValue(model.Extra)", result.CsCode);
        Assert.Contains("protected override int ModelId =>", result.CsCode);
        Assert.DoesNotContain("MessageName", result.CsCode); // 消息名不下发，C# 只烘焙序号

        // descriptor JSON：MainWindowModel 字段结构与类型引用
        using JsonDocument json = JsonDocument.Parse(result.DescriptorJson);
        JsonElement msg = json.RootElement.GetProperty("nested").GetProperty("webwindowui")
            .GetProperty("nested").GetProperty("model").GetProperty("nested")
            .GetProperty("generated").GetProperty("nested").GetProperty("MainWindowModel");
        Assert.Equal(1, msg.GetProperty("fields").GetProperty("name").GetProperty("id").GetInt32());
        Assert.Equal("int32", msg.GetProperty("fields").GetProperty("count").GetProperty("type").GetString());
        Assert.Equal("webwindowui.model.ModelValue", msg.GetProperty("fields").GetProperty("extra").GetProperty("type").GetString());
    }

    // ---- 多窗口共享广播（Feature 1a）----

    [Fact]
    public void MultipleSubscribers_AllReceiveUpdate()
    {
        var m = new MainWindowModel();
        int a = 0, b = 0;
        m.SubscribePushed(_ => a++);
        m.SubscribePushed(_ => b++);

        m.Name = "abc";

        Assert.Equal(1, a);
        Assert.Equal(1, b);
    }

    [Fact]
    public void Unsubscribe_StopsReceiving()
    {
        var m = new MainWindowModel();
        int a = 0, b = 0;
        Action<byte[]> handlerA = _ => a++;
        m.SubscribePushed(handlerA);
        m.SubscribePushed(_ => b++);

        m.UnsubscribePushed(handlerA);
        m.Name = "abc";

        Assert.Equal(0, a);
        Assert.Equal(1, b);
    }

    [Fact]
    public void BroadcastPropertyUpdate_ExcludesSource()
    {
        var m = new MainWindowModel();
        int source = 0, other = 0;
        Action<byte[]> sourceHandler = _ => source++;
        Action<byte[]> otherHandler = _ => other++;
        m.SubscribePushed(sourceHandler);
        m.SubscribePushed(otherHandler);

        m.TrySetProperty("Count", new ModelValue { Number = 7 });
        m.BroadcastPropertyUpdate("Count", sourceHandler);

        // TrySetProperty 期间 PropertyChanged 回声被抑制；广播排除源窗口 → 只有 other 收到
        Assert.Equal(0, source);
        Assert.Equal(1, other);
    }

    [Fact]
    public void IndependentInstances_DoNotCrossTalk()
    {
        var a = new MainWindowModel();
        var b = new MainWindowModel();
        int aPushes = 0, bPushes = 0;
        a.SubscribePushed(_ => aPushes++);
        b.SubscribePushed(_ => bPushes++);

        a.Name = "only-a";

        Assert.Equal(1, aPushes);
        Assert.Equal(0, bPushes);
    }

    // ---- List<Model> 回写（Feature 2）----

    [Fact]
    public void TryFromModelValue_Poco_ConstructsInstance()
    {
        var map = new ModelValueMap
        {
            OrdinalFields = new Dictionary<int, ModelValue>
            {
                [1] = new ModelValue { Text = "task" },       // 序数键（proto 字段号 int）：title=1
                [2] = new ModelValue { Flag = true },         // done=2
                [999] = new ModelValue { Text = "ignored" },  // 未知序数键跳过
            },
        };

        Assert.True(ModelProtocol.TryFromModelValue(
            new ModelValue { ObjectValue = map }, typeof(TodoItemModel), out object? result));
        var todo = Assert.IsType<TodoItemModel>(result);
        Assert.Equal("task", todo.Title);
        Assert.True(todo.Done);
    }

    [Fact]
    public void ToModelValue_Poco_UsesOrdinalKeys()
    {
        // 反向序列化器（ConvertToModelValue，[ModuleInitializer] 注册）用 proto 字段号键（真实 int）而非属性名，
        // 与 ConvertFromModelValue 对称；List<Model> 整列表增量 update 元素以序数对象 map 走线。
        var v = ModelProtocol.ToModelValue(new TodoItemModel { Title = "task", Done = true });

        Assert.NotNull(v.ObjectValue);
        Assert.Equal("task", v.ObjectValue!.OrdinalFields[1].Text);
        Assert.True(v.ObjectValue.OrdinalFields[2].Flag);
        Assert.False(v.ObjectValue.OrdinalFields.ContainsKey(3));
        Assert.False(v.ObjectValue.Fields.ContainsKey("title")); // name 键 map 保持空（序数走 OrdinalFields）
    }

    [Fact]
    public void PocoConverters_RegisteredByModuleInitializer()
    {
        // 样例后端程序集加载即触发各模型的 [ModuleInitializer] 注册生成转换器（替代反射重建）。
        // 引用 WebWindowUI.Sample 类型确保程序集已加载（ModuleInitializer 在首次加载时执行）。
        _ = typeof(TodoItemModel).Assembly;

        Assert.Contains(typeof(TodoItemModel), ModelProtocol._pocoConverters.Keys);
        Assert.Contains(typeof(TodoListModel), ModelProtocol._pocoConverters.Keys);
        Assert.Contains(typeof(MainWindowModel), ModelProtocol._pocoConverters.Keys);
        Assert.Contains(typeof(LauncherModel), ModelProtocol._pocoConverters.Keys);
        Assert.Contains(typeof(AboutModel), ModelProtocol._pocoConverters.Keys);
        Assert.Contains(typeof(SettingsModel), ModelProtocol._pocoConverters.Keys);

        // 反向序列化器同批注册（实例 → 序数对象 map，替换反射序列化）
        Assert.Contains(typeof(TodoItemModel), ModelProtocol._pocoSerializers.Keys);
        Assert.Contains(typeof(MainWindowModel), ModelProtocol._pocoSerializers.Keys);

        // 有参构造（string instanceId = "共享"）→ 不生成转换器
        Assert.DoesNotContain(typeof(MultiWindowModel), ModelProtocol._pocoConverters.Keys);
        Assert.DoesNotContain(typeof(MultiWindowModel), ModelProtocol._pocoSerializers.Keys);

        // 无生成代码的测试 fixture → 仍走反射回退
        Assert.DoesNotContain(typeof(GenericFallbackModel), ModelProtocol._pocoConverters.Keys);
        Assert.DoesNotContain(typeof(GenericFallbackModel), ModelProtocol._pocoSerializers.Keys);
    }

    [Fact]
    public void TrySetProperty_ListOfModel_ReconstructsList()
    {
        var m = new TodoListModel();
        var value = new ModelValue
        {
            List = new ModelValueList
            {
                Items =
                {
                    // 元素对象 map 用序数键（title=1、done=2，int），与桥 ordinalFields 编码一致
                    new ModelValue { ObjectValue = new ModelValueMap { OrdinalFields = new Dictionary<int, ModelValue> { [1] = new ModelValue { Text = "a" }, [2] = new ModelValue { Flag = true } } } },
                    new ModelValue { ObjectValue = new ModelValueMap { OrdinalFields = new Dictionary<int, ModelValue> { [1] = new ModelValue { Text = "b" }, [2] = new ModelValue { Flag = false } } } },
                },
            },
        };

        Assert.True(m.TrySetProperty("Todos", value));
        Assert.Equal(2, m.Todos.Count);
        Assert.Equal("a", m.Todos[0].Title);
        Assert.True(m.Todos[0].Done);
        Assert.Equal("b", m.Todos[1].Title);
    }

    [Fact]
    public void ObservableCollection_AddRemove_PushesPatch()
    {
        var m = new TodoListModel { Todos = { new TodoItemModel { Title = "t1", Done = true } } };
        int pushed = 0;
        byte[]? last = null;
        m.SubscribePushed(b => { pushed++; last = b; });

        // 集合订阅须先武装：字段初始化器在基类构造之后执行，基类 ctor 扫描不到初始集合
        m.ArmCollectionSubscriptions();

        // .Add → Insert 补丁（#3 差量通道）
        m.Todos.Add(new TodoItemModel { Title = "t2" });
        Assert.Equal(1, pushed);
        WebMessage? add = ModelProtocol.Decode(last!);
        Assert.NotNull(add?.Patch);
        Assert.Equal(CollectionPatchAction.Insert, add!.Patch!.Action);
        Assert.Equal("Todos", add.Patch.Property);
        Assert.Equal(1, add.Patch.Index); // 原 1 元素后追加
        Assert.Equal("t2", add.Patch.Items[0].ObjectValue!.OrdinalFields[1].Text); // 序数键：字段 1 = Title

        // .RemoveAt → Remove 补丁
        m.Todos.RemoveAt(0);
        Assert.Equal(2, pushed);
        WebMessage? remove = ModelProtocol.Decode(last!);
        Assert.NotNull(remove?.Patch);
        Assert.Equal(CollectionPatchAction.Remove, remove!.Patch!.Action);
        Assert.Equal(0, remove.Patch.Index);
        Assert.Equal(1, remove.Patch.Count);

        // .Clear → Reset 补丁（事件不带新旧元素，Items 承载整列表 = 空）
        m.Todos.Clear();
        Assert.Equal(3, pushed);
        WebMessage? reset = ModelProtocol.Decode(last!);
        Assert.NotNull(reset?.Patch);
        Assert.Equal(CollectionPatchAction.Reset, reset!.Patch!.Action);
        Assert.Empty(reset.Patch.Items);
    }

    [Fact]
    public void ObservableCollection_Reassigned_SwitchesSubscription()
    {
        var m = new TodoListModel();
        int pushed = 0;
        m.SubscribePushed(_ => pushed++);
        m.ArmCollectionSubscriptions();

        // 替换集合实例：属性替换本身推送一次（与 List<T> 一致），随后旧实例退订、新实例订阅
        var old = m.Todos;
        var fresh = new ObservableCollection<TodoItemModel>();
        m.Todos = fresh;
        Assert.Equal(1, pushed);

        old.Add(new TodoItemModel { Title = "ghost" });
        Assert.Equal(1, pushed); // 旧实例已退订，不推送

        fresh.Add(new TodoItemModel { Title = "live" });
        Assert.Equal(2, pushed); // 新实例已订阅，推送
    }

    // ---- ObservableDictionary 原地自动推送 + 集合免 [ObservableProperty] ----

    [Fact]
    public void ObservableDictionary_InPlaceMutation_PushesWholePropertyUpdate()
    {
        // .NET 侧原地改字典（dict[k]=v）→ ObservableDictionary 抛 CollectionChanged → 框架整属性重推前端。
        var m = new NestedListModel();
        byte[]? pushed = null;
        m.SubscribePushed(b => pushed = b);
        m.ArmCollectionSubscriptions();

        m.Counts["items"] = 99;

        Assert.NotNull(pushed);
        WebMessage? msg = ModelProtocol.Decode(pushed!);
        Assert.NotNull(msg?.Update);
        Assert.NotEqual(0, msg.Update.ModelId);

        // Counts 是 ModelValue 兜底（非 typed repeated）→ name 键对象 map 整体替换前端对象
        var upd = Serializer.Deserialize<NestedListModelUpdate>(new MemoryStream(msg.Update.Payload!));
        Assert.NotNull(upd.Counts);
        Assert.Equal(99.0, upd.Counts!.ObjectValue!.Fields["items"].Number);
        Assert.Equal(4.0, upd.Counts.ObjectValue.Fields["tags"].Number); // 未动键保持
    }

    [Fact]
    public void ObservableDictionary_Add_AlsoPushesWholePropertyUpdate()
    {
        var m = new NestedListModel();
        int pushed = 0;
        m.SubscribePushed(_ => pushed++);
        m.ArmCollectionSubscriptions();

        m.Counts.Add("newkey", 1);

        Assert.Equal(1, pushed); // 整属性重推（非索引差量），一次推送
    }

    [Fact]
    public void ObservableDictionary_Writeback_Reconstructs()
    {
        // 前端整字典回写 → TrySetProperty → TryConvertObject ObservableDictionary 分支重建同类实例。
        var m = new NestedListModel();
        var value = new ModelValue
        {
            ObjectValue = new ModelValueMap
            {
                Fields = new Dictionary<string, ModelValue>
                {
                    ["items"] = new ModelValue { Number = 7 },
                    ["tags"] = new ModelValue { Number = 8 },
                    ["extra"] = new ModelValue { Number = 1 },
                },
            },
        };

        Assert.True(m.TrySetProperty("Counts", value));
        Assert.Equal(7, m.Counts["items"]);
        Assert.Equal(8, m.Counts["tags"]);
        Assert.Equal(1, m.Counts["extra"]);
    }

    [Fact]
    public void TrySetProperty_GetOnlyCollection_InPlaceReconstructs()
    {
        // 显式 get-only 集合属性（不加 [ObservableProperty]）：前端整列回写照常重建，
        // 且生成器原地清空重建保留实例（get-only 没有 setter，替换写法根本不可能）。
        var m = new NestedListModel();
        var oldItems = m.Items;
        var value = new ModelValue
        {
            List = new ModelValueList
            {
                Items =
                {
                    new ModelValue { ObjectValue = new ModelValueMap { OrdinalFields = new Dictionary<int, ModelValue> { [1] = new ModelValue { Text = "a" }, [2] = new ModelValue { Flag = true } } } },
                    new ModelValue { ObjectValue = new ModelValueMap { OrdinalFields = new Dictionary<int, ModelValue> { [1] = new ModelValue { Text = "b" }, [2] = new ModelValue { Flag = false } } } },
                },
            },
        };

        Assert.True(m.TrySetProperty("Items", value));
        Assert.Same(oldItems, m.Items); // 原地重建保留实例与订阅
        Assert.Equal(2, m.Items.Count);
        Assert.Equal("a", m.Items[0].Title);
        Assert.True(m.Items[0].Done);
        Assert.Equal("b", m.Items[1].Title);
        Assert.False(m.Items[1].Done);
    }

    [Fact]
    public void ObservableCollection_Writeback_PreservesInstance()
    {
        // [ObservableProperty] ObservableCollection 写回由替换实例改为原地清空重建：保留订阅
        //（既有 ObservableCollection_Reassigned_SwitchesSubscription 依赖替换时的订阅切换，此处验证原地路径）。
        var m = new TodoListModel();
        var old = m.Todos;
        var value = new ModelValue
        {
            List = new ModelValueList
            {
                Items =
                {
                    new ModelValue { ObjectValue = new ModelValueMap { OrdinalFields = new Dictionary<int, ModelValue> { [1] = new ModelValue { Text = "x" }, [2] = new ModelValue { Flag = false } } } },
                },
            },
        };

        Assert.True(m.TrySetProperty("Todos", value));
        Assert.Same(old, m.Todos);
        Assert.Single(m.Todos);
        Assert.Equal("x", m.Todos[0].Title);
    }

    // ---- MVVM 命令（TryInvokeCommand）----

    [Fact]
    public void TryInvokeCommand_NoArgCommand_Executes()
    {
        var model = new LauncherModel();
        string? opened = null;
        model.OpenRequested += p => opened = p;

        Assert.True(model.TryInvokeCommand(0, null)); // 无参命令（声明序 0）：value 缺省
        Assert.Equal("main", opened);
    }

    [Fact]
    public void TryInvokeCommand_WithArg_ConvertsValue()
    {
        var model = new LauncherModel();
        model.ButtonEnable = true;
        string? opened = null;
        model.OpenRequested += p => opened = p;

        Assert.True(model.TryInvokeCommand(1, ModelProtocol.ToModelValue("todos")));
        Assert.Equal("todos", opened);
    }

    [Fact]
    public void TryInvokeCommand_CanExecute_GatesRefusal()
    {
        var model = new LauncherModel(); // ButtonEnable 默认 false → CanExecute=false
        string? opened = null;
        model.OpenRequested += p => opened = p;

        Assert.False(model.TryInvokeCommand(1, ModelProtocol.ToModelValue("todos")));
        Assert.Null(opened); // 门控拒绝，命令方法不执行

        model.ButtonEnable = true; // 开启门控源
        Assert.True(model.TryInvokeCommand(1, ModelProtocol.ToModelValue("todos")));
        Assert.Equal("todos", opened);
    }

    [Fact]
    public void TryInvokeCommand_UnknownCommand_ReturnsFalse()
    {
        var model = new LauncherModel();
        Assert.False(model.TryInvokeCommand(99, null));
    }
}
