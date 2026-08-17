# WebWindowUI.Platforms.Windows

**Windows 平台实现**：WebView2 窗口宿主 + Win32 消息循环。由入口包 `WebWindowUI` 按平台自动引入，消费方无需显式引用（nuspec 依赖声明）。

## 依赖

- `Microsoft.Web.WebView2`
- `WebWindowUI.Core`（ProjectReference 且 `PrivateAssets="all"` → nuspec 不声明 Core 依赖，入口已带 Core；Core 无平台引用故无环）
- `WebWindowUI.Natives.Windows`（**普通** ProjectReference → nuspec 声明依赖，Win32 共享层，Windows/CEF 共用）

## 组成

| 文件 | 内容 |
|------|------|
| `WindowsPlatform.cs` | `WindowsPlatform : IPlatform`：初始化 Win32 消息循环 + 异步创建 WebView2 环境；`RunOnUiThread`/`IsUiThread` 走 `Win32MessageLoop`；`CreateWindow(options)` 返回 `WindowsWindow`。**无** `[ModuleInitializer]`——注册由应用侧 bootstrap（`WebWindowUIPlatform.Init`）或测试泵显式 `WebWindowPlatform.Register` 完成 |
| `WindowsWindow.cs` | `WindowsWindow : WebWindow`：WebView2 宿主窗口（WebResourceResolver 求首页 + 桥双向 + `ExecuteScriptAsync`）；13 个窗口状态属性/生命周期经 `Win32NativeWindow`（`INativeWindow`）真实现 |

## 关键设计

- **公开 API 消费 Natives**：消息循环/窗口生命周期一律走 `Win32MessageLoop`/`INativeWindow`（`Win32NativeWindow`），不直接引用 internal 的 `Win32`/`MessageLoopSynchronizationContext`。
- **平台注册**：本平台**无** `[ModuleInitializer]`——由应用侧 bootstrap（`WebWindowUIPlatform.Init` 经 targets 注入的 `PlatformBootstrap.g.cs` 惰性登记加载委托，`typeof` 触发加载后 `Register` 首个生效）或测试泵显式 `WebWindowPlatform.Register(new WindowsPlatform())` 注册。避免双构造死锁。
- **桥 JS**：`resolveChannel()` 自适应 `chrome.webview`（Windows）→ `window.webkit.messageHandlers.wwui`（WebKit）。

## 打包

Windows 上 `dotnet pack`（先打 `WebWindowUI.Natives.Windows` 供 restore 解析）。
