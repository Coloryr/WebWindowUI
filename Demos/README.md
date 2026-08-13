# Demos

四个**有功能的真实 Demo**，是包模式生成 = 包模式端到端验证样本。**不在**仓库根 `WebWindowUI.slnx` 里——各自 `Demos/<Demo>/<Demo>.slnx`，先 `dotnet pack` 再单独构建（主解决方案构建不依赖 artifacts 本地源）。

| Demo | 演示 |
|------|------|
| `WebWindowUI.Demo.Todo` | 待办：`TodoListModel`（get-only ObservableCollection + 命令 + JSON 持久化 `%LocalAppData%\...\todos.json`） |
| `WebWindowUI.Demo.SharedNotes` | 双屏共享便签：同一模型实例开 main 编辑窗 + monitor 只读墙 → 全广播 |
| `WebWindowUI.Demo.Monitor` | 系统监控：嵌套模型 master-detail + Timer 线程池跨线程推送 |
| `WebWindowUI.Demo.ImageGallery` | 图片画廊：byte[] 在 typed repeated 里下发 + 双模式上传 |

## 构建

```bash
dotnet pack -c Release -o artifacts/ -p:WWUI_PlatformRef=true   # 先在仓库根打框架包
dotnet build Demos/<Demo>/<Demo>.slnx -c Debug                 # 再单独构建 Demo
```

## 关键设计（踩坑换来的事实）

- **`WebWindowUI.Demo.Todo` macOS Release 裁剪坑**：反射式 `JsonSerializer.Deserialize/Serialize<T>` 报 IL2026（私有 DTO 成员在裁剪下可能被剪掉、持久化运行时 break）——改私有 `[JsonSerializable]` source-gen context（`TodoJsonContext.Default.ListTodoItemDto`）。**任何 Demo/模板想持久化 JSON 都照此办理**。
- **`ImageGallery` 双模式上传**：`UploadBytes`（前端 `<input type="file">` 读 byte[] 回传）/ `PickFile`（`#if WINDOWS` 弹系统原生 `OpenFileDialog` 自读源文件）。命令参数 DTO 走反射路径重建（须参数化 ctor + 可写属性名与 camelCase 前端键忽略大小写匹配）。`UseWPF` 后 SDK 桌面隐式 using 不含 `System.IO`，须显式 using。
