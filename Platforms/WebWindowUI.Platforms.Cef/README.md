# WebWindowUI.Platforms.Cef

**CEF 平台实现**（与 Windows 平台互斥）：CefGlue 托管包装 + Chromium 渲染内核，承载于裸 Win32 顶层窗口。**浏览器托管层直接用 CefGlue.Common 自带实现**（不再自写镜像 CommonBrowserAdapter/CommonCefClient），宿主控件为隐藏宿主 + 重挂载（对齐 CefGlue.Avalonia）。Windows 上经 `Natives.Windows` 复用 Win32 共享层。

## 依赖

- **vendored CefGlue**（`third-party/CefGlue`，针对 **CEF 151** 用 `upgrade-cef.ps1` 重生成）：`CefGlue` / `CefGlue.Common` / `CefGlue.Common.Shared` / `CefGlue.BrowserProcess.Core` 四个工程 ProjectReference
- **Windows 运行时用手动下载的 CEF 151 二进制**（NuGet 的 `chromiumembeddedframework.runtime` / `CefGlue.Next` 止步 150）：`CefRuntimeDir`（Windows）= `C:\temp\cef151\runtime-bin`，经 Content 项传播到 app 输出（libcef.dll + icudtl.dat + *.pak + locales + chrome_elf 等，不含 wrapper DLL）
- Linux/macOS 运行时仍走 NuGet 包：`cef.runtime.linux-{arm64,x64}` / `cef.runtime.osx-{arm64,x64}`
- `WebWindowUI.Core`（`PrivateAssets="all"`）；Windows 上额外普通 ProjectReference `WebWindowUI.Natives.Windows`

## 组成

| 文件 | 内容 |
|------|------|
| `CefPlatform.cs` | `CefPlatform : IWebWindowPlatform`：`CefSubProcess.Run` → `CefRuntimeLoader.Initialize(settings, customSchemes:[app, appdata])`（延迟到首个 BaseCefBrowser 构造时 Load）→ `_message.InitMessageLoop()`。保留 `_browsers` 浏览器 id → 窗口映射、`RunOnCefUiThread`、`RunMessageLoop`、对话框。**不再自建 WwuiCefApp/WwuiCefBrowserProcessHandler**（CefGlue.Common 的 BrowserCefApp 处理 scheme 注册与子进程参数） |
| `CefWindow.cs` | `CefWindow : Xilium.CefGlue.Common.BaseCefBrowser, IWindowBackend`：链接 `BaseCefBrowser.cs` partial + `BaseCefBrowser.Address.cs`（Address 实现）；实现 `CreateControl()` → `Win32CefControl`，OSR 方法抛 NotSupported。`BrowserInitialized` → 注册 scheme 映射；`BrowserClosed` → **仅主浏览器**(`ReferenceEquals(browser, UnderlyingBrowser)`)销毁顶层窗口；`LoadEnd` → `NavigationCompleted` |
| `Win32CefControl.cs` | `IControl` 实现：**隐藏宿主 + 重挂载**——`GetHostViewHandle` 返回隐藏宿主窗口，`InitializeRender` 把浏览器 HWND SetParent 重挂载进可见窗口并铺满客户区。上下文菜单/光标/工具提示最小实现 |
| `BaseCefBrowser.Address.cs` | CefGlue.Common 排除 BaseCefBrowser.cs（partial），平台工程链接后需提供 Address partial 实现 |

**给 vendored CefGlue.Common 的改动**：`InternalsVisibleTo("WebWindowUI.Platforms.Cef")`；`CommonBrowserAdapter` 加 `BrowserClosed` 事件（HandleBrowserDestroyed 触发）+ `CloseBrowser(bool)`；`BaseCefBrowser` 暴露 `BrowserClosed` 事件 + `CloseBrowser(bool)`。**DevTools 关闭也触发 BrowserClosed（所有浏览器）——CefWindow.OnBrowserClosed 必须只对主浏览器销毁窗口**，否则关 DevTools 会把主窗口一起关掉（用户实测程序崩溃/卡死）。

