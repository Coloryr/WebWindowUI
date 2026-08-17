# WebWindowUI.Platforms.MacOS

**macOS 平台实现**：NSWindow + WKWebView，四个 `[Export]` NSObject 子类，编译零警告。由入口包 `WebWindowUI` 按平台自动引入。已真机验证（2026-08，net10.0-macos + workload install macos + `ValidateXcodeVersion=false` 绕过 Xcode 小版本错配）。

## 跨宿主构建

macOS 平台包只在 **Mac 主机**（`_WWUI_IsMacHost` = OSX）编译真实 `net10.0-macos` 源码；非 macOS 宿主上退化为 **net10.0 空壳**（`<Compile Remove="**/*.cs" />` 整包剔除，不编译任何源文件）——保证 slnx 全量构建在 Windows/Linux 上不因缺 macos 工作负载而挂。`MACOS` 编译符号恒定义。

## 组成

| 文件 | 内容 |
|------|------|
| `MacOSPlatform.cs` | 平台注册 + 进程级窗口 registry（`HashSet<MacOSWindow>`）+ 生命周期 |
| `MacOSWindow.cs` | `MacOSWindow : WebWindow`：NSWindow + WKWebView 宿主 + `WKURLSchemeHandler`；窗口状态面经 `MacOSNativeWindow`（`INativeWindow`）实现 |
| `MacOSMessageLoopSynchronizationContext.cs` | 绑定主队列的 `SynchronizationContext`（`Post` 唤醒路径） |
| `PlatformRegistration.cs` | `[ModuleInitializer]` 注册进 `WebWindowPlatform`（CA2255） |

## 关键设计（踩坑换来的事实）

- **`NSApplication.Init()` 不幂等**：平台被构造两次（平台程序集自身 `[ModuleInitializer]` + 应用注入 bootstrap 各 new 一次）第二次抛 `InvalidOperationException: Init has already been invoked`——静态标志守卫。
- **窗口生命周期用进程级 registry**（镜像 Linux）：`OnWindowWillClose` 注销、最后一个关闭 `NSApplication.Terminate` 退出主事件循环（`NSApplication.Run()` 返回）。
- **自定义 scheme** 走 `WKURLSchemeHandler` + 静态 `WebWindowResource`：每窗口独立注册（不共享 WebContext），`SetUrlSchemeHandler(handler, "app"/"appdata")`；响应带 Content-Type/Cache-Control/ACAO。
- **桥 JS**：`resolveChannel()` 自适应 `window.webkit.messageHandlers.wwui`。
- **Debug bundle wwwroot 不自动带**：targets `_WWUI_MacOSBundleWwwroot` 在 `AfterTargets="Build"` 把 `$(OutDir)\wwwroot` 拷进 `Contents\MonoBundle\wwwroot`；Release 短路（wwwroot 已内嵌前端 dll）。`AppBundleDir` 无尾部斜杠，拼接须显式分隔符。

## 打包

须在 **macOS 上** `dotnet pack`（真实 TFM 只在 Mac 主机编译）。
