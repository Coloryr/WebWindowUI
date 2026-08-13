# WebWindowUI

跨平台桌面应用框架：.NET 模型库（WebWindowModel）用 Roslyn 源生成器产出 proto 消息/写回代码，Vue3+Vite 前端通过 `webwindowui-bridge` 双向绑定，Windows=WebView2 / Linux=WebKit2GTK（GTK3）/ macOS=WKWebView。**内存全部来自本仓库的迭代踩坑，遇到不理解的构建行为先查这里。**

**包结构（入口聚合，消费方只引 `WebWindowUI` 一个包）**：`WebWindowUI`（入口，聚合 + 平台引导）+ `WebWindowUI.Core`（运行时代码本体，平台无关）+ `WebWindowUI.Platforms.{Windows,Linux,MacOS,Cef}`（平台实现，按系统自动引入）+ `WebWindowUI.Natives.Windows`（Win32 共享层，Windows/CEF 平台共用）+ `WebWindowUI.Backend`/`WebWindowUI.Frontend`（角色标记包）+ `WebWindowUI.Templates`。依赖单向无环：平台包→{Core, Natives.Windows}→（Mvvm/protobuf），入口→{Core, 平台包}。详见「NuGet 打包」与「平台拆分」两节。

## 三工程结构（核心约定）

每个应用是**三个子工程**（目录 = 工程名）：

```
<App>/                      → 应用 exe（WinExe，Program.cs + 窗口类，产出 <App>.exe）
<App>.Backend/              → 模型库（*Model.cs 模型类 + DataProvider），引用 WebWindowUI.Backend
<App>.Frontend/             → 纯 Vue（真实 .csproj，EnableDefaultItems=false），引用 WebWindowUI.Frontend
```