## 关键设计

- **隐藏宿主 + 重挂载（对齐 CefGlue.Avalonia，DevTools 行为的关键）**：`GetHostViewHandle` 返回隐藏宿主窗口（`Win32BrowserHost.CreateHiddenHost`，Natives.Windows 新增公开类），浏览器先作为隐藏窗口子窗口创建；`InitializeRender` 时 `Win32BrowserHost.Reparent`（SetParent+MoveWindow）重挂载进可见顶层窗口。浏览器直接作为可见窗口子控件时，`SetAsPopup(GetWindowHandle())` 的 DevTools 弹窗会顶替内容/即开即关。
- **初始化走 CefRuntimeLoader**：自定义 scheme（app/appdata）经 `CustomScheme` 传入，处理器工厂（`ResourceSchemeHandlerFactory`/`MessageSchemeHandlerFactory`）由 loader 注册。
- **MTML=true（CEF UI 线程独立）**：CefGlue.Common 内部 marshal 浏览器操作；`CefWindow` 原生窗口操作走主线程（`Win32MessageLoop.RunOnUiThread`）。
- **DevTools 关闭不影响主窗口**：`BrowserClosed` 对 DevTools 浏览器也触发，`OnBrowserClosed` 用 `ReferenceEquals(browser, UnderlyingBrowser)` 过滤。

## durable 坑

- **DevTools 窗口即开即关/崩溃不是 GPU 问题（2026-08-14 用户明确：不要动 GPU）**：`--disable-gpu` 无效、`Failed to create shared context for virtualization` 是 VM GPU 合成器噪音但非根因。**DevTools 前端（devtools:// 重 JS）在 CEF 150/151 的 V8 间歇 fastfail（0xC0000409）**，与嵌入方式/scheme/GPU 都无关（data: 页面 + --disable-gpu 也照样崩）。**可靠 DevTools = 远程调试**（`--remote-debugging-port` + 外部 Chrome `chrome://inspect`）。
- **launcher 的 STATUS_STACK_BUFFER_OVERRUN = protobufjs 描述符解析 V8 fastfail**：`protobuf.Root.fromJSON(descriptor)` 解析共享 descriptor（含全部模型，其中引用递归 ModelValue 的模型如 About）时 V8 fastfail；`base + LauncherModel + LauncherModelUpdate`（不引用 ModelValue）干净；打破 ModelValue 递归也干净。**方案：每模型独立 descriptor 或打破递归**。CEF 151 升级未修复（V8 bug 横跨 150/151）。
- **CEF 响应 MimeType 不能带 charset（「网页只有文本」根因）**：`CefResponse.MimeType` 塞整串 `text/html; charset=utf-8` 会让 CEF 不识别为 HTML、页面按纯文本显示源码——`WwuiResourceHandler.GetResponseHeaders` 用 `;` 剥离。**不要显式设 `CefResponse.Charset`**（触发原生 STATUS_STACK_BUFFER_OVERRUN 崩溃）。
- **CEF 回调里访问 CefFrame/CefBrowser 属性 → fastfail 崩溃**：`OnAfterCreated` 里 `GetMainFrame().Url`、`OnLoadStart` 里 `frame.Url` 等诊断日志会崩 libcef（0xC0000409）。CEF 回调内不得访问 frame/browser 的 URL 属性。

## 平台选择

`UseCEF` 只对消费方应用可见（MSBuild 属性不跨 ProjectReference 传播），包模式 CEF 消费方另显式 `PackageReference WebWindowUI.Platforms.Cef`；仓库模式由 targets 按 `UseCEF` 给应用工程补平台 ProjectReference。

## 打包

Windows 上 `dotnet pack`（先打 `WebWindowUI.Natives.Windows`）。
