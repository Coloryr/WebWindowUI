# WebWindowUI.Sample.Frontend（纯 Vue 前端）

三工程结构里的前端：真实 .csproj（`EnableDefaultItems=false`），引用 `WebWindowUI.Frontend` 标记库。`npm install` 须在本目录跑（依赖和 vite 二进制在该层 node_modules）。详见上级 [Sample/README.md](../README.md)。

## 布局

- `src/window/<窗口路径>/` 一页一窗口（index.html + main.ts + App.vue）
- `src/models/` 生成的 TS 模型镜像（末尾带 `bindXxx()` 绑定助手）
- `src/bridge/` 生成的 protobufjs descriptor（信封内联、自包含）
- `package.json` + `vite.config.ts`（桥依赖 `webwindowui-bridge`，见 webwindowui-bridge README）

## 构建

Debug 由应用 `BuildFrontend` 调 vite 直产 bin/wwwroot；Release 前端工程自驱动、产物编进前端 dll（见 CLAUDE.md「构建链路」）。
