# WebWindowUI.Templates

**`dotnet new` 项目模板**（`PackageType=Template`）：`content/` 三子工程骨架（应用 + Backend + Frontend）。`sourceName=WebWindowUI.Sample`，`preferNameDirectory: true`，`primaryOutputs=[WebWindowUI.Sample.csproj]`。消费方装包后 `dotnet new webwindowui -n <AppName>` 即得三工程骨架。

## 模板骨架

- `WebWindowUI.Sample/` 应用 exe（`Program.cs` + `WebWindowUI.Sample.csproj`）
- `WebWindowUI.Sample.Backend/` 模型库（`MainModel.cs` + csproj）
- `WebWindowUI.Sample.Frontend/` 纯 Vue（package.json + tsconfig + vite.config + public/logo.svg + csproj）
- `Directory.Build.props` / `Directory.Packages.props`（CPM）/ `WebWindowUI.Platform.props`（**本地副本**，见下）
- `WebWindowUI.Sample.slnx`（`<Project Path=.../>` 平铺）

## durable 坑（踩过，勿改）

- **不可加 restore postActions**（多 csproj 模板 `B17581D1` 每次生成都报「无法确定哪个项目文件要添加引用」）。
- **模板 slnx 坑**：`<Folder Name="/">` 根文件夹抛 MSB4025，模板骨架用裸 `<Project Path=.../>` 平铺。
- **模板 Program.cs 必须显式 `using WebWindowUI;`**：sourceName 替换后生成命名空间（`TestApp`）的外层命名空间链不含 `WebWindowUI`，未限定 `WebWindowUIPlatform` 解析不到——Sample/Demo 的命名空间在 `WebWindowUI.*` 下靠外层链恰能解析，只有模板要显式。
- **`WebWindowUI.Platform.props` 必须随模板本地化**：生成工程首个 restore 时 `.nuget.g.props` 尚不存在、包 props 还没被导入，TFM 只能来自本地文件。
- **包模式冷 slnx Release 首次构建偶发 CS0246**：首个干净 `dotnet build <App>.slnx -c Release` 的 restore 竞态偶发让前端工程某次求值识别不到 `WebWindowUI.Frontend` 标记、`_WWUI_InjectFrontendHost` 未注入。非 targets 缺陷：重跑一次或先 `dotnet restore` 再 `--no-restore`。
