# WebWindowUI.Natives.Windows

**Win32 共享层**：Windows 与 CEF 平台共用的裸 Win32 P/Invoke。纯 native，无 Core 之外依赖（引 Core 仅为 `IMessageLoop`/`INativeWindow` 接口）。平台包**普通** ProjectReference（非 `PrivateAssets=all`）→ nuspec 声明依赖，消费方必须经平台包还原到本程序集。

## 组成

| 文件 | 内容 |
|------|------|
| `Win32.cs` | 常量 / 结构 / WndProcDelegate / P/Invoke 声明 |
| `Win32MessageLoop.cs` | **公开** `Win32MessageLoop : IMessageLoop`：隐藏消息窗口的 WM_RUN 调度 + `InitMessageLoop`/`RunOnUiThread`/`IsUiThread`/`MessageLoop` |
| `MessageLoopSynchronizationContext.cs` | 绑定隐藏窗口的 `SynchronizationContext`（`Post` → `PostMessageW(WM_RUN)` → 创建线程 `RunQueued`） |
| `Win32NativeWindow.cs` | **公开** `Win32NativeWindow : INativeWindow`：HWND 生命周期（Show/Hide/Close/Activate/SetTitle/SetIcon/GetSize/`WindowHandle`） |
| `Win32Native.cs` | 消息框 / 文件对话框（OpenFile/SaveFile） |

## 关键设计

- **`Win32` / `MessageLoopSynchronizationContext` 是 internal**：平台不得直接引用，一律走公开 API（`Win32MessageLoop.RunOnUiThread`）——曾踩过 CEF 残留直引 internal 类型 + 已删除 `_hwnd` 字段的 CS0122/CS0103。
- **消息窗口进程单例**：`GetOrCreateMarshalWindow` 谁先创建谁拥有消息队列。STA 泵（Tests.Windows）必须先建隐藏窗口 → 绑定 SC → 本线程注册平台，否则 WM_RUN 落进无泵线程的队列、async 延续永不派发。

## 打包

Windows 上 `dotnet pack`（无平台依赖，平台包 nuspec 声明它，须先就绪供后续 restore 解析）。