- **角色判定（取代内容启发式）**：工程引用哪个标记类库即证明角色——`WebWindowUI.Backend`（触发模型→proto/descriptor/TS 生成）、`WebWindowUI.Frontend`（激活本工程 vite 目标）、都不引用 = 应用（构建前端 + wwwroot 传递复制）。**ProjectReference 按 %(Filename) 精确匹配；PackageReference 按 %(Identity) 精确匹配**（包名无扩展名时 %(Filename) 会把 `.Backend` 剥成 `WebWindowUI`）。精确匹配保证 `Foo.Backend` 这类名字带关键字但不引用标记的工程不误判。
- **仓库模式对齐包模式（根 Directory.Build.props/.targets）**：仓库内工程**只引用标记库**即自动获得平台选择、共享 targets 与源生成器，无需手写 `<Import WebWindowUI.Platform.props>`/`<Import WebWindowUI.targets>` 或 SourceGen 引用（Sample/Demo/框架/Tests 已全部不显式导入；模板/Demo 消费方走包模式天然如此）。平台选择经根 `Directory.Build.props` Import `WebWindowUI.Platform.props`（见平台拆分节）；根 `Directory.Build.targets` 做另两件事：① `_WebWindowUITargetsLoaded` 空时 Import `WebWindowUI.targets`（等价包模式 buildTransitive——包模式 Demo 的 buildTransitive 在 .nuget.g.targets 里先于 Directory.Build.targets 导入、已置位 → 跳过不双导入）；② 对「ProjectReference 到 `WebWindowUI.Backend`」的模型库注入 `@(Analyzer)` = SourceGen.dll（等价包模式 `analyzers/dotnet/cs/`）。**durable 坑：ProjectReference 的 `OutputItemType="Analyzer"` 不透传**——`ResolveProjectReferences` 逐直接引用取 `GetTargetPath`，标记工程引用 SourceGen 给不到消费方；包模式能透传是 NuGet 的 analyzers 沿包依赖图传递，与 ProjectReference 无关。故仓库模式只能由根文件直接注入，且判定按「引用类型」区分（不能用 `@(_WWUI_BackendRef)`：它对 PackageReference 也非空，会把包模式消费方双注入成 CS0101）。
- **前端工程定位**：targets 对每个 ProjectReference 跑 `_WWUI_ReportFrontend`（前端工程自识别自己引了 `WebWindowUI.Frontend` 标记并回传全路径），`_WWUI_DiscoverFrontend` 捕获 TargetOutputs → `WebWindowUIFrontendProject`。显式声明的 `WebWindowUIFrontendProject` 仍优先。Backend 不 ProjectReference 前端，`WebWindowUIBridgeDir` 默认按「去掉 .Backend 后缀的兄弟前端工程」布局约定推导。
- **模型发现**：targets 的 `ModelProtoFile Include="**\*Model.cs" Exclude="obj\**\*Model.cs;bin\**\*Model.cs"` **递归**扫后端工程（含 `Items\` 子目录），**文件名即类名、必须 `*Model.cs` 结尾**（`MonitorSettings.cs` 不命中、`MonitorSettingsModel.cs` 才命中）。ProtoBase 由类名推导（PascalCase → snake_case）。
- **根命名空间自动推断**：生成器 `--all-models` 收全部模型文件，取最长公共段前缀作根；剩余段小写进 TS 子路径（`WebWindowUI.Sample.Users` → `src/models/users/`）。**想调整 TS 目录布局只需改 C# 命名空间**，无任何配置。

## 构建链路（按配置分叉）

- **Debug**：应用 `BuildFrontend` 调前端工程（`Targets="WebWindowUIBuildFrontend"`，`WwwrootDir=$(TargetDir)wwwroot`）→ vite **直产应用 bin/wwwroot**；`AddWebWindowUIResourcesCopy` 把 bin/wwwroot 经 ContentWithTargetPath 注入（wwwroot 求值期不存在，不能走 Content→AssignTargetPaths）。测试工程经 ProjectReference 传递复制。
- **Release**：应用两个目标都短路，改由**前端工程自驱动**——`_WWUI_FrontendSelfDrive` 在 CoreCompile 前跑 vite 到前端工程 `obj/<Config>/<TFM>/wwwroot`，`_WWUI_EmbedWwwroot` 把它注入 EmbeddedResource **编进前端 dll**（LogicalName=`wwwroot\相对路径`）；应用对前端 ProjectReference 的 `ReferenceOutputAssembly` 由 targets 按配置注入（Debug=false 不复制空 dll、Release=true 复制带资源 dll）。前端 dll 是纯 Vue 工程、本身无 C# 类型，targets 另注入 `FrontendHost` 空标记类型进前端 dll、`FrontendLoad` 模块初始化器进应用 dll——应用启动即 `typeof` 静态引用强制加载前端 dll（AOT 安全，见 WebResourceResolver 节）。
- **前端增量**：`FrontendInput`（src/public/package.json/vite.config/tsconfig）vs `FrontendOutput`（`$(WwwrootDir)\window\**\index.html` + `.frontend-stamp` Touch 标记）。**node_modules 不在 FrontendInput** → 改桥后 `dotnet build` 会跳过 vite 重建（bundle 是旧的，坑）；强制重建须 touch 一个 FrontendInput 文件。
- **vite 产物按配置压缩**：经 `WWUI_CONFIGURATION=$(Configuration)` 传给 vite.config.ts，Release minify / Debug 不压缩 + inline sourcemap。**vite 8 是 rolldown 内核，`minify` 写 `true` 不能写 `'esbuild'`**（会去加载未安装的 esbuild 报错）。
- **GenerateModelProto 不设 Inputs/Outputs，每次构建都执行**：生成器幂等写（内容相同不写、保持 mtime），descriptor/TS 缺失时必重建，内容不变时 mtime 不动、不触发 vite 重建。

### Release 内嵌 MSBuild 坑（durable）

- `$(IntermediateOutputPath)` 由 Sdk.targets 在 csproj 正文**之后**才定义 → 正文里拼它得到空串、`WwwrootDir` 塌缩成裸 `wwwroot`。须用 `$(BaseIntermediateOutputPath)$(Configuration)\$(TargetFramework)\wwwroot`。
- **元数据函数在项元数据值里不展开**：`%(RecursiveDir.Replace(...))` 会被当字面量。只用普通元数据引用（`wwwroot\%(_WWUI_EmbedFiles.RecursiveDir)%(Filename)%(Extension)`）。
- **注入点挂 `BeforeTargets="_GenerateCompileInputs"`**（不是 CoreCompile——SDK 在 `_GenerateCompileInputs` 才把 EmbeddedResource 组装成 Csc 的 Resources 参数），须显式给 `Type=Non-Resx`、`WithCulture=false` 元数据。
- **Release 内嵌资源名是反斜杠**：`wwwroot\window\main\index.html`，反射查内嵌资源须按 `wwwroot\*` 匹配，正斜杠过滤会漏查。
- **MSBuild XML 注释不能含 `--`**（写 `--all-models` 报 MSB4024）；提 CLI 参数去掉一个短横线。

### MSBuild 求值限制（直接踩坑换来的）

- 求值期 PropertyGroup 条件**不能引用 item 列表/元数据**（MSB4099/MSB4190/MSB4191）→ 角色判定放 ItemGroup 用 `WithMetadataValue` 过滤，Target 条件（exec 期）才用 `'@(Item)' != ''`。
- **属性函数参数不展开 item 引用**：`GetDirectoryName('$(P)')` 在 P=`@(Item)` 时收到字面量。目录一律在 item 层 `%(Directory)` 派生。
- **`%(Directory)` 不含 RootDir**：对绝对路径返回去盘符部分，必须拼 `%(RootDir)%(Directory)` 才是绝对目录，否则生成器 Exec 以相对路径落盘到错误位置。
- **Outputs/Inputs 是 item 感知上下文**，item 引用拼字符串会 MSB4012 → 桥 descriptor 目录（=@(Item)）不能进 Outputs。
- **targets 批处理作用域坑**：以 `%(Identity)` 批处理的目标体内，对 @(ModelProtoFile) 的 transform 会被限定到当前批次——`--all-models` 要先在独立目标（无批处理元数据引用）把全量路径 transform 转成项、再裸 `@(Item)` 进属性，Exec 里引用属性。
- **targets 内 Condition 在自身 DependsOnTargets 前求值** → 探测结果（`_WWUI_DiscoverFrontend`）不能用进 target Condition，只能放 body 的 ItemGroup/MSBuild 守卫里。
- **ReferenceOutputAssembly 注入时序**：SDK 在 `AssignProjectConfiguration` 里已把空 ROA 默认 true、快照进 `_MSBuildProjectReferenceExistent`——注入目标必须 `BeforeTargets="AssignProjectConfiguration;_SplitProjectReferencesByFileExistence;ResolveProjectReferences"`，挂后两个已太晚。
- **Exe 工程 ProjectReference 的 apphost 泄漏**：`ReferenceOutputAssembly="false"` 拦不住 apphost.exe + runtimeconfig/deps 经 GetCopyToOutputDirectoryItems 透传复制（SDK 透传条件 `%(_MSBuildProjectReferenceExistent.Private) != 'false'`）——须**同时**加 `Private="false"`。

## WebResourceResolver（wwwroot 两来源、内嵌优先）

先查程序集嵌入资源再回退磁盘（Debug 磁盘、Release 内嵌，互斥存在）。内嵌查找名 = `wwwroot\` + 相对路径（`/`→`\`）。内嵌程序集**懒发现**（单例缓存）：只扫 AppDomain 已加载程序集，取含 `wwwroot\` 前缀资源的程序集。Release 下前端 dll 虽在 deps.json/产物目录但应用不引用其类型、不会自动 Load——靠 targets 注入的**宿主标记机制**强制加载：`_WWUI_InjectFrontendHost` 把 `FrontendHost`（空标记类型）注入编译进每个前端工程 dll，`_WWUI_InjectFrontendLoad` 把 `FrontendLoad`（`[ModuleInitializer]`，`GC.KeepAlive(typeof(FrontendHost))`）注入编译进应用工程——进程启动时 `typeof` 静态引用前端 dll：JIT 下强制加载进已加载程序集、NativeAOT 下根进链接闭包（内嵌 wwwroot 随之保留），**全程无 Assembly.Load**。两文件随入口包分发到 `build\;buildTransitive\`。Program.cs 图标也走同一 resolver。**平台调度与前端 dll 的 Assembly.Load 已全部清除**（此前 WebResourceResolver 按名 `Assembly.Load`/`LoadFrom` 扫 BaseDirectory 的 AOT 遗留不再存在）。

## 数据绑定 / 推送 / 集合

- **单属性变化自动推送**：快照/补丁走 protobuf，页面加载完成后推完整快照；前端回写（ModelSet）写回属性。**同一模型实例绑多窗口 = 共享广播**（多订阅者 `List<Action<byte[]>>`）；远程回写应用后 `BroadcastPropertyUpdate(property, exclude=源窗口)` 排除源窗口。
- **线缆无消息名（ModelId/CommandId 代替字符串）**：`ModelUpdate`/`GeneratedModel` 只发 `int32 modelId`（= 完整消息名 FNV-1a 哈希，生成器 `ModelIdFor` 单一来源、.NET 与 TS 两侧同函数产出）+ `payload`，`ModelInvoke` 只发 `int32 commandId`（= `[RelayCommand]` 方法声明序，`ModelProtoGenerator.CollectCommands` 与 `WriteBackGenerator` 同读源声明序）。`WebWindowModel.ModelId` 非 0 表示有生成编码器（0 回退通用 ModelSnapshot）。**durable 坑：前端解码类型靠生成器烘焙进 TS 镜像类的 `static ['__protocol'] = { modelId, full, update }`**（字符串字面量键，同 `__repeatedFields`——别用运行时 `constructor.name` 反射，Release minify 会改 class 绑定名）。`ModelSet.Property`/`CollectionPatch.Property` 仍留字符串（属性名按名回写，未纳入序号化）。
- **ObservableCollection 原地增删自动推送**：.NET 侧 `.Add()/.Remove()` 即自动整列表推送（不必整体替换列表属性；List 原地 Add 不触发 PropertyChanged 的旧坑绕开）。集合属性**免 [ObservableProperty]**（get-only 也双向）。
- **集合差量补丁 CollectionPatch**：ObservableCollection 增删走**差量**（`WebMessage` oneof `patch`，action Insert/Remove/Replace/Move/Reset/ElementSet）；`Reset`（如 .Clear）不带元素无法差量 → 回退整列表补丁。补丁自包含。前端 `applyPatch` 对响应式数组**原地 splice**。
- **元素级双向（List\<已知模型\> 元素字段，按 ModelInstanceId 寻址）**：改单个元素属性（如 `todos[0].Done`）不再整列表往返。**前端→.NET**：typed-repeated 属性拆成「结构 watch（浅拷贝只读 length+下标，push/splice/替换触发）+ 每元素每字段 watch」，字段变化发 `ModelSet{ property=集合, elementInstanceId, elementProperty, value }`，.NET 按 id 找元素、`_isApplyingRemoteWrite` 下 `item.TrySetProperty` 原地写（**保实例**，非 Clear+Add 重建）；元素未带 id（旧端）退回整列表回写兜底。**.NET→前端**：模型元素集合自动订阅元素 PropertyChanged（生成器 `EnsureItemsSubscribed` 产出 + `OnCollectionChanged`/属性替换重同步），元素属性变化推 `CollectionPatch{ Action=ElementSet, ElementInstanceId, ElementProperty, ElementValue }`（.NET 原地改元素属性也推；元素级写回后 `BroadcastElementUpdate` 跨窗口广播，排除源窗口）。**元素 id 在线缆的来源**：生成器给每个模型消息合成框架保留 `modelInstanceId`（int64，字段号=数据字段数+1）——完整快照路径经它；差量补丁/整列 update 的 ModelValue 序数路径经 `ToModelValue` 对 WebWindowModel 统一注入 name 键 `_modelInstanceId`。前端收敛元素时抽出为**不可枚举** `_modelInstanceId`（同根模型模式，不进 `Object.keys` watch 循环）。**durable 约定：modelInstanceId 永不进 update 消息/TS 镜像/`__repeatedFields`**（生成器过滤；id 永不变更），序数契约 `CollectFieldNumbers` 仍是纯数据字段。`WebWindow.OnBackendMessageReceived` 的 ModelSet 按 `ElementProperty` 空否分派整属性/元素级两条路径。**durable 坑一：前端 `applyPatch` 的 ElementSet 分支比较 `patch.elementInstanceId`（int64 解码成 protobufjs Long）与元素 `_modelInstanceId`（JS number）必须先 `normalize()`（→toNumber），直接 `===` 恒 false → 元素级补丁静默不落**（.NET 已推、前端不动，表现同「.NET 改元素属性逐项推送超时」）。**durable 坑二：前端结构回写（push/splice/替换 → 整列 ModelSet）若元素不带 `_modelInstanceId`、.NET 按 Clear+Add 重建 → 元素实例全换新、前端旧 `_modelInstanceId` 失效，之后元素级编辑按 id 找不到元素**——桥 `sendElementList` 给每个元素补 `_modelInstanceId` name 键（`fields: { _modelInstanceId: jsToModelValue(id) }`），生成器 `EmitModelElementCollectionSet` 按 id 合并保实例（命中复用旧实例、未命中才 TryFromModelValue 重建），结构回写后元素 id 稳定、逐元素编辑可持续。
- **typed repeated（List\<已知模型\>）**：生成器产 `repeated SomeModel`（descriptor `"type":"SomeModel"`、TS `SomeModel[]`）。**typed 元素 ModelValue 对象 map 键是真实 int（proto 字段号，声明顺序 1..N）而非属性名**——落在 `ModelValueMap.OrdinalFields`（map<int32,ModelValue>），与 name 键 `fields`（generic object/Dictionary）并存。前端 watch 回写 typed-repeated 用 `{ object: { ordinalFields: { [String(num)]: jsToModelValue(el[fieldName]) } } }`，.NET `ConvertFromModelValue` `foreach (kv in v.OrdinalFields) switch (kv.Key) { case 1: }`（int 键直接数字字面量）。字段号单一来源 `CollectFieldNumbers` → descriptor 与桥两侧无漂移。generic object/Dictionary/未注册 POCO 维持 name 键。前端收敛成命名键（`fullModelEntries` 只收敛根层 typed repeated；元素内嵌套 typed repeated 全量快照可读，但**整列表重推后嵌套成员退化为序数键** `[{ "1": name }]`，模板用容错访问器 `tag.name ?? tag['1']`）。
- **TS 序数键契约构建期烘焙（`__repeatedFields`）**：生成器把 typed-repeated 的「属性名 → { proto 字段号: 元素属性名 }」烘焙成模型镜像类的 `static ['__repeatedFields']`（**字符串字面量键**，声明与访问两侧都用 `['...']`），桥 `bindModel` 直接读 `constructor['__repeatedFields']` 建 typedElemFields。**durable 大坑：不要用运行时 `constructor.name` → `lookupType` 反射取元素字段表**——Release minified bundle（vite 8 / rolldown 内核压缩器）会把 class 绑定名改名（`class TodoListModel` → `g=class{...}`），`constructor.name` 失真、lookupType 落空，typed-repeated **补丁**元素退化成序数键 `{"1":"t3"}`（快照路径走 resolvedType name 键、不受影响 → 表现为「快照过、.NET Add 后挂」，E2E 20s 超时）。字符串字面量 minifier 永不改写，故协议契约一律构建期定死为字面量，别依赖任何运行时 JS 标识符反射。
- **ObservableDictionary 原地自动推送**：.NET 侧原地改（dict[k]=v / Add / Remove / Clear）抛 CollectionChanged → 框架**整属性重推**（name 键对象 map，非 typed → 不收敛序数键），前端对象整体替换。前端原地改经深 watch 整字典 name 键回写 .NET（`TryConvertObject` 的 `ObservableDictionary<,>` 分支重建同类实例）。**durable 大坑：readonly struct 字段 + 防御性复制 → 死循环**——`DictionaryEntryEnumerator._inner` 若 `readonly`，`MoveNext()` 每次 true、枚举字典无限循环（--blame-hang 拿 .dmp 定位）；须去 readonly。非泛型 `IDictionary` 的 `GetEnumerator()` 返回 `IDictionaryEnumerator`（不能 yield，`Reset()` 抛 NotSupportedException）。
- **字段初始化器在基类构造之后执行**：基类 ctor 扫描看不到初始集合，集合订阅须在 `BuildSnapshotEnvelope` 首次推送时武装（`ArmCollectionSubscriptions`），属性被替换时切订阅，`_isApplyingRemoteWrite` 期间不推送。
- **单订阅者快路径 + 空订阅者短路**（`PushEnvelope`）：`Count==1` 直接调、免 ToArray；`Count==0` 直接 return。最后订阅者解绑自动 `UnbindCollections()`。

## MVVM 命令（[RelayCommand] → 前端命令方法）

模型类里 `[RelayCommand]` 方法（CommunityToolkit.Mvvm）源生成 ICommand 属性 `{方法名}Command`。链路：
- **协议加 invoke**：`ModelInvoke{ commandId, value }`（commandId=命令序号、value=ModelValue 可空）挂 WebMessage oneof。
- **生成器** `CollectCommands` 收集 `[RelayCommand]` 方法，TS 镜像继承桥的 `ModelCommandHost` 基类 + 产出命令方法（无参 / 带参），wire 发命令序号（`[RelayCommand]` 声明序 0 起，`.NET` switch 同序）。
- **桥** `ModelCommandHost` 只承载 `protected _commandChannel?` 类型契约；`bindModel` 对命令模型用 `Object.defineProperty` 注入**不可枚举** `_commandChannel`（不污染 `Object.keys` 响应式 watch 循环）。无命令的模型不继承、桥不注入。
- **.NET** `WebWindowModel.TryInvokeCommand(commandId, value)`：按「命令名+Command」属性找 ICommand，参数类型取 **`RelayCommand<T>` 泛型参数**（有参命令），`CanExecute` 门控**拒绝执行**、`Execute(arg)`。
- **事件出口**：命令方法要驱动窗口/宿主时用**公开事件**（`public event Action<string>? OpenRequested`）——事件非 [ObservableProperty] 字段、不进快照，宿主订阅开窗。
- 命令方法里的属性变化照常走增量推送（Invoke 不在 `_isApplyingRemoteWrite` 抑制内）。

## 写回源生成器（WebWindowUI.Generator.SourceGen）

netstandard2.0、`IsRoslynComponent=true`，**两个 IIncrementalGenerator**（都内存产出、都 partial，合并进同一类型）：

- **WriteBackGenerator** — 每个 `WebWindowModel` 子类产 `{Model}.WriteBack.g.cs`（5 个成员）：`TrySetGeneratedProperty`/`TryGetGeneratedProperty`（switch(name)）、`TryInvokeGeneratedCommand`（CanExecute 门控，无参命令按 `typeof(object)`）、`SubscribeGeneratedCollections`、`ConvertFromModelValue`/`ConvertToModelValue` + `[ModuleInitializer] __WWUI_RegisterPocoConverter`（注册进 `_pocoConverters`/`_pocoSerializers`，**反射兜底已移除**）。
- **ProtoGenerator** — 产 `{Model}Proto.g.cs`（原 console C# 部分改造，逻辑本体 `ModelProtoGenerator` namespace 保持 `WebWindowUI.Generator`，console 与测试经普通引用调用，命名空间不变零改名）。console 瘦身只写 descriptor/TS（`--model/--json-out/--ts-out-dir/--all-models/--root-namespace`，`--cs-out` 已删）。
- **序数键**：POCO 序列化/反序列化按 proto 字段号 int 键（非属性名），`PropInfo.Number`=0（解析失败）跳过。序列化器用 `m.Props`（全可读属性），反序列化器只用 WritableProps（未知序数键跳过）。`ModelValueMap` 加 `[ProtoMember(2)] OrdinalFields(Dictionary<int,ModelValue>)`。
- **durable 坑**：
  - 生成器跑在**未加生成源码的初始编译**上，属性名只能从 `[ObservableProperty]` **字段符号**推（剥**一个**前导 `_` 再 PascalCase，`__name→_Name`），命令属性名从 `[RelayCommand]` **方法符号**推。
  - 生成的 partial 是**派生类**：基类 `EnsureCollectionSubscribed` 必须 `private`→`protected`；生成代码只经 `ApplyRemoteWrite(Action setter)`（抑制回声）/`EnsureCollectionSubscribed` 间接碰私有成员。
  - POCO 注册用 `[ModuleInitializer]` 而非 static ctor（static ctor 可能还没跑）；`delegate bool PocoConvertFunc(ModelValueMap, out object?)`（`Func` 无法区分「失败」与「null POCO」）。
  - netstandard2.0 API 缺口：无 `IReadOnlySet<T>`（用 `IReadOnlyCollection`）、无 `ToHashSet()`（用 `new HashSet<T>(...)`）、无单字符 `string.Split(char, StringSplitOptions)`（用 `Split(new[]{'/'}, ...)`）。
- **增量重构（#6）**：WriteBack 按模型注册输出（`RegisterSourceOutput` 替代 `Collect`，单模型变化只重产该模型）；Proto 解析一次 + 值相等上下文（`EquatableArray` 的「类名→命名空间」表——改其它模型字段时 Roslyn 短路、`models.Combine(allNamespaces)` 独立缓存，typed-repeated 检测只依赖命名空间表）。`EquatableArray<T>` 提升为顶层 internal。
- **测试**：`CSharpGeneratorDriver` + `.AsSourceGenerator()`；`parseOptions` 必须与输入树一致（默认 Latest、输入用 Preview 抛不一致）；「无输出」断言不能 `Assert.Empty(run)`（Proto 对无 [ObservableProperty] 的 EmptyModel 也产 Proto.g.cs）——按 hint 名断言 WriteBack 缺席。`-p:EmitCompilerGeneratedFiles=true` 才落盘检查。
- **WebView2 E2E 全挂的诊断坑**：全部 `WaitBridgeReadyAsync` 超时 → 先查测试 bin 的 wwwroot 是否为空目录（`.frontend-stamp` 在但 vite 产物缺失时 MSBuild 误判 up-to-date 跳过 vite）；修复：删 `app bin\...\wwwroot` 重建。
- **`using WebWindowUI.Sample;` 不能当冗余删**：测试文件在 `namespace WebWindowUI.Tests`，C# 命名空间解析只走当前+外层——`WebWindowUI.Sample` 是 `WebWindowUI` 的**兄弟**子命名空间，不在链上。`using WebWindowUI;` 才是真冗余。子命名空间不反向解析：用到 `WebWindowUI.Sample.Items` 类型须加 `using WebWindowUI.Sample.Items;`（外层不含它），模型 doc 注释 `<see cref>` 指外层类型也要全限定（CS1574）。
- **残留 TS 剪枝由 console 生成器精确做**（`PruneStaleTs`，按「类名 → 期望子路径」，`--all-models` 缺失跳过）；targets 的 `_WWUI_CleanBridgeOutputs` 只剪平铺 bridge JSON。只剪孤儿、不整删（保幂等写 mtime）。
- **供给方式（包模式 vs 仓库模式）**：包模式经 `analyzers/dotnet/cs/`（Backend 包内嵌）沿包依赖图传给引用链上所有模型库；仓库模式由根 `Directory.Build.targets` 对「ProjectReference 到 WebWindowUI.Backend」的模型库注入 `@(Analyzer)`（见三工程结构）。两者都等价于「引用标记即得生成器」，别再给模型库写自己的 SourceGen 引用。

## NuGet 打包（入口聚合，全平台十个包；Windows 上产 8 个）

依赖单向无环：`WebWindowUI.Platforms.*` → {`WebWindowUI.Core` →（Mvvm/protobuf）, `WebWindowUI.Natives.Windows`（Win32 共享层，仅 Windows/CEF 平台）}，`WebWindowUI`（入口）→ {`WebWindowUI.Core`, `WebWindowUI.Platforms.*`}，标记包 → `WebWindowUI`。

- **`WebWindowUI`（入口包，聚合 + 平台引导）**：唯一源码 `Platform.cs`（`Platform.EnsureRegistered`，AOT 安全 `#if` 静态引用触发平台加载）。ProjectReference `WebWindowUI.Core` + 按 `$(WWUIPlatform)` 条件 PackageReference 平台包（**仓库模式 `WWUI_PlatformRef != 'true'` 时改 ProjectReference 相邻平台工程**——`Platform.cs` 的 `#if` 引用需编译期拿到平台类型）+ `build/WebWindowUI.targets` **和 `buildTransitive/WebWindowUI.targets` 各一份**（`PackagePath="build\;buildTransitive\"`）+ 平台选择 props（`WebWindowUI.Platform.props` 打成 `build\WebWindowUI.props` **和 `buildTransitive\WebWindowUI.props`**——NuGet 约定「包名.props」自动导入，包模式消费方自动得 WWUIPlatform/TFM/平台符号）+ `tools/net10.0/` 模型生成器。nuspec 依赖 = Core + 平台包 → 消费方只引本包即自动带回全部。**必须 buildTransitive 也打**：NuGet 只对直接引用者自动导入 build/ 的 targets，透过标记包引用的模型库只吃到 buildTransitive/。
- **`WebWindowUI.Core`（运行时代码本体）**：WebWindow / WebWindowModel / protobuf 协议 / WebResourceResolver / WebWindowPlatform 运行时调度。依赖仅 CommunityToolkit.Mvvm + protobuf-net，**无任何平台依赖**（平台包反引它）。入口包聚合它，消费方经入口间接得到，不直接引用。
- **`WebWindowUI.Platforms.{Windows,Linux,MacOS,Cef}`（平台实现包）**：各自带自身依赖（Windows=Microsoft.Web.WebView2，Linux=GirCore.GLib-2.0，MacOS=无托管额外依赖，Cef=CefGlue.Next.Core + SharpCompress）。ProjectReference `WebWindowUI.Core` 且 `PrivateAssets="all"` → 平台包 nuspec 不声明 Core 依赖（入口已带 Core），也因 Core 无平台引用而无环。入口只引入与构建/运行 OS 匹配的一个（`$(WWUIPlatform)` 条件）。
- **`WebWindowUI.Natives.Windows`（Win32 共享层包）**：裸 Win32 P/Invoke（常量/结构/WndProcDelegate/隐藏消息窗口/MessageLoop），Windows 与 CEF 平台共用，纯 native 无 Core 依赖。平台包**普通** ProjectReference（非 `PrivateAssets=all`）→ nuspec 声明依赖（入口包不带 Natives，消费方必须经平台包还原到本程序集）。平台经**公开** API 消费本层：`Win32MessageLoop : IMessageLoop`（`InitMessageLoop`/`RunOnUiThread`/`IsUiThread`/`MessageLoop`，封装隐藏消息窗口的 WM_RUN 调度 + SC + UiThreadId）与 `INativeWindow`（`Win32NativeWindow`，封装 HWND 生命周期：Show/Hide/Close/Activate/SetTitle/SetIcon/GetSize/`WindowHandle`）。**`Win32`/`MessageLoopSynchronizationContext` 是 internal**——平台不得直接引用，一律走 `Win32MessageLoop.RunOnUiThread`（拆分 native 后 CEF 曾残留直引 internal 类型 + 已删除的 `_hwnd` 字段致 CS0122/CS0103，已迁移到公开 API；`CefWindow` 用 `_nativeWindow.WindowHandle`/`GetSize()`/`Close()`）。
- **`WebWindowUI.Backend` / `WebWindowUI.Frontend`（角色标记包）**：各一个空标记类，依赖入口包 `WebWindowUI`。一条 `PackageReference` 即带回 targets+生成器+Core+平台。Backend 包额外内嵌写回/Proto 源生成器到 `analyzers/dotnet/cs/`。`WebWindowUI.Generator.SourceGen` 不产独立包（`IsPackable=false`，分析器内嵌进 Backend）。
- **`WebWindowUI.Templates`**（`PackageType=Template`）：`content/` 三子工程骨架，`sourceName=WebWindowUI.Sample`，`preferNameDirectory: true`，`primaryOutputs=[WebWindowUI.Sample.csproj]`。**不可加 restore postActions**（多 csproj 模板 `B17581D1` 每次生成都报「无法确定哪个项目文件要添加引用」）。**模板 slnx 坑**：`<Folder Name="/">` 根文件夹抛 MSB4025，模板骨架用裸 `<Project Path=.../>` 平铺。
- **CPM 集中版本**：`Directory.Packages.props`（`ManagePackageVersionsCentrally=true`）。所有 PackageReference 裸写（无 Version）。**PackageVersion 不许浮动版本**（`3.2.*` 抛 NU1011，钉死 protobuf-net 3.2.56 / System.Text.Json 8.0.6）。`WebWindowUI.Platforms.{Windows,Linux,MacOS,Cef}` 也进根 CPM（入口的版本化 PackageReference 需要）。
- **打包纪律（三步，Natives → 平台包 → 入口）**：
  1. `dotnet pack Natives/WebWindowUI.Natives.Windows -c Release -o artifacts/`（Win32 共享层，无依赖；平台包 nuspec 声明它，须先就绪供后续 restore 解析——`dotnet pack Platforms.Windows` 只声明依赖不产本包）。
  2. `dotnet pack WebWindowUI.Platforms.Windows -c Release -o artifacts/`（先打平台包：它只构建 Core+Natives，均无平台依赖 → 全新环境无死锁；Linux/MacOS 平台包不能在 Windows 上交叉打，须在各自 OS 上补打）。
  3. `dotnet pack WebWindowUI.slnx -c Release -o artifacts/ -p:WWUI_PlatformRef=true`（`WWUI_PlatformRef` 门控入口的平台 PackageReference：默认不传则入口不需要平台包、仓库内构建/测试零 artifacts 依赖；只有打框架时才引入平台依赖。平台包与 Natives 已在第 1/2 步就绪 → restore 可解析）。
  **重复打包同一版本必须清缓存** `dotnet nuget locals global-packages --clear`（或删 `%USERPROFILE%\.nuget\packages\webwindowui*`）——同版本号不改缓存，消费方会恢复旧 dll 编译出 CS0115。
