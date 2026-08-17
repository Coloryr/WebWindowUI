# WebWindowUI.Natives.Linux

**GTK3 原生 P/Invoke 共享层**（拆分是为 CEF 复用）：GTK 窗口壳 + GObject 信号桥，Linux WebKit 与 CEF 平台共用。**不含任何 webkit 绑定**——libwebkit2gtk 相关内容留在 `WebWindowUI.Platforms.Linux`（WebKit 是 Linux WebKit 平台私有，CEF 用 Chromium 不需要它；本层拆分是为将来 Linux CEF 平台复用 GTK 窗口壳）。

## 组成

| 文件 | 内容 |
|------|------|
| `GtkNative.cs` | 裸 GTK3 P/Invoke（`libgtk-3.so.0` + 消息框/文件对话框）+ **GObject 信号桥** `ConnectSignal`/`DisconnectSignal`（`libgobject-2.0.so.0`，窗口 destroy/configure 信号） |
| `LinuxNativeWindow.cs` | **公开** `LinuxNativeWindow : INativeWindow`（镜像 `Win32NativeWindow`）：GTK 窗口句柄 + 信号桥 + `SetChild` 挂 WebView + 生命周期/窗口状态属性/事件（同 `INativeWindow` 契约） |
| `GlobalUsings.cs` | 全局 using |

## 关键设计

- **`GtkNative` 是 internal**：经 `InternalsVisibleTo WebWindowUI.Platforms.Linux` 暴露。引用 Core 只为 `INativeWindow`/`WebWindowOptions`（同 Natives.Windows 引 Core 只为 `IMessageLoop`，非「平台依赖」）。
- **镜像 Natives.Windows**：平台包**普通** ProjectReference → nuspec 声明依赖；Linux 平台包经公开 `LinuxNativeWindow : INativeWindow` 消费本层。已取代平台里旧 `GtkWindowHost`。
- **跨主机编译**：只 P/Invoke 原生库、不加载，支持在 Windows/macOS 上编译检查。

## 打包

须在 **Linux 上** `dotnet pack`（soname 引用不加载只做编译检查，Windows 上只能构建）；先于 `Platforms.Linux` 打包（nuspec 声明依赖，须就绪供 restore 解析）。
