# WebWindowUI.Tests.Linux

**WebKitGTK 4.1 平台 E2E**（19 用例，xunit，与 Windows/macOS 齐平）：`WebKitModelBridgeTests.cs` 覆盖模型桥全场景。**无条件**引用 `WebWindowUI.Platforms.Linux`，跨主机可编译、只在 Linux 主机跑（`IsTestProject` 按 `WWUIPlatform=='Linux'` 门控）。

## Support

| 文件 | 职责 |
|------|------|
| `GtkPump.cs` | GTK 主循环泵（镜像 Windows 的 `StaThreadPump`） |
| `WebKitTestHarness.cs` | WebKitGTK 测试助手：建窗/等 bridge ready/双窗口共享模型泛型重载/JS 求值 |

## 文件约定

- `Timeout = TimeSpan.FromSeconds(90)` 静态字段。
- 脚本带 `; 0` 后缀（WebKit 求值多语句脚本需尾表达式）。

## durable 坑

- 平台注册必须在泵线程（镜像 Windows STA 语义），否则异步延续收不到导航/消息。
- 跑前确认 Sample 前端 node_modules 的桥是最新（见仓库根 CLAUDE.md「前端调试」）；旧桥（0.1.5）产物无 `_modelInstanceId` 捕获逻辑 → 根级/元素级实例 ID 全缺、数据快照照常到达（迷惑性强）。

## 回归

Linux 主机 `dotnet test WebWindowUI.slnx -c Debug` → 124（协议 105 + 本套件 19）。