- **durable 坑：不能对入口的平台 PackageReference 加 `ExcludeRestorePackageImports!=true` 门控**（早期尝试）——restore 图遍历**连被还原项目自身**也用 `ExcludeRestorePackageImports=true` 重新求值、其 assets 反映该遍历求值（NuGet.targets `_GenerateRestoreGraphProjectEntryInputProperties`），加了会把入口自己的平台依赖也抑制掉（assets deps 空、nuspec 丢平台依赖）。Core 拆分后平台工程不引用入口、无任何环路径，也不需要靠它挡循环。
- 本地源见仓库根 `NuGet.config`（artifacts 目录）。**durable 坑：artifacts 目录缺失 → NuGet 报 NU1301（本地源不存在）硬失败，即使所需包已在全局缓存也照挂**。缓解（都实测过）：`dotnet restore -p:RestoreIgnoreFailedSources=true`（全局属性）才把 NU1301 降成 NU1101；`NuGet.config <config>` 同名键无效、`Directory.Build.props` 属性只在单工程 restore 生效、**slnx 级 restore 不认**。治本方案即现状：Demo 从 `WebWindowUI.slnx` 移除——主解决方案构建不依赖 artifacts，Demo 各自经 `Demos/<Demo>/<Demo>.slnx` 单独构建（须先 pack）。

