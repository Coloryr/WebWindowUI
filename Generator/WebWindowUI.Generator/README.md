# WebWindowUI.Generator（console 生成器）

**模型 → protobufjs descriptor + TS 镜像** 的命令行生成器。随入口包分发到 `tools/net10.0/`，由 `WebWindowUI.targets` 的 `GenerateModelProto` 目标在构建时调用（每次构建都执行，生成器幂等写——内容相同不写、保持 mtime，descriptor/TS 缺失时必重建）。

## 用法

```text
dotnet WebWindowUI.Generator.dll --model <Model.cs> --json-out <descriptor.json>
                                 [--ts-out-dir <dir> [--root-namespace <ns>] [--all-models <全部模型路径;分隔>]]
```

- `--model`：当前模型的源文件路径（必填）。文件名即类名，须 `*Model.cs` 结尾。
- `--json-out`：descriptor 输出路径（9 个基础信封消息内联进每个模型 descriptor，桥 `Root.fromJSON` 自包含解析）。
- `--ts-out-dir`：TS 模型镜像输出根目录。
- `--all-models`：全部模型源码路径（`;` 分隔），供 `List<已知模型>`（typed repeated）识别与元素模型命名空间解析；只生成当前模型，其余仅用于引用解析。
- `--root-namespace`：TS 子路径的根命名空间。缺省对 `--all-models` 全部模型取**最长公共前缀**自动推断；剩余段小写进 TS 子路径（`WebWindowUI.Sample.Users` → `src/models/users/`）。想调整 TS 目录布局只需改 C# 命名空间，零配置。

## 职责

- 写 protobufjs **descriptor**（`--json-out`）。
- 写前端 **TS 模型镜像**（`--ts-out-dir`），并 `PruneStaleTs` 按「类名 → 期望子路径」精确剪残留孤儿（`--all-models` 缺失时跳过）。

`--cs-out` 已删：C# 侧生成改由 `WebWindowUI.Generator.SourceGen`（Roslyn 增量生成器）在编译期完成。

## 内部

逻辑本体在 `ModelProtoGenerator`（namespace 保持 `WebWindowUI.Generator`，console 与测试经普通引用调用，命名空间不变零改名）。console 瘦身后只负责解析 CLI、组装输入、落盘输出。
