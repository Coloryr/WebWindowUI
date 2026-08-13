# WebWindowUI.Demo.Monitor.Backend（模型库）

| 文件 | 内容 |
|------|------|
| `MonitorModel.cs` | 采样 Timer 线程池线程跨线程推送 |
| `MonitorSettingsModel.cs` | 嵌套设置模型（`PollIntervalMs` 等） |
| `ProcessModel.cs` | 进程条目 |

设置窗口绑 `model.Settings` 同一子实例（master-detail，改 PollIntervalMs 主窗口订阅重建定时器立即生效）。主窗口展示嵌套 settings 用序数键翻译。详见 [Demos/README.md](../../README.md)。