## 平台拆分（Windows=WebView2 / Linux=WebKit2GTK（GTK3）/ macOS=WKWebView）

- **包结构（Core + 入口 + 平台包，依赖单向无环）**：`WebWindowUI.Core`（运行时代码本体，平台无关，**不引用任何平台工程/包**）→ `WebWindowUI.Platforms.{Windows,Linux,MacOS}`（平台实现，ProjectReference Core 且 `PrivateAssets=all`）→ `WebWindowUI`（**入口包，聚合 + 平台引导**，ProjectReference Core + 按 WWUIPlatform 条件 PackageReference 平台包 + 唯一源码 `Platform.cs`）。消费方只引 `WebWindowUI`：nuspec 依赖 Core + 平台包 → 自动带回平台实现及其自身依赖（WebView2/GirCore）。**平台包依赖 Core 而非入口 → 打包无鸡生蛋**（打平台包只构建 Core，Core 无平台依赖，无需平台包先存在；打入口才需要平台包先就绪，pack 顺序见「NuGet 打包」）。
- **运行时调度（AOT 安全，无 Assembly.Load）**：`WebWindowPlatform`（在 Core）是**纯注册表**——`Current` 返回已注册实现，平台程序集 `[ModuleInitializer]`（`PlatformRegistration.cs`，库场景预期用法故 NoWarn CA2255）调 `internal Register` 写入自身静态字段（无静态字段初始化器 → 无 cctor，无类型初始化死锁）。**加载触发由入口包 `Platform.EnsureRegistered()`（`Platform.cs`）承担**：编译期 `#if WINDOWS/LINUX/MACOS` 静态引用平台类型（`GC.KeepAlive(typeof(...))`，无反射）——JIT 下 `typeof` 解析强制加载平台程序集、模块初始化器在触发线程注册；NativeAOT 下类型被静态链接、模块初始化器启动时按依赖序执行。**消费方 Program.cs 的 Main 首行必须调一次 `WebWindowUI.Platform.EnsureRegistered()`**（平台无关；这是唯一一处消费方改动，入口程序集在 JIT 下懒加载、模块初始化器不会自动跑，Core 保持平台无关就必然要求 Core 之上的显式启动）。`WebWindow` 构造时调 `WebWindowPlatform.Current`。
- **仓库模式（构建/测试 slnx，不传 WWUI_PlatformRef）**：入口的平台 PackageReference 被门控关闭，改由入口工程 `WebWindowUI.csproj` 按 WWUIPlatform 条件 ProjectReference 相邻平台工程（`Exists` 判定），经引用图传递进消费方产物（应用引用入口 → 平台 dll 进 bin）；测试工程另有自己的直接条件引用（`WebWindowUI.Tests.Platform.csproj`）。包模式（`WWUI_PlatformRef=true` 打包）则完全走 nuspec 依赖。
- `WebWindowUI.Platform.props` 集中平台选择：`$(WWUIPlatform)`（Windows/Linux/MacOS，默认按宿主 OS）驱动 TFM（net10.0-windows / net10.0 / net10.0-macos）与 `WINDOWS/LINUX/MACOS` DefineConstants。**所有条件键控 WWUIPlatform 而非裸 `IsOSPlatform`**（否则 `-p:WWUIPlatform=Linux` 在 Windows 上双定义符号）。标记库 TFM 必须镜像核心。targets 里的 npm/vite 命令用 `IsOSPlatform`（跟构建宿主走）。App 工程 OutputType **配置键控**（Debug=Exe 控制台看日志 / Release=WinExe 无控制台；WinExe 的 WindowsSubsystem 仅 Windows 生效，Linux/macOS 上等效 Exe）。桥 JS `resolveChannel()` 自适应 `chrome.webview`（Windows）→ `window.webkit.messageHandlers.wwui`（WebKit）。
- **平台选择自动分发（消费方零手写 Import，同 targets 机制）**：三条导入路径，`_WWUI_PlatformPropsLoaded` 守卫保证幂等（先导入者生效，其余跳过）：① **仓库模式**根 `Directory.Build.props` Import `$(MSBuildThisFileDirectory)WebWindowUI.Platform.props`（重组织后位于仓库根，仓库内所有工程含 Sample/Demo/框架/Tests）；② **包模式**入口包把它打包成 `build\WebWindowUI.props` + `buildTransitive\WebWindowUI.props`（NuGet 约定「包名.props」自动导入，透过后端/前端标记包间接引用只吃 buildTransitive/）；③ **模板**`content/Directory.Build.props`（生成后即工程根）导入随模板生成的本地 `WebWindowUI.Platform.props` 副本——**必须本地**：生成工程首个 restore 时 .nuget.g.props 尚不存在、包 props 还没被导入，TFM 只能来自本地文件。Platforms.*/Generator/SourceGen 的 csproj 正文显式 TFM 覆盖根 props 默认，行为不变（只多一个无害的平台编译符号）。
- **Linux = WebKit2GTK 4.1（GTK3 端口）**，不是 WebKitGTK 6.0/GTK4。GirCore 只发布 WebKitGTK 6.0（GTK4）与 GLib 绑定，无 GTK3/WebKit2 4.1 托管绑定，故窗口壳 + WebKit 绑定全部手写 P/Invoke（`WebWindowUI.Platforms.Linux/`：`WebKit2Native.cs`（libwebkit2gtk-4.1.so.0 + libjavascriptcoregtk-4.1.so.0 + gobject/glib/gio + **libsoup 构造 scheme 响应头**，soup2/soup3 按 WebKitGTK 实际链接版本运行时探测，见「Linux scheme 响应」+ `GtkNative.cs`/`GtkWindowHost.cs`（libgtk-3.so.0））。Linux 只引 `GirCore.GLib-2.0`（消息循环，纯托管，运行时才加载原生库 → 支持 Windows 上编译检查）。运行前提 Ubuntu `libwebkit2gtk-4.1-0`（其依赖自带对应 libsoup，无需单独装）。
- **GirCore 信号坑**：事件用 `+=` 订阅（不是 `add_OnXxx`，合成 add 访问器 CS0571）。delegate：`GObject.SignalHandler<T>` = `void(T sender, EventArgs args)`。文件头 `#pragma warning disable CA1416`。歧义：`using Action = System.Action;`（Gio.Action）与 `using Exception = System.Exception;`（JavaScriptCore.Exception）。
- **MSBuild 条件坑**：嵌套单引号 `'$([MSBuild]::IsOSPlatform('Windows'))' == 'true'` → MSB4092。裸函数调用 `Condition="$([MSBuild]::IsOSPlatform('Windows'))"`，取反 `!$([MSBuild]::IsOSPlatform('Windows'))`。
- **macOS = 盲写**（Windows 编译不了）：NSWindow + WKWebView + 四个 `[Export]` NSObject 子类。绑定事实：`NSDictionary.FromObjectsAndKeys(objects, keys, count)` 对象在前、`NSHttpUrlResponse(NSUrl, nint, string, NSDictionary?)`、`NSData.FromArray(byte[])`、`EvaluateJavaScriptAsync(string)`→`Task<NSObject>`。需 Mac + `dotnet workload install macos` 验证。
- 平台限制：Linux/macOS `SetIcon` no-op、ExecuteScriptAsync JSON best-effort。**Linux scheme 响应补 ACAO:* 与 Cache-Control 头**（镜像 Windows 的 ResourceHeaders：hash 资产长缓存、其余 no-store；404 回 no-store）：WebKitGTK 旧 `webkit_uri_scheme_request_finish` 只能带 content-type 设不了响应头，须走 `WebKitURISchemeResponse`（≥2.36：`response_new`/`set_content_type`/`set_http_headers`/`finish_with_response`）+ `webkit_security_manager_register_uri_scheme_as_cors_enabled`（自定义 scheme 默认不开跨源，不注册则 fetch 被 CORS 门控拦截）+ `webkit_security_manager_register_uri_scheme_as_secure`（镜像 Windows 的 TreatAsSecure，页面按 https 安全上下文求值）+ libsoup `SoupMessageHeaders`。**durable 坑一（真正的崩溃源）：`set_http_headers` 是 `(transfer full)`**——GUniquePtr 接管 headers 所有权、不 ref，调用方传完绝不能再 unref/free（旧实现这么做 → response 持有的指针被提前释放，WebKit 异步读回调迭代它 + 析构再释放一次 → **double-free/UAF 段错误，且旧 `finish` 不碰 headers 所以「换旧 API 就正常」**）。headers 交出去即归 WebKit 释放。**durable 坑二：headers 必须与 WebKitGTK 自身链接的 libsoup 同版**——webkit2gtk-4.1 的 libsoup 依赖随发行版而异（WebKitGTK < 2.42 → libsoup-2.4.so.1（soup2）；≥ 2.42 → libsoup-3.0.so.0（soup3）），soup2/soup3 的 `SoupMessageHeaders` 内部布局不兼容、释放函数不同（unref vs free），headers 最终由 WebKit 侧按它链接的 soup 释放，构造版本错配即崩溃。`WebKit2Native` 于 `Initialize` 扫 `/proc/self/maps` 探测（libwebkit2gtk 加载后其 DT_NEEDED 依赖已映射）用同版 API 构造，两套 LibraryImport 都惰性加载只调被选中的那个；stream/response 则各 ref 一次、调用方 unref 自己的引用。

