# WebWindowUI.Sample（应用 exe）

三工程结构里的应用层：`Program.cs` + 各窗口类，产出 `WebWindowUI.Sample.exe`。launcher 入口按需开窗，每窗口一功能。详见上级 [Sample/README.md](../README.md)。

| 文件 | 窗口 |
|------|------|
| `Program.cs` | 入口：`WebWindowUIPlatform.Init()` + launcher |
| `LauncherWindow.cs` | 功能入口（`LauncherModel.request` 回写开窗） |
| `MainWindow.cs` / `TodosWindow.cs` / `ResourcesWindow.cs` / `MultiWindow.cs` | 双向绑定 / List\<Model\> / app:// 资源 / 共享·独立模型 |
| `NestedWindow.cs` / `NestedListWindow.cs` / `NestedDetailWindow.cs` / `NestedListItemWindow.cs` | 嵌套 + 子窗口 master-detail / 列表元素嵌套 + tags/meta |
| `SettingsWindow.cs` / `AboutWindow.cs` | 嵌套设置 / 关于 |

wwwroot 经 `AddWebWindowUIResourcesCopy` 注入（Debug 磁盘 / Release 内嵌前端 dll），模型在 `WebWindowUI.Sample.Backend`。
