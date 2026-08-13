# WebWindowUI.Demo.ImageGallery.Backend（模型库）

| 文件 | 内容 |
|------|------|
| `ImageItemModel.cs` | `byte[]? Data`（生成器映射 bytes→Uint8Array）+ `string Path`（存储完整路径，卡片/lightbox 灰字展示） |
| `ImageGalleryModel.cs` | 画廊集合 + 双模式上传命令 |
| `UploadFile.cs` | 上传命令参数 DTO（`{ name, data, path }`） |

**双模式上传**：`UploadBytes`（前端 `<input type="file">` 读 byte[] 回传）/ `PickFile`（`#if WINDOWS` 弹系统原生 `OpenFileDialog` 自读源文件拷入存储目录），共用 `StoreBytes` 落盘 `%LocalAppData%\WebWindowUI.Demo.ImageGallery\images`。命令参数 DTO 走**反射路径**重建（须参数化 ctor + 可写属性名与 camelCase 前端键忽略大小写匹配）。Backend csproj 有 `<UseWPF Condition="'$(WWUIPlatform)' == 'Windows'">true</UseWPF>`。详见 [Demos/README.md](../../README.md)。
