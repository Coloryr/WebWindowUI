# WebWindowUI.Platforms.Cef

**CEF 平台实现**（与 Windows 平台互斥）：CefGlue 托管包装 + Chromium 渲染内核，承载于裸 Win32 顶层窗口。**浏览器托管层直接用 CefGlue.Common 自带实现**（不再自写镜像 CommonBrowserAdapter/CommonCefClient），宿主控件为隐藏宿主 + 重挂载（对齐 CefGlue.Avalonia）。Windows 上经 `Natives.Windows` 复用 Win32 共享层。

## 依赖

- **CefGlue.Next NuGet 包**（**CEF 150 代** 150.7871.115）：`CefGlue.Next.Core` / `CefGlue.Next.Common` / `CefGlue.Next.Common.Shared` PackageReference（本地补丁源 `E:\temp_code\CefGlue\Nuget\output`，见「上游补丁」）；`BaseCefBrowser.cs` 为**本地 vendored 副本**（Common 包不含源码，与上游同步）
- **CEF 150 运行时由包链自动部署**：`CefGlue.Next.Common` 依赖 `chromiumembeddedframework.runtime.win-x64`（150.0.11）→ 其 build props 定义 `CefRedist64CopyResources`，`CefGlue.Next.Common` 的 buildTransitive targets（`CefRedistCopyWindowsResources`）在 Exe 输出时拷贝 libcef.dll + locales + *.pak 等
- Linux/macOS 运行时仍走 NuGet 包：`cef.runtime.linux-{arm64,x64}` / `cef.runtime.osx-{arm64,x64}`
- `WebWindowUI.Core`（`PrivateAssets="all"`）；Windows 上额外普通 ProjectReference `WebWindowUI.Natives.Windows`

## 组成

| 文件 | 内容 |
|------|------|
| `CefPlatform.cs` | `CefPlatform : IWebWindowPlatform`：`Init(string[] args)` 先 `CefSubProcess.Run(args, true)` 分发子进程（同 exe 模型，应用 Main 只调 `WebWindowUIPlatform.Init(args)`）→ `CefRuntimeLoader.Initialize(settings, customSchemes:[app, appdata])`（延迟到首个 BaseCefBrowser 构造时 Load）→ `_message.InitMessageLoop()`。保留 `_browsers` 浏览器 id → 窗口映射、`RunOnCefUiThread`/`PostToCefUiThread`（ActionCefTask）、`RunMessageLoop`、对话框。BrowserCefApp 处理 scheme 注册与子进程参数 |
| `CefWindow.cs` | `CefWindow : Xilium.CefGlue.Common.BaseCefBrowser, IWindowBackend`：链接 `BaseCefBrowser.cs` partial + `BaseCefBrowser.Address.cs`（Address 实现）；**抽象成员在具体子类实现**（`CreateControl()` → `Win32CefControl`，OSR 方法抛 NotSupported）。`BrowserInitialized` → 记录主浏览器并注册 id 映射；`BrowserClosed` → **仅主浏览器**（初始化时捕获的 `_mainBrowser`，非 UnderlyingBrowser——销毁后已被适配器置空）销毁顶层窗口；`LoadEnd`（主帧）→ `NavigationCompleted` |
| `Win32CefControl.cs` | `IControl` 实现：**隐藏宿主 + 重挂载**——`GetHostViewHandle` 返回隐藏宿主窗口，`InitializeRender` 把浏览器 HWND 重挂载进目标可见窗口（`Win32BrowserHost.Reparent`）并铺满客户区。上下文菜单/光标/工具提示最小实现 |
| `BaseCefBrowser.Address.cs` | CefGlue.Common 排除 BaseCefBrowser.cs（partial），平台工程链接后提供 Address partial 实现（命名空间必须与链接文件一致 `Xilium.CefGlue.Common`） |
| `AppSchemeHandlerFactory.cs` | `CefSchemeHandlerFactory` + `CefResourceHandler`：GET 服务 wwwroot 资源，POST `__wwui` 解码 JS 回传字节按浏览器 id 分派回窗口 |

**给上游 CefGlue.Common 的改动**（`E:\temp_code\CefGlue`，**改完须重新打包** `dotnet pack -c Debug -o Nuget\output` 并清 `%USERPROFILE%\.nuget\packages\cefglue.next.*` 缓存）：`InternalsVisibleTo("WebWindowUI.Platforms.Cef")`；`CommonBrowserAdapter` 加 `BrowserClosed` 事件（`Action<CefBrowser>`，HandleBrowserDestroyed 触发）+ `CloseBrowser(bool)`；`BaseCefBrowser` 暴露 `BrowserClosed` 事件 + `CloseBrowser(bool)`（BaseCefBrowser.cs 的改动同时要同步到本工程的 vendored 副本）。**DevTools 关闭也触发 BrowserClosed（所有浏览器）——CefWindow.OnBrowserClosed 必须只对主浏览器销毁窗口**，否则关 DevTools 会把主窗口一起关掉（用户实测程序崩溃/卡死）。

## 关键设计

- **隐藏宿主 + 重挂载（对齐 CefGlue.Avalonia，DevTools 行为的关键）**：`GetHostViewHandle` 返回隐藏宿主窗口（`Win32BrowserHost.CreateHiddenHost`，Natives.Windows 新增公开类），浏览器先作为隐藏窗口子窗口创建；`InitializeRender` 时 `Win32BrowserHost.Reparent`（SetParent+MoveWindow）重挂载进可见顶层窗口。浏览器直接作为可见窗口子控件时，`SetAsPopup(GetWindowHandle())` 的 DevTools 弹窗会顶替内容/即开即关。
- **初始化走 CefRuntimeLoader**：自定义 scheme（app/appdata）经 `CustomScheme` 传入，处理器工厂（`ResourceSchemeHandlerFactory`/`MessageSchemeHandlerFactory`）由 loader 注册。
- **MTML=true（CEF UI 线程独立）**：CefGlue.Common 内部 marshal 浏览器操作；`CefWindow` 原生窗口操作走主线程（`Win32MessageLoop.RunOnUiThread`）。
- **DevTools 关闭不影响主窗口**：`BrowserClosed` 对 DevTools 浏览器也触发，`OnBrowserClosed` 用 `ReferenceEquals(browser, UnderlyingBrowser)` 过滤。

