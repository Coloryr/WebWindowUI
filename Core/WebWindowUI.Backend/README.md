# WebWindowUI.Backend（后端角色标记包）

**模型库工程引用本包即声明「后端」角色**——`WebWindowUI.targets` 据此识别（`%(Identity)`/`%(Filename)` 精确匹配）并触发模型 → proto/descriptor/TS 生成与集合订阅等处理。这是**角色判定**而非内容启发式：引了标记即被当成模型库，与工程名无关（`Foo.Backend` 这种名字带关键字但不引用标记的工程不会被误判）。

一条 `PackageReference` 即带回：入口 `WebWindowUI`（共享 targets + 平台 + Core）+ 源生成器。

## 内容

- 空标记类 `Marker.cs`（角色证明）。
- 依赖入口包 `WebWindowUI`。
- **内嵌源生成器到 `analyzers/dotnet/cs/`**：`WebWindowUI.Generator.SourceGen.dll`（WriteBackGenerator + ProtoGenerator，见 Generator 层 README）沿包依赖图传给引用链上所有模型库，模型库零手写分析器引用。

## 用法

模型库工程（三工程结构里的 `<App>.Backend`）加：

```xml
<ProjectReference Include="..\WebWindowUI.Backend\WebWindowUI.Backend.csproj" />
```

或包模式 `PackageReference WebWindowUI.Backend`。之后把 `*Model.cs` 放进工程即自动生成协议。
