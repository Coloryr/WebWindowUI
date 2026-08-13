# WebWindowUI.Platforms.Windows

**Windows 平台实现**：WebView2 窗口宿主 + Win32 消息循环。由入口包 `WebWindowUI` 按平台自动引入，消费方无需显式引用（nuspec 依赖声明）。

## 依赖

- `Microsoft.Web.WebView2`
- `WebWindowUI.Core`（ProjectReference 且 `PrivateAssets="all"` → nuspec 不声明 Core 依赖，入口已带 Core；Core 无平台引用故无环）
- `WebWindowUI.Natives.Windows`（**普通** ProjectReference → nuspec 声明依赖，Win32 共享层，Windows/CEF 共用）

## 组成

| 文件 | 内容 |
|------|------|
| `WindowsPlatform.cs` | `WindowsPlatform : IWebWindowPlatform`：`[ModuleInitializer]`（`PlatformRegistration.cs` 同款 CA2255）经 `internal Register` 注册进 `WebWindowPlatform`；初始化 Win32 消息循环 + 异步创建 WebView2 环境；`RunOnUiThread`/`IsUiThread` 走 `Win32MessageLoop` |
| `WindowsWindow.cs` | `WindowsWindow : IWindowBackend`：WebView2 宿主窗口（WebResourceResolver 求首页 + 桥双向 + `ExecuteScriptAsync`） |

## 关键设计

- **公开 API 消费 Natives**：消息循环/窗口生命周期一律走 `Win32MessageLoop`/`INativeWindow`（`Win32NativeWindow`），不直接引用 internal 的 `Win32`/`MessageLoopSynchronizationContext`。
- **平台注册**：程序集 `[ModuleInitializer]` 调 `internal Register` 写 `WebWindowPlatform` 静态字段（无静态字段初始化器 → 无 cctor，无类型初始化死锁）。加载由应用侧 `WebWindowUIPlatform.Init()` 触发（惰性委托 + `typeof` 静态引用，AOT 安全，见入口包 README）。
- **桥 JS**：`resolveChannel()` 自适应 `chrome.webview`（Windows）→ `window.webkit.messageHandlers.wwui`（WebKit）。

## 打包

Windows 上 `dotnet pack`（先打 `WebWindowUI.Natives.Windows` 供 restore 解析）。