## durable 坑

- **DevTools 窗口即开即关/崩溃不是 GPU 问题（2026-08-14 用户明确：不要动 GPU）**：`--disable-gpu` 无效、`Failed to create shared context for virtualization` 是 VM GPU 合成器噪音但非根因。**DevTools 前端（devtools:// 重 JS）在 CEF 150/151 的 V8 间歇 fastfail（0xC0000409）**，与嵌入方式/scheme/GPU 都无关（data: 页面 + --disable-gpu 也照样崩）。**可靠 DevTools = 远程调试**（`--remote-debugging-port` + 外部 Chrome `chrome://inspect`）。
- **launcher 的 STATUS_STACK_BUFFER_OVERRUN = protobufjs 描述符解析 V8 fastfail**：`protobuf.Root.fromJSON(descriptor)` 解析共享 descriptor（含全部模型，其中引用递归 ModelValue 的模型如 About）时 V8 fastfail；`base + LauncherModel + LauncherModelUpdate`（不引用 ModelValue）干净；打破 ModelValue 递归也干净。**方案：每模型独立 descriptor 或打破递归**。CEF 151 升级未修复（V8 bug 横跨 150/151）。
- **缺 app.manifest → chrome://gpu 渲染进程 fastfail（2026-08-15 CefDemo 实证）**：CEF+.NET 应用（同 exe 子进程加载 .NET）缺应用清单时 chrome://gpu 渲染进程确定性 0xC0000409（fastfail 0x39、寄存器 0xBEEDDEAD、RIP 停在 RET；CEF150=libcef+0x42240BE、CEF151=+0x44C0BBE）。**修复 = 应用工程嵌 app.manifest**（`requestedExecutionLevel asInvoker` + `supportedOS` Win7/8/8.1/10 GUID）。**子进程分发必须用 `CefSubProcess.Run`（RendererCefApp，不带 GetBrowserProcessHandler）**——把带浏览器处理器的 SimpleApp 传给子进程 ExecuteProcess 会在渲染进程创建托管浏览器处理器包装（同源参照 `E:\temp_code\CefGlue\CefGlue.Demo.Avalonia`，其默认 URL 即 chrome://gpu）。CefDemo 可用配置：temp_code CefGlue（CEF 150 代）+ `C:\temp\cef150\runtime-bin` + manifest，chrome://gpu 全硬件加速 + F12 DevTools 可用。
- **CEF 响应 MimeType 不能带 charset（「网页只有文本」根因）**：`CefResponse.MimeType` 塞整串 `text/html; charset=utf-8` 会让 CEF 不识别为 HTML、页面按纯文本显示源码——`WwuiResourceHandler.GetResponseHeaders` 用 `;` 剥离。**不要显式设 `CefResponse.Charset`**（触发原生 STATUS_STACK_BUFFER_OVERRUN 崩溃）。
- **CefResourceHandler.Read 返回语义（2026-08-15 CustomScheme 失效根因）**：返回 `true` = 成功写入数据（CEF 继续调 Read）；返回 `false` 且 `bytesRead == 0` = 响应完成；**返回 `false` 且 `bytesRead != 0` = 错误 → `ERR_FAILED`**。实现须「写入即返回 true、EOF 时 bytesRead=0 返回 false」（对齐 `DefaultResourceHandler`），开头 `callback?.Dispose()` 防泄漏。曾误写「`return _offset < _data.Length`」→ 最后一块数据返回 false → 主文档 load ERR_FAILED → 页面空白、子资源全不加载。
- **Win32 窗口必须由 UI（主）线程创建（2026-08-15 双窗口死锁根因）**：命令路径（scheme POST）在 **CEF IO 线程**——非主线程 `new CefWindow` 时 `Win32NativeWindow` 的 CreateWindowExW 把 HWND 绑到 IO 线程消息队列 → 主线程 `SetWindowTextW`/`SetForegroundWindow` 等 SendMessage 跨线程等待 → 主线程与 IO 线程**互锁死锁**（两窗口全卡死；dotnet-stack 可见主线程卡 SetWindowTextW、后台线程卡 ManualResetEventSlim.Wait）。**修复 = `CefPlatform.CreateWindow` 用 `_message.RunOnUiThread` marshal 到主线程创建**（WebWindow 后续 SetTitle 等也经 RunOnUiThread，主线程拥有窗口后无跨线程 SendMessage）。
- **CEF 回调里访问 CefFrame/CefBrowser 属性 → fastfail 崩溃**：`OnAfterCreated` 里 `GetMainFrame().Url`、`OnLoadStart` 里 `frame.Url` 等诊断日志会崩 libcef（0xC0000409）。CEF 回调内不得访问 frame/browser 的 URL 属性。

## 平台选择

`UseCEF` 只对消费方应用可见（MSBuild 属性不跨 ProjectReference 传播），包模式 CEF 消费方另显式 `PackageReference WebWindowUI.Platforms.Cef`；仓库模式由 targets 按 `UseCEF` 给应用工程补平台 ProjectReference。

## 打包

Windows 上 `dotnet pack`（先打 `WebWindowUI.Natives.Windows`）。
