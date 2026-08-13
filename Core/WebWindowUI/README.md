# WebWindowUI（入口包）

**入口聚合 + 平台引导**。消费方只引用本包：nuspec 依赖 `WebWindowUI.Core` + 平台包（按系统条件），自动带回运行库、平台实现及平台自身依赖（WebView2 / GirCore 等），零额外配置。

依赖单向无环：`入口 → {Core, 平台包}`、`平台包 → Core`（平台包不引用入口）。

## 唯一源码：WebWindowUIPlatform

`WebWindowUIPlatform.cs` 提供 `Init()`：**惰性加载委托驱动的 AOT 安全平台加载**。

- 应用 Main 首行调 `WebWindowUIPlatform.Init()`，触发 targets 注入的 `PlatformBootstrap.g.cs`（`[ModuleInitializer]` 里的 `RegisterPlatformLoader(() => GC.KeepAlive(typeof(平台类型)))` 惰性登记，只存委托不加载）。
- `Init()` 才真正加载平台程序集，其 `[ModuleInitializer]` 在触发线程注册进 `WebWindowPlatform`。
- JIT 下 `typeof` 强制加载；NativeAOT 下类型静态链接、根进链接闭包。**全程无 `Assembly.Load`**。

## 打包内容（随 NuGet 分发）

| 内容 | 说明 |
|------|------|
| `build/WebWindowUI.targets` + `buildTransitive/WebWindowUI.targets` | 共享构建目标：模型 → proto/descriptor/TS 生成、前端构建编排、平台分派、Release 前端 dll 宿主注入 |
| `build/WebWindowUI.props` + `buildTransitive/WebWindowUI.props` | 平台选择 props（= `WebWindowUI.Platform.props`）：`WWUIPlatform`/TFM/平台符号，NuGet「包名.props」约定自动导入，包模式消费方（含经标记包间接引用）零手写 Import |
| `build/` + `buildTransitive/FrontendHost.cs` / `FrontendLoad.cs` | 前端 dll 宿主标记源文件（targets 注入编译用，不进本包 dll） |
| `tools/net10.0/` | 模型生成器 `WebWindowUI.Generator.dll`（console，落盘 descriptor/TS） |

> 必须 `buildTransitive` 也打：NuGet 只对**直接**引用者自动导入 `build/` 的 targets，透过 `WebWindowUI.Backend`/`WebWindowUI.Frontend` 标记包间接引用的模型库只吃到 `buildTransitive/`。

## 平台引用门控

平台包用 `$(WWUI_PlatformRef)=='true'` 门控（仅框架打包传 `-p:WWUI_PlatformRef=true` 才引入平台依赖）。仓库内构建/测试默认不传：平台工程引用由 `WebWindowUI.targets` 注入应用工程、测试工程直接引用提供，本入口自身不引平台工程（MSBuild 属性不跨 ProjectReference 传播，`UseCEF` 只对消费方应用可见）。

## 仓库模式 vs 包模式

- **仓库模式**（构建/测试 slnx）：入口的平台 PackageReference 被门控关闭，平台选择/分派在应用工程侧完成（见 `WebWindowUI.targets`）。
- **包模式**（`-p:WWUI_PlatformRef=true` 打包）：完全走 nuspec 依赖；CEF 消费方另显式 `PackageReference WebWindowUI.Platforms.Cef`。
