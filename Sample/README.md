# Sample

**三工程同构示例**（2026-08-09 从仓库根 `WebWindowUI.Sample/` 改名，命名空间 `WebWindowUI.Sample` 不变）。每窗口一功能，覆盖框架各数据绑定能力。

| 子工程 | 角色 |
|--------|------|
| `WebWindowUI.Sample.Backend` | 模型库（`*Model.cs` + `DataProvider.cs` + `Items/` 嵌套模型） |
| `WebWindowUI.Sample` | 应用 exe（launcher 入口 + 各窗口类） |
| `WebWindowUI.Sample.Frontend` | 纯 Vue（`src/window/` 一页一窗口，`src/models/` 生成的 TS 镜像，`src/bridge/` 生成的 descriptor） |

## 功能窗口

| 窗口 | 演示能力 |
|------|----------|
| `main` | 双向绑定 |
| `todos` | `List<Model>` 一一对应（typed repeated 元素级双向） |
| `resources` | `app://` 资源 + `appbin://` 数据通道 |
| `multi` | 共享 / 独立模型 |
| `nested` | 单模型嵌套 + 子窗口 master-detail |
| `nested-list` | 列表元素嵌套 + 元素内再嵌套 tags/meta |
| `settings` | 嵌套设置模型 |
| `about` | 关于页 |

## launcher

入口按需开窗（`LauncherModel.request` 回写 + `Task.Run` 延迟清空——同步清空落在回声抑制窗口内 null 推不回前端、同按钮二次点击失效）。`bindXxx()` 绑定助手在模型 TS 镜像末尾（封装 `bindModel` + descriptor import）。

## 测试联动

Sample 的 wwwroot 经 ProjectReference 传递复制进各测试工程 bin；模型类型供 Tests.Protocol（纯逻辑）与三个平台 E2E 套件复用。改模型/前端后跑 E2E 前确认 wwwroot 非空（见各 Tests README durable 坑）。
