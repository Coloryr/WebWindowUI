# WebWindowUI.Platforms.Cef

**CEF 平台实现**（与 Windows 平台互斥）：CefGlue 托管包装 + Chromium 渲染内核，承载于裸 Win32 顶层窗口。**浏览器托管层纯公共 API 自建**（`CefBrowserHosting`，不触碰 CefGlue 内部类型，无需上游补丁），宿主控件为隐藏宿主 + 重挂载（对齐 CefGlue.Avalonia）。Windows 上经 `Natives.Windows` 复用 Win32 共享层。

## 依赖

- **CefGlue.Next NuGet 包**（**CEF 150 代** 150.7871.115）：`CefGlue.Next.Core` / `CefGlue.Next.Common` / `CefGlue.Next.Common.Shared` / `CefGlue.Next.BrowserProcess.Core` PackageReference。只用**公共 API**（Core 的 `CefRuntime`/`CefApp`/`CefClient`/`CefBrowserHost` 等 + Common.Shared 的 `CustomScheme` + Common 的 `LoadEndEventArgs` + BrowserProcess.Core 的 `CefSubProcess`），不依赖 `E:\temp_code\CefGlue` 补丁源
- **CEF 150 运行时由包链自动部署**：`CefGlue.Next.Common` 依赖 `chromiumembeddedframework.runtime.win-x64`（150.0.11）→ 其 build props 定义 `CefRedist64CopyResources`，`CefGlue.Next.Common` 的 buildTransitive targets（`CefRedistCopyWindowsResources`）在 Exe 输出时拷贝 libcef.dll + locales + *.pak 等
- Linux/macOS 运行时仍走 NuGet 包：`cef.runtime.linux-{arm64,x64}` / `cef.runtime.osx-{arm64,x64}`
- `WebWindowUI.Core`（`PrivateAssets="all"`）；Windows 上额外普通 ProjectReference `WebWindowUI.Natives.Windows`

## 组成

| 文件 | 内容 |
|------|------|
| `CefPlatform.cs` | `CefPlatform : IPlatform`：`Init(string[] args)` 先 `CefSubProcess.Run(args, true)` 分发子进程（同 exe 模型，应用 Main 只调 `WebWindowUIPlatform.Init(args)`）→ 自建初始化链 `CefRuntime.Load()` + `CefRuntime.Initialize(new CefMainArgs(args), settings, new AppCefApp(schemes), IntPtr.Zero)` + 逐个 `RegisterSchemeHandlerFactory`（对齐上游 `CefRuntimeLoader.InternalInitialize`；`AppCefApp.OnRegisterCustomSchemes` 逐个 `AddCustomScheme`）→ `_message.InitMessageLoop()`。保留 `_browsers` 浏览器 id → 窗口映射、`RunOnCefUiThread`/`PostToCefUiThread`（ActionCefTask）、`RunMessageLoop`、`Dialog`（Win32Dialog）。**`CreateWindow` 经 `_message.RunOnUiThread` marshal 到主线程创建**（Win32 窗口必须由主线程创建，见 durable 坑）。`CefRuntime.Shutdown` 在 `RunMessageLoop` 返回后同线程调用 |
| `CefBrowserHosting.cs` | **浏览器托管（纯公共 API 自建，替代上游内部 `CommonBrowserAdapter`/`IControl`）**：`Create` 建隐藏宿主（`Win32BrowserHost.CreateHiddenHost`）→ `CefWindowInfo.SetAsChild` → `CefBrowserHost.CreateBrowser(windowInfo, client, settings, "", null, null)` 异步创建；`OnAfterCreated` 记录 browser + 重挂载进目标窗口（`Win32BrowserHost.Reparent`）+ 导航初始 URL + 触发 `Initialized`；`OnBeforeClose` 销毁隐藏宿主 + 触发 `BrowserClosed`；`OnLoadEnd` 转发 `LoadEnd`。JS：`ExecuteJavaScript`（主帧）、`EvaluateJavaScript`（`frame.V8Context` `Enter/TryEval`，CEF UI 线程）。关闭 `CloseBrowser`。嵌套 `HostingClient : CefClient` + `LifeSpanHandler`（OnAfterCreated/DoClose→false/OnBeforeClose）+ `LoadHandler`（OnLoadEnd） |
| `CefWindow.cs` | `CefWindow : WebWindow`：构造先断言 `CefRuntime.IsInitialized`（未初始化抛异常——加载由 `CefPlatform.Init` 完成）→ Win32 顶层窗口（`Win32NativeWindow`）→ `CefBrowserHosting`（订阅 `Initialized`/`BrowserClosed`/`LoadEnd`）→ 初始 URL → 尺寸。`BrowserClosed` → **仅主浏览器**（初始化时捕获的 `_mainBrowser`）销毁顶层窗口。窗口状态面经 `INativeWindow` 实现 |
| `AppSchemeHandlerFactory.cs` | `CefSchemeHandlerFactory` + `CefResourceHandler`：GET 服务 wwwroot 资源，POST `__wwui` 解码 JS 回传字节按浏览器 id 分派回窗口 |