## 单文件发布（PublishSingleFile）

- `PublishSingleFile` + `SelfContained` 只管托管侧（DLL/运行时全打进 exe）；**WPF 原生 DLL（D3DCompiler_47_cor3 / PresentationNative_cor3 / wpfgfx_cor3 / vcruntime140_cor3 / PenImc_cor3）+ WebView2Loader 必须 `IncludeNativeLibrariesForSelfExtract=true` 才内嵌**；`IncludeAllContentForSelfExtract=true` 收内容文件；`EnableCompressionInSingleFile=true` 把 133MB 压到 ~65MB。
- **PDB 内嵌**：不要 `DebugType=none`（发布版丢符号），也不要靠 pubxml 删 .pdb——统一在仓库根 `Directory.Build.props` 设 `DebugType=embedded`（Release 条件，所有工程含库继承），符号随程序集打进 exe，无独立 .pdb 文件。**每个 csproj 也显式写 per-config DebugType**（Debug=portable / Release=embedded）：模板生成工程不继承根 props，Release 单文件发布会散 .pdb，靠 csproj 显式写保证（仓库工程与根 props 重复但无害、自包含）。
- **单文件下 WebResourceResolver 磁盘回退失效**（BaseDirectory 无 dll），内嵌 wwwroot 靠「已加载程序集」扫描命中。发布 exe 启动即建 `.exe.WebView2` 用户数据目录属正常（跑过就会留，清理发布目录时注意）。

