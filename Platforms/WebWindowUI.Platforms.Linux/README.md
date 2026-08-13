# WebWindowUI.Platforms.Linux

**Linux 平台实现**：WebKit2GTK **4.1（GTK3 端口）**，不是 WebKitGTK 6.0/GTK4。GirCore 只发布 WebKitGTK 6.0（GTK4）与 GLib 绑定、无 GTK3/WebKit2 4.1 托管绑定，故窗口壳 + WebKit 绑定**全部手写 P/Invoke**。由入口包 `WebWindowUI` 按平台自动引入。

## 依赖

- `GirCore.GLib-2.0`（消息循环，纯托管，运行时才加载原生库 → 支持 Windows/macOS 上编译检查）
- `WebWindowUI.Core`（`PrivateAssets="all"`）
- `WebWindowUI.Natives.Linux`（普通 ProjectReference → nuspec 声明依赖，GTK3 窗口壳 + GObject 信号桥）

## 组成

| 文件 | 内容 |
|------|------|
| `WebKit2Native.cs` | libwebkit2gtk-4.1.so.0 + libjavascriptcoregtk-4.1.so.0 + gobject/glib/gio + **libsoup 构造 scheme 响应头**（soup2/soup3 按 WebKitGTK 实际链接版本运行时探测） |
| `WebKit2Events.cs` | GObject 信号到 C# 事件的桥 |
| `LinuxWindow.cs` | `LinuxWindow : IWindowBackend`：WebKit2GTK WebView + scheme 响应（app:// appbin://） |
| `LinuxPlatform.cs` | `LinuxPlatform : IWebWindowPlatform`：注册 + GTK 主循环集成 |
| `LinuxMessageLoopSynchronizationContext.cs` | 绑定 GTK 主循环的 `SynchronizationContext` |
| `GlobalUsings.cs` | 全局 using + 歧义消解（`Gio.Action`/`JavaScriptCore.Exception`） |

## 关键设计

- **webkit 绑定留在本平台**（`WebKit2Native.cs`），GTK 窗口壳在 `Natives.Linux`（CEF 复用）。
- **Linux scheme 响应**：WebKitGTK 旧 `webkit_uri_scheme_request_finish` 只能带 content-type 设不了响应头，须走 `WebKitURISchemeResponse`（≥2.36）+ `webkit_security_manager_register_uri_scheme_as_cors_enabled`（自定义 scheme 默认不开跨源）+ `as_secure`（镜像 Windows TreatAsSecure）+ libsoup `SoupMessageHeaders` 带 ACAO:* 与 Cache-Control（hash 资产长缓存、其余 no-store）。
  - **durable 坑一**：`set_http_headers` 是 `(transfer full)`——GUniquePtr 接管 headers 所有权、传完绝不能再 unref/free（旧实现这么干 → double-free/UAF 段错误）。
  - **durable 坑二**：headers 必须与 WebKitGTK 自身链接的 libsoup 同版（WebKitGTK < 2.42 → libsoup-2.4.so.1 / ≥ 2.42 → libsoup-3.0.so.0，释放函数不同）。`Initialize` 扫 `/proc/self/maps` 探测（libwebkit2gtk 加载后 DT_NEEDED 已映射）用同版 API 构造，两套 LibraryImport 都惰性加载只调被选中那个。
- **GirCore 信号坑**：事件用 `+=` 订阅（合成 add 访问器 CS0571）；delegate 用 `GObject.SignalHandler<T>`；文件头 `#pragma warning disable CA1416`。
- **运行前提**：Ubuntu `libwebkit2gtk-4.1-0`（其依赖自带对应 libsoup）。

## 打包

须在 **Linux 上** `dotnet pack`（先打 `WebWindowUI.Natives.Linux`）。
