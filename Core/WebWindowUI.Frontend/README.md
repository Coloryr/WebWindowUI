# WebWindowUI.Frontend（前端角色标记包）

**前端（Vue + Vite）工程引用本包即声明「前端」角色**——`WebWindowUI.targets` 据此自识别并回传全路径（`_WWUI_ReportFrontend`），激活本工程的 vite 构建目标，并让应用工程拿到 `WebWindowUIFrontendProject` 用于前端编排。

一条 `PackageReference` 即带回：入口 `WebWindowUI`（共享 targets + 平台 + Core）。

## 内容

- 空标记类 `Marker.cs`（角色证明）。
- 依赖入口包 `WebWindowUI`。

## 角色与构建

前端工程是**纯 Vue**（真实 .csproj，`EnableDefaultItems=false` 只认前端源文件），本身无 C# 类型：

- **Debug**：应用 `BuildFrontend` 调前端工程 `WebWindowUIBuildFrontend`，vite **直产应用 bin/wwwroot** 磁盘读取。
- **Release**：前端工程自驱动，vite 产物注入 `EmbeddedResource` **编进前端 dll**（`wwwroot\相对路径`）；应用靠 targets 注入的 `FrontendHost` 宿主标记类型强制加载前端 dll，`WebResourceResolver` 从内嵌资源读取。

## 用法

```xml
<ProjectReference Include="..\WebWindowUI.Sample.Frontend\WebWindowUI.Sample.Frontend.csproj" />
```

前端源码布局：`src/window/<窗口路径>/` 一页一窗口，`src/models/`（生成的 TS 镜像）、`src/bridge/`（生成的 descriptor）、`package.json` + `vite.config.ts`（桥依赖 `webwindowui-bridge`，见 webwindowui-bridge README）。
