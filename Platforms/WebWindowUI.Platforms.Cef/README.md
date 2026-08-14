# WebWindowUI.Platforms.Cef

**CEF 平台实现**（与 Windows 平台互斥）：CefGlue.Next 托管包装 + Chromium 渲染内核，承载于裸 Win32 子窗口（CEF 子浏览器窗口为子控件）。Windows 上经 `Natives.Windows` 复用 Win32 共享层；Linux/macOS 上无独立 Natives 层（GTK 窗口壳供未来复用，见 `Natives.Linux`）。

## 依赖

- `CefGlue.Next.Core` / `CefGlue.Next.Common` / `CefGlue.Next.Common.Shared` / `CefGlue.Next.BrowserProcess.Core`
- 运行时包按 `WWUIPlatform` + 主机架构条件引入：`cef.runtime.linux-{arm64,x64}` / `cef.runtime.osx-{arm64,x64}` / `chromiumembeddedframework.runtime.win-{arm64,x64}`（启动自动下载运行时）
- `WebWindowUI.Core`（`PrivateAssets="all"`）
- Windows 上额外普通 ProjectReference `WebWindowUI.Natives.Windows`（Win32 消息循环 + 窗口宿主）

## 组成

| 文件 | 内容 |
|------|------|
| `CefPlatform.cs` | `CefPlatform : IWebWindowPlatform`：初始化 CEF 运行时（**镜像 `CefRuntimeLoader`**：`CefRuntime.Load` + `UncaughtExceptionStackSize=100` + 按平台 MTML/NoSandbox/ExternalMessagePump——Windows/Linux **MTML=true**、Mac MTML=false+ExternalMessagePump + `ProcessExit→Shutdown`）+ 注册 app/appbin 自定义 scheme 处理器（Standard/Secure/CorsEnabled/FetchEnabled）；`_browsers` 浏览器 id → 窗口映射分派 scheme 回调；`RunOnCefUiThread`（PostTask 到 CEF UI 线程 + 等 OnContextInitialized 门控） |
| `CommonBrowserAdapter.cs` | `CommonBrowserAdapter`（镜像上游同名类）：浏览器生命周期引擎——持 `CommonCefClient`/主浏览器、建浏览器（`SetupBrowserView` = `SetAsChild` + `WS_EX_NOACTIVATE`、**不设 runtime_style** → Chrome 样式子窗口嵌入）、路由 CEF 回调、执行 JS、DevTools、关闭；事件 `Initialized`/`LoadEnd`/`BrowserClosed`。**浏览器操作一律 marshal 到 CEF UI 线程**。裁剪上游渲染进程 IPC/对象绑定/崩溃管道。含 `ICefBrowserHost`/`IControl` 接口与 `LoadEnd` 事件类型 |
| `CommonCefClient.cs` | `CommonCefClient : CefClient`（镜像上游）：安装生命期/加载处理器（`CommonCefLifeSpanHandler`/`CommonCefLoadHandler`），CEF 回调路由回 `ICefBrowserHost` |
| `BaseCefBrowser.cs` | `BaseCefBrowser` 抽象基类（薄壳）：只暴露事件 `BrowserInitialized`/`LoadEnd`/`BrowserClosed` 与操作 `ExecuteJavaScript`/`ShowDeveloperTools`/`CloseBrowser`/`Address`/`CreateBrowser`，全部委托给 `CommonBrowserAdapter`；宿主控件由子类传入（构造 `internal`，本平台每窗口一个 CEF 显示） |
| `CefWindow.cs` | `CefWindow : BaseCefBrowser, IWindowBackend`：镜像 `WindowsWindow`、渲染内核换 CEF。**持有 `_nativeWindow`（Win32 顶层窗口）** 并适配成 `Win32Control` 交给基类；订阅基类事件：`BrowserInitialized` → scheme 映射注册 + 1s 后自动 DevTools，`LoadEnd` → `NavigationCompleted`，`BrowserClosed` → 摘映射 + 销毁顶层窗口 |

## 关键设计

- **消费公开 API**：`CefWindow` 用 `_nativeWindow.WindowHandle`/`GetSize()`/`Close()`——曾残留直引 internal `Win32` + 已删除 `_hwnd` 字段致 CS0122/CS0103（拆分 native 后未同步），一律走 `Win32MessageLoop.RunOnUiThread`。
- **浏览器生命周期在 `CommonBrowserAdapter`**：`CefClient`/处理器/建浏览器/执行 JS/DevTools/关闭全在适配器，`BaseCefBrowser` 只是薄壳、`CefWindow` 只承载窗口宿主与 `IWindowBackend` 契约；`BrowserClosed`（on_before_close 主浏览器）同时触发 `Closed` 事件（对齐 Windows 平台 `NativeWindow_Destory → Closed`）。
- **MTML=true + Chrome 样式主浏览器**：镜像 CefGlue.Demo.Avalonia 的启动路径。CEF UI 线程独立于主线程——`CommonBrowserAdapter` 的浏览器操作内部 `RunOnCefUiThread`（`PostTask(UI)` + 同步等待，`OnContextInitialized` 门控），`CefWindow` 的原生窗口操作走主线程（`Win32MessageLoop`）。主浏览器 `SetupBrowserView` 不设 runtime_style（Chrome bootstrap 下解析为 Chrome 子窗口嵌入），DevTools（Chrome-only）才能附着。
- **DevTools 自动打开**：`BrowserInitialized` 后 1s 自动 `ShowDeveloperTools`（Chrome 样式、**不设父窗口**）。**durable 坑：不能 `SetAsPopup(GetWindowHandle())`**——主浏览器是顶层窗口的子控件，GetWindowHandle() 返回子窗口句柄，作 SetAsPopup 父句柄会被 CEF 用作 DevTools 宿主 → DevTools 顶替主网页内容显示（实测用户机器）。不设父窗口则 CEF 用独立 DevTools 窗口。损坏/虚拟化 GPU 下 DevTools 窗口仍可能即开即关（本机 VM 复现，renderer 无崩溃、窗口关闭原因未明）。
- **平台选择**：`UseCEF` 只对消费方应用可见（MSBuild 属性不跨 ProjectReference 传播），包模式 CEF 消费方另显式 `PackageReference WebWindowUI.Platforms.Cef`；仓库模式由 targets 按 `UseCEF` 给应用工程补平台 ProjectReference。

## 打包

Windows 上 `dotnet pack`（先打 `WebWindowUI.Natives.Windows`）。
