# WebWindowUI.Tests.Protocol

**协议 / 单元测试**（纯逻辑，跨主机可构建）：模型、生成器、协议、resolver、BuildHomeUrl。**不直接引用平台工程 / Sample 应用**（平台 dll 仍经 Sample.Backend → 标记包 → 入口链传递进 bin，与拆分前一致，但测试不碰 WebView2/WebKit 运行时）；bin 无真实 wwwroot 资源，Linux/macOS 上也能构建。

## 组成

| 文件 | 覆盖 |
|------|------|
| `ModelTests.cs` | WebWindowModel 基础（快照/属性/集合推送） |
| `ModelProtoTests.cs` | 协议编码（`ModelProtocol`/信封/序数键）+ 漂移测试 `BaseEnvelope_InlineInEveryDescriptor_MatchesCompiledDto`（锁 descriptor ↔ `[ProtoMember]`） |
| `WriteBackGeneratorTests.cs` | WriteBack 生成器（`CSharpGeneratorDriver` + `.AsSourceGenerator()`） |
| `GeneratorTests.cs` | console 生成器逻辑 |
| `WebResourceLocatorTests.cs` | WebResourceResolver |
| `WebWindowTests.cs` | 窗口抽象 / BuildHomeUrl |
| `AssemblyInfo.cs` | xunit `DisableTestParallelization` |

## 关键约束

- 引 `WebWindowUI.Sample.Backend` 拿模型类型；命名空间 `WebWindowUI.Tests`——**`using WebWindowUI.Sample;` 不能当冗余删**（`WebWindowUI.Sample` 是 `WebWindowUI` 的**兄弟**子命名空间，不在解析链上；`using WebWindowUI;` 才是真冗余）。
- 生成器测试断言纪律：`parseOptions` 必须与输入树一致（默认 Latest、输入用 Preview 抛不一致）；「无输出」不能 `Assert.Empty(run)`（Proto 对无 `[ObservableProperty]` 的 EmptyModel 也产 Proto.g.cs）——按 hint 名断言 WriteBack 缺席。

## 回归计数

macOS/Linux/Windows 主机都跑这套：105 个用例。