## Demo 应用

`Demos/` 下四个**有功能的真实 Demo**（包模式生成 = 包模式端到端验证样本；**不在**仓库根 `WebWindowUI.slnx` 里——各自 `Demos/<Demo>/<Demo>.slnx`，先 `dotnet pack` 再单独构建，主解决方案构建不依赖 artifacts 本地源）：

- **`WebWindowUI.Demo.Todo`（待办）**：TodoItemModel + TodoListModel（get-only ObservableCollection Items + NewTitle/Status + 命令 AddTitle/Toggle/Remove/ClearCompleted，持久化 `%LocalAppData%\...\todos.json` 用私有 DTO 规避序列化基类状态）。单窗口 main。
- **`WebWindowUI.Demo.SharedNotes`（双屏共享便签）**：NotesModel，App 用**同一个实例**开 main 编辑窗 + monitor 只读墙 → 任一窗口操作全广播、其余实时跟随（多订阅者 + 远程回写排除源）。
- **`WebWindowUI.Demo.Monitor`（系统监控）**：嵌套模型 MonitorSettingsModel + MonitorModel（采样 Timer 线程池线程跨线程推送）；设置窗口绑 `model.Settings` 同一子实例（master-detail，改 PollIntervalMs 主窗口订阅重建定时器立即生效）。主窗口展示嵌套 settings 用序数键翻译（`{ "1": pollIntervalMs, ... }`）。
- **`WebWindowUI.Demo.ImageGallery`（图片画廊，2026-08）**：byte[] 在 typed repeated 元素里下发图片 + 双模式上传 + 列表查看。条目 `ImageItemModel` 带 `byte[]? Data`（生成器映射 bytes→Uint8Array）+ `string Path`（存储完整路径，卡片/lightbox 灰字展示）。存储 `%LocalAppData%\WebWindowUI.Demo.ImageGallery\images`。
  - **双模式上传（两个按钮）**：共用 `UploadFile` DTO + 共用 `StoreBytes` 落盘。`UploadBytes`（字节）：前端 `<input type="file">` 读成 byte[] 回传 `{ name, data, path }`（path 是 WebView2 非标准 File.path）；`PickFile`（路径）：前端点按钮 → 后端 `#if WINDOWS` 弹系统原生 `Microsoft.Win32.OpenFileDialog`（WPF）→ 自读源文件拷入存储目录，前端不再发 `{name,path}`。Backend 加 `<UseWPF Condition="'$(WWUIPlatform)' == 'Windows'">true</UseWPF>`。
  - **坑：UseWPF 后 SDK 桌面隐式 using 不含 `System.IO`/`System.Net.Http`**——模型里 `File`/`Path`/`FileInfo` 报 CS0246，须显式 `using System.IO;`。命令在 WebView2 UI 线程（STA）执行，`ShowDialog()` 弹模态对话框安全。
  - 命令参数对象 `{ name, data, path }` → 生成代码 `ModelProtocol.TryFromModelValue(value, typeof(UploadFile))` 走**反射路径**重建（关键约束：DTO 须参数化 ctor + 可写属性名与 camelCase 前端键忽略大小写匹配；`_pocoConverters` 只有 WebWindowModel 才注册）。TS 的 `new Blob([bytes])` 在 TS5.7+ 报 `Uint8Array<ArrayBufferLike>` 不满足 `BlobPart`——须 `new Blob([new Uint8Array(bytes)])` 拷贝。前端 WeakMap<item, blob URL> 缓存渲染（不把 url 存进条目对象，防深 watch 序列化回 .NET）。

