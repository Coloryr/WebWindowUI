# WebWindowUI.Sample.Backend（模型库）

三工程结构里的后端：`*Model.cs` 模型类 + `DataProvider`。引用 `WebWindowUI.Backend` 标记库，源生成器自动产出 proto/descriptor/TS。详见上级 [Sample/README.md](../README.md)。

| 文件 | 模型 |
|------|------|
| `MainWindowModel.cs` / `TodoListModel.cs` + `Items/TodoItemModel.cs` | 双向绑定 / List\<Model\> 元素级 |
| `NestedParentModel.cs` / `NestedListModel.cs` + `Items/NestedListItemModel.cs` + `Items/NestedItemTagModel.cs` / `NestedDetailModel.cs` | 嵌套 + 子窗口 master-detail / 列表元素嵌套 + tags/meta |
| `SettingsModel.cs` / `AboutModel.cs` | 嵌套设置 / 关于 |
| `MultiWindowModel.cs` / `LauncherModel.cs` | 共享·独立模型 / 入口开窗 |
| `DataProvider.cs` | 数据提供 |

`Items/` 子目录模型同样命中 `**\*Model.cs` 递归扫描（文件名即类名，须 `*Model.cs` 结尾）。
