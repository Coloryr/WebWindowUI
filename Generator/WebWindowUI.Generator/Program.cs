namespace WebWindowUI.Generator;

/// <summary>
/// 由示例工程的 MSBuild 目标在构建时调用（C# DTO 已改由源生成器 WebWindowUI.Generator.SourceGen.ProtoGenerator
/// 内存产出，本工具只做前端文件落盘）：
///   dotnet WebWindowUI.Generator.dll --model MainWindowModel.cs --json-out <path>
///                                    [--ts-out-dir <dir> [--root-namespace <ns>] [--all-models <全部模型路径;分隔>]]
/// 读取模型源码，生成 protobufjs descriptor（基础信封已内联，前端自包含解析）与前端 TS 模型镜像
/// （落在 --ts-out-dir 下，子路径 = 命名空间 − 根命名空间；根命名空间缺省对 --all-models 的全部模型
/// 命名空间取最长公共前缀自动推断，也可 --root-namespace 显式覆盖）。
/// </summary>
internal static class Program
{
    /// <summary>
    /// 解析命令行并生成 descriptor/TS（PruneStaleTs 剪残留）；缺模型参数/文件不存在时打印错误返回 1。
    /// </summary>
    /// <param name="args">命令行参数（--model/--json-out/--ts-out-dir/--all-models/--root-namespace）。</param>
    /// <returns>进程退出码。</returns>
    private static int Main(string[] args)
    {
        var modelPath = Get(args, "--model");
        var jsonOut = Get(args, "--json-out");
        var tsOutDir = Get(args, "--ts-out-dir");
        var allModels = Get(args, "--all-models");
        var rootNs = Get(args, "--root-namespace") ?? "";

        if (modelPath is null)
        {
            Console.Error.WriteLine("缺少 --model 参数（模型源文件路径）。");
            return 1;
        }

        // 防御：--model 源文件不存在时报错退出（不抛未处理异常 → exit 134），
        // 路径由 MSBuild 目标拼装（跨平台分隔符差异可能产出非法路径），构建方能拿到可读的错误。
        if (!File.Exists(modelPath))
        {
            Console.Error.WriteLine($"模型源文件不存在：{modelPath}");
            return 1;
        }

        // 根命名空间：--root-namespace 未显式给出时，对 --all-models 的全部模型命名空间取最长公共前缀作根（零配置）。
        // 读不到的模型文件跳过（防御）；公共前缀为空时 TsSubPath 回退落根。
        if (rootNs == "" && !string.IsNullOrWhiteSpace(allModels))
        {
            rootNs = ModelProtoGenerator.CommonNamespacePrefix(
                allModels
                    .Split(';', StringSplitOptions.RemoveEmptyEntries)
                    .Select(p => p.Trim())
                    .Where(p => File.Exists(p))
                    .Select(p => ModelProtoGenerator.GetNamespace(File.ReadAllText(p)))
                    .Where(n => n is not null)
                    .Cast<string>());
        }

        var source = File.ReadAllText(modelPath);
        var modelClassName = Path.GetFileNameWithoutExtension(modelPath);

        // 全模型源码表（类名 → 源码）：供生成器识别 List<已知模型>（强类型 repeated）、
        // 取元素模型命名空间（TS import / 快照类型全限定）与输出全量 descriptor。
        // 读不到的模型文件跳过（防御）；只生成当前模型，其余仅用于引用解析。
        var allModelSources = new Dictionary<string, string>();
        if (!string.IsNullOrWhiteSpace(allModels))
        {
            foreach (var p in allModels
                .Split(';', StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .Where(File.Exists))
            {
                allModelSources[Path.GetFileNameWithoutExtension(p)] = File.ReadAllText(p);
            }
        }

        var result = ModelProtoGenerator.Generate(source, modelClassName, allModelSources, rootNs);

        if (jsonOut is not null)
            WriteIfChanged(jsonOut, result.DescriptorJson);
        if (tsOutDir is not null)
        {
            var sub = ModelProtoGenerator.TsSubPath(result.Namespace, rootNs);
            WriteIfChanged(Path.Combine(tsOutDir, sub, modelClassName + ".ts"), result.TsCode);
            PruneStaleTs(tsOutDir, rootNs, allModelSources);
        }

        Console.WriteLine($"Generated {modelClassName}: {Path.GetFileName(jsonOut ?? "")}, models/{modelClassName}.ts");
        return 0;
    }

    /// <summary>
    /// 残留 TS 清理：删除 tsOutDir 下「当前全模型集合不再产出」的模型镜像。按「类名 → 期望子路径」精确剪
    /// （模型改名/删模型/换命名空间会让旧路径文件变孤儿，按任意子路径排除会漏掉同名不同路径的旧文件）；
    /// 与 targets 的 _WWUI_CleanBridgeOutputs 剪枝互补（后者只剪平铺 bridge JSON）。幂等写前提不受影响：
    /// 只删不在期望集合的文件。--all-models 缺失时无法推期望集合，跳过。
    /// </summary>
    /// <param name="tsOutDir">TS 输出根目录。</param>
    /// <param name="rootNs">根命名空间。</param>
    /// <param name="allModelSources">全模型源码表（类名 → 源码）。</param>
    private static void PruneStaleTs(string tsOutDir, string rootNs, IReadOnlyDictionary<string, string> allModelSources)
    {
        if (allModelSources.Count == 0 || !Directory.Exists(tsOutDir))
            return;

        var expected = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (cls, src) in allModelSources)
        {
            var ns = ModelProtoGenerator.GetNamespace(src) ?? rootNs;
            var sub = ModelProtoGenerator.TsSubPath(ns, rootNs);
            expected.Add(Path.Combine(sub, cls + ".ts").Replace('\\', '/'));
        }

        foreach (var file in Directory.EnumerateFiles(tsOutDir, "*.ts", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(tsOutDir, file).Replace('\\', '/');
            if (!expected.Contains(rel))
                File.Delete(file);
        }
    }

    /// <summary>
    /// 幂等写入：内容与已存在文件相同则不写（保持 mtime），避免无谓触发前端 vite 重建。
    /// </summary>
    /// <param name="path">目标路径。</param>
    /// <param name="content">内容。</param>
    private static void WriteIfChanged(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (File.Exists(path) && File.ReadAllText(path) == content)
            return;
        File.WriteAllText(path, content);
    }

    /// <summary>
    /// 从命令行参数取键后值。
    /// </summary>
    /// <param name="args">参数数组。</param>
    /// <param name="name">键名。</param>
    /// <returns>键对应的值；未找到为 null。</returns>
    private static string? Get(string[] args, string name)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == name)
                return args[i + 1];
        }
        return null;
    }
}