## 验证纪律

- **跑完示例应用必须关掉**：`taskkill //F //IM <App>.exe`，等待后确认无残留。进程会锁定 bin 下输出文件导致后续构建 MSB3027；测试遗留进程干扰下一次观察。**绝不要结束 Windows SearchHost 的 msedgewebview2.exe 进程**。
- Git Bash 的 taskkill 会把 `/IM` 当路径 → 须 `cmd //c "taskkill /IM xxx.exe /F"` 包。
- `tasklist` 把镜像名截到 25 字符（`WebWindowUI.Demo.Monito`），别 grep 全名，用前缀 grep。
- 应用进程由 bash `&` 启动时，其宿主 shell 一结束进程就死——启动验证用 Start-Process 或保持 shell。
- **测试拆成两个工程**：`Tests/WebWindowUI.Tests.Protocol/`（协议/单元：模型、生成器、协议、resolver、BuildHomeUrl——纯逻辑，**不直接引用平台工程/Sample 应用**（平台 dll 仍经 Sample.Backend→标记包→入口链传递进 bin，与拆分前一致，但测试不碰 WebView2/WebKit 运行时）、bin 无真实 wwwroot 资源，Linux/macOS 上也能构建）+ `Tests/WebWindowUI.Tests.Platform/`（平台 E2E：WebView2/WebKit 桥测试 + `Support/` 泵，#if 门控，引 Sample 应用拿 wwwroot 传递复制 + 条件平台引用）。Core 对**两**测试工程 `InternalsVisibleTo`（internal 如 `BuildSnapshotEnvelope`/`ExecuteScriptAsync`），平台包只对平台测试工程；协议工程不含 `Microsoft.CodeAnalysis.CSharp` 之外的平台依赖。slnx `/Tests/` 文件夹挂两工程——**此前 slnx 误写单数 `Test/WebWindowUI.Tests/` 路径致 MSB3202**，已随拆分修复。
- **平台 E2E STA 泵（durable）**：`Support/StaThreadPump.cs` 的泵线程初始化顺序必须是「先 `Win32.GetOrCreateMarshalWindow` 建隐藏消息窗口 → 绑定 SC → **在本线程**加载平台程序集触发 `[ModuleInitializer]` 注册」。**GetOrCreateMarshalWindow 是进程单例，谁先创建谁拥有消息队列**——若平台注册发生在别的线程（早期实现有个 MTA 注册线程，module init 在它上面建 marshal 窗口），WM_RUN 全落进那个无泵线程的队列、async 延续永不派发 → 测试全挂 await（`WaitBridgeReadyAsync` 超时、env/controller 创建异步卡死）。泵线程还必须吞掉 WM_QUIT（`MsgWaitForMultipleObjectsEx` + 200ms 兜底），否则最后一个窗口关闭时泵退出。
- 仓库级回归：`dotnet build WebWindowUI.slnx -c Debug/-c Release`（0 错，MSB3277 WebView2 WindowsBase 无害警告）+ `dotnet test WebWindowUI.slnx -c Debug`（124：协议 105 + 平台 19，含元素级双向 E2E：`TodoList_TypedList_Bidirectional` / `TodoList_SharedModel_ElementEdit_BroadcastsToOtherWindow` / `NestedListItemWindow_Tags_TypedRepeated_Bidirectional`）。**Demo 不在主 slnx**（见 NuGet 打包一节），包模式回归各 Demo 经自身 `Demos/<Demo>/<Demo>.slnx` 单独验证。
- 前端调试：桥改动要**物理拷进** `node_modules/webwindowui-bridge`（npm link 符号链接被 rolldown 解析到真实路径、无依赖报 `Failed to resolve import "protobufjs"`）+ touch `vite.config.ts` 强制 vite 重建，再 grep bundle 验证。

