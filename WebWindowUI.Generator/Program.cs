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
    private static int Main(string[] args)
    {
        string? modelPath = Get(args, "--model");
        string? jsonOut = Get(args, "--json-out");
        string? tsOutDir = Get(args, "--ts-out-dir");
        string? allModels = Get(args, "--all-models");
        string rootNs = Get(args, "--root-namespace") ?? "";

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

        string source = File.ReadAllText(modelPath);
        string modelClassName = Path.GetFileNameWithoutExtension(modelPath);

        // 全模型源码表（类名 → 源码）：供生成器识别 List<已知模型>（强类型 repeated）、
        // 取元素模型命名空间（TS import / 快照类型全限定）与输出全量 descriptor。
        // 读不到的模型文件跳过（防御）；只生成当前模型，其余仅用于引用解析。
        var allModelSources = new Dictionary<string, string>();
        if (!string.IsNullOrWhiteSpace(allModels))
        {
            foreach (string p in allModels
                .Split(';', StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .Where(File.Exists))
            {
                allModelSources[Path.GetFileNameWithoutExtension(p)] = File.ReadAllText(p);
            }
        }

        ModelProtoResult result = ModelProtoGenerator.Generate(source, modelClassName, allModelSources, rootNs);

        if (jsonOut is not null)
            WriteIfChanged(jsonOut, result.DescriptorJson);
        if (tsOutDir is not null)
        {
            string sub = ModelProtoGenerator.TsSubPath(result.Namespace, rootNs);
            WriteIfChanged(Path.Combine(tsOutDir, sub, modelClassName + ".ts"), result.TsCode);
            PruneStaleTs(tsOutDir, rootNs, allModelSources);
        }

        Console.WriteLine($"Generated {modelClassName}: {Path.GetFileName(jsonOut ?? "")}, models/{modelClassName}.ts");
        return 0;
    }

    /// <summary>残留 TS 清理：删除 tsOutDir 下「当前全模型集合不再产出」的模型镜像。
    /// 幂等写保持 mtime 的前提不受影响：只删不在期望集合的文件，其余文件由 WriteIfChanged 决定是否重写。
    /// 与 targets 的 _WWUI_CleanBridgeOutputs 剪枝互补——桥 descriptor 平铺（{ProtoBase}.json）按名剪即可；
    /// TS 镜像带命名空间子路径（{子路径}\{类名}.ts），改名/删模型/换命名空间都会让旧路径文件变孤儿，
    /// 而「任意子路径按类名排除」会把换路径后的旧文件漏掉（同名不同路径），故由这里按「类名 → 期望子路径」精确剪。
    /// 每模型一次调用都扫一遍（N 个模型 O(N²)，模型数十个内无感）；--all-models 缺失时无法推期望集合，跳过。</summary>
    private static void PruneStaleTs(string tsOutDir, string rootNs, IReadOnlyDictionary<string, string> allModelSources)
    {
        if (allModelSources.Count == 0 || !Directory.Exists(tsOutDir))
            return;

        var expected = new HashSet<string>(StringComparer.Ordinal);
        foreach ((string cls, string src) in allModelSources)
        {
            string ns = ModelProtoGenerator.GetNamespace(src) ?? rootNs;
            string sub = ModelProtoGenerator.TsSubPath(ns, rootNs);
            expected.Add(Path.Combine(sub, cls + ".ts").Replace('\\', '/'));
        }

        foreach (string file in Directory.EnumerateFiles(tsOutDir, "*.ts", SearchOption.AllDirectories))
        {
            string rel = Path.GetRelativePath(tsOutDir, file).Replace('\\', '/');
            if (!expected.Contains(rel))
                File.Delete(file);
        }
    }

    /// <summary>幂等写入：内容与已存在文件相同则不写（保持 mtime），供构建目标的增量判断参考。
    /// 生成器每次构建都被调用（descriptor 缺失时必须重建），但内容不变时文件时间戳不动，
    /// 前端 vite 的 FrontendInput（含 src/bridge、src/models）就不被无谓地触发重建。</summary>
    private static void WriteIfChanged(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (File.Exists(path) && File.ReadAllText(path) == content)
            return;
        File.WriteAllText(path, content);
    }

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
