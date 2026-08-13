# WebWindowUI.Tests.Windows

**WebView2 平台 E2E**（19 用例，xunit）：`WebView2ModelBridgeTests.cs` 覆盖模型桥全场景（数据快照/增量推送/集合差量/元素级双向/命令/共享模型多窗口广播/模型实例 ID）。**无条件**引用 `WebWindowUI.Platforms.Windows` + `WebWindowUI.Natives.Windows`，跨主机可编译、只在 Windows 主机跑（`IsTestProject` 按 `WWUIPlatform=='Windows'` 门控）。

## Support

| 文件 | 职责 |
|------|------|
| `StaThreadPump.cs` | STA 泵线程：先 `Win32.GetOrCreateMarshalWindow` 建隐藏消息窗口 → 绑定 SC → **在本线程**加载平台程序集触发 `[ModuleInitializer]` 注册；`MsgWaitForMultipleObjectsEx` + 200ms 兜底吞 WM_QUIT |
| `PumpWin32.cs` | Win32 消息泵 |
| `TestBootstrap.cs` | 平台引导（调 `WebWindowUIPlatform.Init`） |
| `WebView2TestHarness.cs` | WebView2 测试助手：建窗/等 bridge ready/双窗口共享模型泛型重载/JS 求值 |

## durable 坑

- **STA 泵线程初始化顺序**：`GetOrCreateMarshalWindow` 是进程单例、谁先创建谁拥有消息队列。平台注册若发生在别的线程，WM_RUN 全落进那个无泵线程的队列、async 延续永不派发 → 测试全挂 `WaitBridgeReadyAsync` 超时。
- **全部 `WaitBridgeReadyAsync` 超时**：先查测试 bin 的 wwwroot 是否空目录（`.frontend-stamp` 在但 vite 产物缺失时 MSBuild 误判 up-to-date 跳过 vite）；修复：删 `app bin\...\wwwroot` 重建。
- **绝不要结束 Windows SearchHost 的 msedgewebview2.exe 进程**。

## 回归

Windows 主机 `dotnet test WebWindowUI.slnx -c Debug` → 124（协议 105 + 本套件 19）。
