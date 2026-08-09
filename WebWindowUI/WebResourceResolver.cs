using System.Reflection;

namespace WebWindowUI;

/// <summary>
/// 内置静态资源提供者：把自定义 scheme 的相对路径映射到 wwwroot 资源（Vue+Vite 构建产物）。
/// 两种来源，按构建配置互斥存在、内嵌优先：
///   Release：wwwroot 以 EmbeddedResource 内嵌进前端 dll（LogicalName = wwwroot\<相对路径>，
///            见 WebWindowUI.targets 的 _WWUI_EmbedWwwroot），运行时从程序集嵌入资源读——应用输出目录不再有磁盘 wwwroot。
///   Debug：wwwroot 直产在产物目录（AppContext.BaseDirectory\wwwroot），从磁盘读。
/// 内嵌程序集懒发现：扫描已加载程序集 + BaseDirectory 下 dll，取含「wwwroot\」前缀资源的程序集缓存。
/// </summary>
public static class WebResourceResolver
{
    private static readonly object Sync = new();
    private static Assembly[]? _embeddedCandidates;

    public static Stream? Resolve(string relativePath)
    {
        // 1) 内嵌资源（Release：wwwroot 编进前端 dll）。查找名与构建侧 LogicalName 约定对应
        //    （wwwroot\ + 相对路径，/ 转 \）。
        string embeddedName = "wwwroot\\" + relativePath.Replace('/', '\\');
        foreach (Assembly asm in GetEmbeddedCandidates())
        {
            Stream? stream = asm.GetManifestResourceStream(embeddedName);
            if (stream is not null)
                return stream;
        }

        // 2) 磁盘回退（Debug：wwwroot 直产产物目录）
        string root = Path.Combine(AppContext.BaseDirectory, "wwwroot");
        string full = Path.GetFullPath(Path.Combine(root, relativePath));
        // 防止目录穿越：解析结果必须落在 wwwroot 内
        if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            return null;
        return File.Exists(full) ? File.OpenRead(full) : null;
    }

    /// <summary>懒发现含 wwwroot 嵌入资源的程序集（已加载的 + BaseDirectory 下的 dll），结果缓存。</summary>
    private static Assembly[] GetEmbeddedCandidates()
    {
        Assembly[]? candidates = _embeddedCandidates;
        if (candidates is not null)
            return candidates;

        lock (Sync)
        {
            if (_embeddedCandidates is not null)
                return _embeddedCandidates;

            var found = new List<Assembly>();
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
                if (HasWwwrootResources(asm))
                    found.Add(asm);

            // 前端 dll 未被引用其类型的应用自动加载，须主动发现：先按名 Load（默认上下文，随 deps 解析），
            // 失败再 LoadFrom（避免把同一程序集加载进重复的 LoadFrom 上下文）。
            try
            {
                foreach (string dll in Directory.GetFiles(AppContext.BaseDirectory, "*.dll"))
                {
                    try
                    {
                        string name = Path.GetFileNameWithoutExtension(dll);
                        Assembly asm;
                        try
                        {
                            asm = Assembly.Load(new AssemblyName(name));
                        }
                        catch
                        {
                            asm = Assembly.LoadFrom(dll);
                        }
                        if (HasWwwrootResources(asm))
                            found.Add(asm);
                    }
                    catch
                    {
                        // 非托管/损坏 dll 跳过
                    }
                }
            }
            catch
            {
                // BaseDirectory 不可枚举时忽略
            }

            _embeddedCandidates = [.. found];
            return _embeddedCandidates;
        }
    }

    private static bool HasWwwrootResources(Assembly asm)
    {
        try
        {
            foreach (string name in asm.GetManifestResourceNames())
                if (name.StartsWith("wwwroot\\", StringComparison.Ordinal))
                    return true;
        }
        catch
        {
            // 反射失败（无托管资源表等）忽略
        }
        return false;
    }
}
