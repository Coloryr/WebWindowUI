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
| `CefPlatform.cs` | `CefPlatform : IWebWindowPlatform`：初始化 CEF 运行时（**单线程模式** MTML=false，CEF UI 线程 == 主线程，`RunMessageLoop` 用 `CefRuntime.RunMessageLoop()`）+ 注册 app/appbin 自定义 scheme 处理器（Standard/Secure/CorsEnabled/FetchEnabled）；`_browsers` 浏览器 id → 窗口映射分派 scheme 回调 |
| `CefWindow.cs` | `CefWindow : IWindowBackend`：镜像 `WindowsWindow`、渲染内核换 CEF。`_nativeWindow`（`INativeWindow` = `Win32NativeWindow`）承载顶层窗口 + `CefClient`/生命周期/加载处理器（on_load_end → `NavigationCompleted`） |

## 关键设计

- **消费公开 API**：`CefWindow` 用 `_nativeWindow.WindowHandle`/`GetSize()`/`Close()`——曾残留直引 internal `Win32` + 已删除 `_hwnd` 字段致 CS0122/CS0103（拆分 native 后未同步），一律走 `Win32MessageLoop.RunOnUiThread`。
- **平台选择**：`UseCEF` 只对消费方应用可见（MSBuild 属性不跨 ProjectReference 传播），包模式 CEF 消费方另显式 `PackageReference WebWindowUI.Platforms.Cef`；仓库模式由 targets 按 `UseCEF` 给应用工程补平台 ProjectReference。

## 打包

Windows 上 `dotnet pack`（先打 `WebWindowUI.Natives.Windows`）。