## 样例（Sample/，2026-08-09 从仓库根 WebWindowUI.Sample/ 改名）

三工程同构（命名空间 `WebWindowUI.Sample` 不变，文件系统路径引用它的代码全断——测试 helper 的仓库根标记、`Driver.RunOnSampleModels` 的 backendDir 等硬编码路径须跟着改；残留空壳目录可直接删）。样例是**每窗口一功能**：main（双向绑定）/ todos（List\<Model\> 一一对应）/ resources（app:// 资源 + appbin:// 数据通道）/ multi（共享/独立模型）/ nested（单模型嵌套 + 子窗口 master-detail）/ nested-list（列表元素嵌套 + 元素内再嵌套 tags/meta）/ settings / about。launcher 入口按需开窗（`LauncherModel.request` 回写 + `Task.Run` 延迟清空——同步清空落在回声抑制窗口内 null 推不回前端、同按钮二次点击失效）。`bindXxx()` 绑定助手在模型 TS 镜像末尾（封装 bindModel + descriptor import）。

## 桥协议（descriptor 自包含）

生成器把 9 个基础信封消息（WebMessage 信封 + ModelValue/ModelValueList/ModelValueMap + ModelReady/ModelUpdate/ModelSet/ModelSnapshot/GeneratedModel + ModelInvoke + CollectionPatch）**内联进每个模型 descriptor**。桥 `bindModel(model, generatedJson)` 的 `generatedJson` 必填，`Root.fromJSON` 直接解析。信封字段：`ModelUpdate`/`GeneratedModel` = `{ modelId: int32, payload: bytes }`（无 messageName）、`ModelInvoke` = `{ commandId: int32, value }`（无 command 字符串）。漂移测试 `BaseEnvelope_InlineInEveryDescriptor_MatchesCompiledDto` 锁 descriptor ↔ `ModelProtocol.cs` `[ProtoMember]`。`npm install` 必须在 `<App>.Frontend/` 跑（依赖和 vite 二进制在该层 node_modules）。

- **实例级唯一 ID（modelInstanceId）**：`WebMessage` 信封层字段 8（int64，不进 oneof payload）——.NET 侧 `WebWindowModel.ModelInstanceId`（静态计数器 `Interlocked.Increment` 进程内单调自增，每实例唯一有序）。**所有出站信封**（full/snapshot/update/patch）自动携带（`BuildGenericSnapshot` 反射循环显式排除该元数据属性）；桥从首个 full/snapshot 捕获并暴露为**非枚举** `model._modelInstanceId`（仿 `_commandChannel`，不进 `Object.keys` watch 循环），ready/set/invoke 回传同字段。**双端「0 容忍」防串守卫**：.NET `WebWindow.OnBackendMessageReceived` 与桥 `onMessage` 对非 full/snapshot 消息校验 `modelInstanceId`，窗口换绑后旧实例在途消息丢弃（0 = 旧桥/首握手未携带，容忍不守卫）。命名刻意避开 Sample 模型已有的**数据属性** `InstanceId`（SharedNotes 标签「共享实例」、Settings Guid）——框架级字段统一 `modelInstanceId`/`ModelInstanceId`/`_modelInstanceId`。线缆双向兼容：新字段对旧桥是 protobufjs 跳过的未知字段，旧端缺字段按 0 容忍。
