# WebWindowUI.Core

**运行时代码本体（平台无关）**。所有平台实现包（`WebWindowUI.Platforms.*`）反引本包；消费方经入口包 `WebWindowUI` 间接得到，不直接引用。

## 依赖

仅 `CommunityToolkit.Mvvm` + `protobuf-net`。无任何平台依赖——Windows/Linux/macOS/CEF 的平台实现都在各自的平台包里。

## 核心类型

| 类型 | 职责 |
|------|------|
| `WebWindow` | **全抽象窗口契约**（平台窗口继承实现，消费方不继承）：`WebWindowOptions`（窗口路径/标题/无头/尺寸）、`Model` 绑定、13 个窗口状态属性（SystemDecorations/WindowState/Position/Size/MinSize/MaxSize/ShowInTaskbar/CanResize/CanMinimize/CanMaximize/IsDialog/IsActive/Screens）、`Show/ShowDialog/Hide/Close/Activate/SetIcon`、`Loaded/Closed/Closing/Resize/Move/Active/WindowStateChange/SystemDecorationsChange` 事件 |
| `WebWindowModel` | 模型基类：属性/集合变化订阅 → 增量推送；`TrySetProperty` / `TryInvokeCommand` / `BuildSnapshotEnvelope`；`ModelInstanceId`（进程内唯一，帧级寻址） |
| `ModelProtocol` | protobuf 协议：`ModelValue` 值树、`WebMessage` 信封、`ToModelValue/ConvertFromModelValue`、POCO 转换器注册表 |
| `WebResourceResolver` | wwwroot 两来源读取：**先查程序集嵌入资源再回退磁盘**（Debug 磁盘、Release 内嵌），首页地址推导、图标 |
| `WebWindowPlatform` | 平台**注册表**（纯）：`Current` 返回已注册 `IPlatform`（未注册抛 `PlatformNotSupportedException`），`Register(impl)` 首个生效；`Current.CreateWindow(options)` 是建窗唯一入口 |
| `ObservableDictionary<TK,TV>` | 原地增删自动抛 `CollectionChanged` → 框架整属性重推前端的字典 |
| `INativeWindow` | **原生窗口契约（只含窗口平台相关，不含 WebView 内容）**：HWND 生命周期（Show/Hide/Close/Activate/SetTitle/SetIcon/GetSize）+ 窗口状态属性（Position/Size/MinSize/MaxSize/ShowInTaskbar/CanResize/CanMinimize/CanMaximize/IsDialog/IsActive/Screens）+ 事件（Destory/Resize/Move/Active/WindowStateChange/SystemDecorationsChange） |

协议相关见 `Protocol/`（`ModelProtocol.cs` 定义信封，`JsStringLiteral` / `StringCodec` 处理字符串通道）。

## 关键设计

- **数据绑定 / 推送 / 集合**：单属性变化自动推送增量（protobuf 补丁）；`ObservableCollection` 原地增删走差量 `CollectionPatch`；`ObservableDictionary` 原地改整属性重推；`List<已知模型>`（typed repeated）元素级双向（按 `ModelInstanceId` 寻址、原地写保实例）；多窗口绑同一模型实例 = 共享广播、远程回写排除源窗口。
- **MVVM 命令**：`[RelayCommand]` 方法（CommunityToolkit.Mvvm）→ 前端命令方法，`ModelInvoke{ commandId, value }` 走 wire，`CanExecute` 门控拒绝执行。
- **WebResourceResolver 内嵌优先**：内嵌程序集懒发现（单例缓存），Release 下前端 dll 靠 targets 注入的 `FrontendHost`/`FrontendLoad` 宿主标记机制强制加载（`typeof` 静态引用，JIT / NativeAOT 双安全），**全程无 `Assembly.Load`**。
- **接口即契约**：平台包经 `INativeWindow`/`IMessageLoop` 公开消费 Natives 层，本层 `internal` 成员经 `InternalsVisibleTo` 暴露给各平台包与测试工程。

## 用法（消费方视角）

消费方只引 `WebWindowUI`（入口包）即自动带回本包。写一个模型：

```csharp
public partial class MainWindowModel : WebWindowModel
{
    [ObservableProperty]
    public partial string Title { get; set; } = "Hello";
}
```

开窗口：

```csharp
var win = WebWindowPlatform.Current.CreateWindow(new WebWindowOptions("main") { Title = "主窗口" });
win.Model = model;
win.Show();
```
