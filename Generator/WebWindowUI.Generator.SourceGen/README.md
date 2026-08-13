# WebWindowUI.Generator.SourceGen（Roslyn 源生成器）

netstandard2.0、`IsRoslynComponent=true` 的**两个 IIncrementalGenerator**（都内存产出、都 partial，合并进同一类型）。不产独立 NuGet 包（`IsPackable=false`）：打包时内嵌进 `WebWindowUI.Backend` 包的 `analyzers/dotnet/cs/`，沿包依赖图传给引用链上所有模型库（仓库模式由根 `Directory.Build.targets` 对 ProjectReference 到 Backend 的模型库注入 `@(Analyzer)`）。引用标记即得生成器，别再给模型库写自己的 SourceGen 引用。

## WriteBackGenerator

每个 `WebWindowModel` 子类产 `{Model}.WriteBack.g.cs`（5 个成员）：

- `TrySetGeneratedProperty` / `TryGetGeneratedProperty`（switch(name)）
- `TryInvokeGeneratedCommand`（CanExecute 门控，无参命令按 `typeof(object)`）
- `SubscribeGeneratedCollections`
- `ConvertFromModelValue` / `ConvertToModelValue` + `[ModuleInitializer] __WWUI_RegisterPocoConverter`（注册进 `_pocoConverters`/`_pocoSerializers`，**反射兜底已移除**）

## ProtoGenerator

产 `{Model}Proto.g.cs`（原 console C# 部分改造，逻辑本体 `ModelProtoGenerator`）。给模型生成 proto 编码器（`ModelIdFor` 哈希、`CollectCommands` 命令序号、`EnsureItemsSubscribed` 集合订阅、`ToModelValue` 序列化等）。

## 序数键

POCO 序列化/反序列化按 proto 字段号 int 键（非属性名）。序列化器用 `m.Props`（全可读属性），反序列化器只用 WritableProps（未知序数键跳过）。

## durable 坑（踩过，勿改）

- 生成器跑在**未加生成源码的初始编译**上，属性名只能从 `[ObservableProperty]` **字段符号**推（剥**一个**前导 `_` 再 PascalCase，`__name→_Name`），命令属性名从 `[RelayCommand]` **方法符号**推。
- 生成的 partial 是**派生类**：基类 `EnsureCollectionSubscribed` 必须 `private`→`protected`；生成代码只经 `ApplyRemoteWrite(Action setter)`（抑制回声）/`EnsureCollectionSubscribed` 间接碰私有成员。
- POCO 注册用 `[ModuleInitializer]` 而非 static ctor（static ctor 可能还没跑）；`delegate bool PocoConvertFunc(ModelValueMap, out object?)`（`Func` 无法区分「失败」与「null POCO」）。
- netstandard2.0 API 缺口：无 `IReadOnlySet<T>`（用 `IReadOnlyCollection`）、无 `ToHashSet()`（用 `new HashSet<T>(...)`）、无单字符 `string.Split(char, StringSplitOptions)`（用 `Split(new[]{'/'}, ...)`）。

## 增量重构

WriteBack 按模型注册输出（`RegisterSourceOutput`，单模型变化只重产该模型）；Proto 解析一次 + 值相等上下文（`EquatableArray<T>` 的「类名→命名空间」表——改其它模型字段时 Roslyn 短路、`models.Combine(allNamespaces)` 独立缓存，typed-repeated 检测只依赖命名空间表）。`EquatableArray<T>` 为顶层 internal。
