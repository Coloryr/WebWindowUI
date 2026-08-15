# CefDemo

**CEF 直连测试应用**（CefGlue 最小示例，验证 CEF 150 运行时 + chrome://gpu 场景；对齐 `E:\temp_code\CefGlue\CefGlue.Demo.Avalonia`）。

## 依赖

- **temp_code CefGlue 源码工程**（`E:\temp_code\CefGlue`，**CEF 150 代**）：`CefGlue` / `CefGlue.BrowserProcess.Core` ProjectReference
- **CEF 150 运行时**：手动放到输出目录（`libcef.dll` + locales + `*.pak` + chrome_elf 等，来源 `C:\temp\cef150\runtime-bin` 或 `chromiumembeddedframework.runtime.win-x64` 包）
- **`app.manifest`**：**必须**——缺应用清单时 chrome://gpu 渲染进程确定性崩溃（0xC0000409，见 WebWindowUI.Platforms.Cef README durable 坑）

## 设计

- 入口 `Program.cs`：`CefSubProcess.Run(args, true)` 子进程分发（同 exe 模型）→ `CefRuntime.Initialize`（`CefSettings{NoSandbox=true}` 最小化）→ 消息循环。
- `SimpleApp`：仅 GPU 进程注入 `--use-angle=gl`（对齐 C 实例 simple_app.c）。
- 默认页面 chrome://gpu（`SimpleBrowserProcessHandler`），验证 GPU 全硬件加速 + F12 DevTools 可用。

## 运行

`dotnet build` 后从输出目录启动（CEF 运行时需在 exe 同级）；`--url=` 可指定页面。
