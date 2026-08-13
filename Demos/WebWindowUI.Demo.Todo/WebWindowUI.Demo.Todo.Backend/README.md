# WebWindowUI.Demo.Todo.Backend（模型库）

| 文件 | 内容 |
|------|------|
| `TodoListModel.cs` | get-only `ObservableCollection<Item> Items` + `NewTitle`/`Status` + 命令 AddTitle/Toggle/Remove/ClearCompleted |
| `TodoItemModel.cs` | 待办项（typed repeated 元素） |

持久化用私有 `[JsonSerializable]` source-gen context（`TodoJsonContext`，配 `[JsonSourceGenerationOptions(WriteIndented = true)]`）规避 macOS Release 裁剪 IL2026——任何 Demo/模板想持久化 JSON 都照此办理。详见 [Demos/README.md](../../README.md)。
