# WebWindowUI.Demo.SharedNotes（应用 exe）

双屏共享便签的应用层：`Program.cs` 用**同一个** `NotesModel` 实例开 main 编辑窗 + monitor 只读墙 → 任一窗口操作全广播、其余实时跟随（多订阅者 + 远程回写排除源）。详见 [Demos/README.md](../../README.md)。