## 关键设计

- **纯公共 API 自建托管**：不引 CefGlue 内部类型（`CommonBrowserAdapter`/`IControl`/`MenuEntry`/`CefRuntimeLoader.Load`）——此前直连内部 API 靠上游 `E:\temp_code\CefGlue` 给 Common 加的 `InternalsVisibleTo("WebWindowUI.Platforms.Cef")`，补丁源已删除后编译即 CS0122。`CefBrowserHosting` 复刻了 `CommonBrowserAdapter` 的浏览器创建/事件/JS 语义（参考仓库内 `CefDemo/` 的 SimpleApp 系列——纯公共 API 自建托管且跑通）。
- **隐藏宿主 + 重挂载（对齐 CefGlue.Avalonia，DevTools 行为的关键）**：浏览器先作为隐藏宿主窗口（`Win32BrowserHost.CreateHiddenHost`）子窗口创建；`OnAfterCreated` 时 `Win32BrowserHost.Reparent`（SetParent+MoveWindow）重挂载进可见顶层窗口。浏览器直接作为可见窗口子控件时，`SetAsPopup(GetWindowHandle())` 的 DevTools 弹窗会顶替内容/即开即关。
- **初始化走自建链**：`CefRuntime.Load()` → `CefRuntime.Initialize(new CefMainArgs(args), settings, app, IntPtr.Zero)` → `RegisterSchemeHandlerFactory`。scheme 注册由 `AppCefApp.OnRegisterCustomSchemes`（`AddCustomScheme`）+ 初始化后的 factory 注册共同完成，行为对齐上游 loader。
- **CEF UI 线程独立（MTML=true）**：浏览器创建/生命周期/JS 回调都在 CEF UI 线程（`CefBrowserHosting` 内直接操作，无需 marshal）；`CefWindow` 原生窗口操作走主线程（`Win32MessageLoop.RunOnUiThread`）。
- **DevTools 关闭不影响主窗口**：`BrowserClosed` 对 DevTools 浏览器也触发，`OnBrowserClosed` 用**初始化时捕获的 `_mainBrowser`**（`ReferenceEquals` 过滤）销毁顶层窗口。

## durable 坑

- **DevTools 窗口即开即关/崩溃不是 GPU 问题（2026-08-14 用户明确：不要动 GPU）**：`--disable-gpu` 无效、`Failed to create shared context for virtualization` 是 VM GPU 合成器噪音但非根因。**DevTools 前端（devtools:// 重 JS）在 CEF 150/151 的 V8 间歇 fastfail（0xC0000409）**，与嵌入方式/scheme/GPU 都无关（data: 页面 + --disable-gpu 也照样崩）。**可靠 DevTools = 远程调试**（`--remote-debugging-port` + 外部 Chrome `chrome://inspect`）。
- **launcher 的 STATUS_STACK_BUFFER_OVERRUN = protobufjs 描述符解析 V8 fastfail**：`protobuf.Root.fromJSON(descriptor)` 解析共享 descriptor（含全部模型，其中引用递归 ModelValue 的模型如 About）时 V8 fastfail；`base + LauncherModel + LauncherModelUpdate`（不引用 ModelValue）干净；打破 ModelValue 递归也干净。**方案：每模型独立 descriptor 或打破递归**。CEF 151 升级未修复（V8 bug 横跨 150/151）。
- **缺 app.manifest → chrome://gpu 渲染进程 fastfail（2026-08-15 CefDemo 实证）**：CEF+.NET 应用（同 exe 子进程加载 .NET）缺应用清单时 chrome://gpu 渲染进程确定性 0xC0000409（fastfail 0x39、寄存器 0xBEEDDEAD、RIP 停在 RET；CEF150=libcef+0x42240BE、CEF151=+0x44C0BBE）。**修复 = 应用工程嵌 app.manifest**（`requestedExecutionLevel asInvoker` + `supportedOS` Win7/8/8.1/10 GUID）。**子进程分发必须用 `CefSubProcess.Run`（RendererCefApp，不带 GetBrowserProcessHandler）**——把带浏览器处理器的 SimpleApp 传给子进程 ExecuteProcess 会在渲染进程创建托管浏览器处理器包装。CefDemo 可用配置：CEF 150 代 + `C:\temp\cef150\runtime-bin` + manifest，chrome://gpu 全硬件加速 + F12 DevTools 可用。
- **CEF 响应 MimeType 不能带 charset（「网页只有文本」根因）**：`CefResponse.MimeType` 塞整串 `text/html; charset=utf-8` 会让 CEF 不识别为 HTML、页面按纯文本显示源码——`WwuiResourceHandler.GetResponseHeaders` 用 `;` 剥离。**不要显式设 `CefResponse.Charset`**（触发原生 STATUS_STACK_BUFFER_OVERRUN 崩溃）。
- **CefResourceHandler.Read 返回语义（2026-08-15 CustomScheme 失效根因）**：返回 `true` = 成功写入数据（CEF 继续调 Read）；返回 `false` 且 `bytesRead == 0` = 响应完成；**返回 `false` 且 `bytesRead != 0` = 错误 → `ERR_FAILED`**。实现须「写入即返回 true、EOF 时 bytesRead=0 返回 false」（对齐 `DefaultResourceHandler`），开头 `callback?.Dispose()` 防泄漏。曾误写「`return _offset < _data.Length`」→ 最后一块数据返回 false → 主文档 load ERR_FAILED → 页面空白、子资源全不加载。
- **Win32 窗口必须由 UI（主）线程创建（2026-08-15 双窗口死锁根因）**：命令路径（scheme POST）在 **CEF IO 线程**——非主线程 `new CefWindow` 时 `Win32NativeWindow` 的 CreateWindowExW 把 HWND 绑到 IO 线程消息队列 → 主线程 `SetWindowTextW`/`SetForegroundWindow` 等 SendMessage 跨线程等待 → 主线程与 IO 线程**互锁死锁**（两窗口全卡死；dotnet-stack 可见主线程卡 SetWindowTextW、后台线程卡 ManualResetEventSlim.Wait）。**修复 = `CefPlatform.CreateWindow` 用 `_message.RunOnUiThread` marshal 到主线程创建**（WebWindow 后续 SetTitle 等也经 RunOnUiThread，主线程拥有窗口后无跨线程 SendMessage）。
- **CEF 回调里访问 CefFrame/CefBrowser 属性 → fastfail 崩溃**：`OnAfterCreated` 里 `GetMainFrame().Url`、`OnLoadStart` 里 `frame.Url` 等诊断日志会崩 libcef（0xC0000409）。CEF 回调内不得访问 frame/browser 的 URL 属性。
- **CEF 初始化后才能建窗口**：`CefWindow` 构造断言 `CefRuntime.IsInitialized`，未调 `WebWindowUIPlatform.Init(args)`（触发 `CefPlatform.Init`）就开窗会抛 `InvalidOperationException`——加载链不再是旧 CommonBrowserAdapter 的「窗口触发延迟 Load」。
- **durable：`dotnet` CLI 对非解决方案构建把 `SolutionDir` 置为字面量 `*Undefined*`**（不是未定义/空串）——`'$(SolutionDir)' == ''` 恒假、按「非空即跳过」写的 target 直接不执行（`PackCefGlueTogether` 首版即栽在这，包永远没连带上）。判定「是否直接打包而非 slnx」要写 `'$(SolutionDir)' == '*Undefined*' Or '$(SolutionDir)' == ''`（`msbuild.exe` 直构建才是真空串，两态都收）。

## 平台选择

`UseCEF` 只对消费方应用可见（MSBuild 属性不跨 ProjectReference 传播），包模式 CEF 消费方另显式 `PackageReference WebWindowUI.Platforms.Cef`；仓库模式由 targets 按 `UseCEF` 给应用工程补平台 ProjectReference。

## 打包

Windows 上 `dotnet pack`（先打 `WebWindowUI.Natives.Windows`）。**CefGlue 四包随 CEF 平台包一起打进 `artifacts`**：vendored 源码（`third-party/CefGlue`）的 `PackageOutputPath` 指向仓库根 `artifacts`，直接 `dotnet pack WebWindowUI.Platforms.Cef.csproj` 时 csproj 内 `PackCefGlueTogether` 目标先把 4 个 CefGlue 工程也打包进同一 `artifacts`（nuspec 声明 `CefGlue.Next.*` 依赖，消费方还原需要这些包）；`dotnet pack WebWindowUI.slnx` 时 CefGlue 由解决方案自身打包（SolutionDir 为真实路径，`PackCefGlueTogether` 跳过免并发重复打包）。CefGlue 工程 `GeneratePackageOnBuild=false`，普通构建不产包。
