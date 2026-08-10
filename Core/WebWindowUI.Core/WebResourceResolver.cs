using System.Reflection;

namespace WebWindowUI.Core;

/// <summary>
/// 内置静态资源提供者：把自定义 scheme 的相对路径映射到 wwwroot 资源（Vue+Vite 构建产物）。
/// 两种来源，按构建配置互斥存在、内嵌优先：
///   Release：wwwroot 以 EmbeddedResource 内嵌进前端 dll（LogicalName = wwwroot\<相对路径>，
///            见 WebWindowUI.targets 的 _WWUI_EmbedWwwroot）。前端 dll 由应用侧 FrontendLoad
///            模块初始化器在进程启动时经 typeof 强制加载（AOT 安全）——
///            这里只扫已加载程序集即命中，应用输出目录不再有磁盘 wwwroot。
///   Debug：wwwroot 直产在产物目录（AppContext.BaseDirectory\wwwroot），从磁盘读。
/// 已加载程序集懒扫描（单例缓存），取含「wwwroot\」前缀资源的程序集。
/// </summary>
public static class WebResourceResolver
{
    private static readonly object Sync = new();
    private static Assembly[]? _embeddedCandidates;

    public static Stream? Resolve(string relativePath)
    {
        // 1) 内嵌资源（Release：wwwroot 编进前端 dll，前端 dll 已被应用模块初始化器加载进已加载程序集）。
        //    查找名与构建侧 LogicalName 约定对应（wwwroot\ + 相对路径，/ 转 \）。
        var embeddedName = "wwwroot\\" + relativePath.Replace('/', '\\');
        foreach (var asm in GetEmbeddedCandidates())
        {
            Stream? stream = asm.GetManifestResourceStream(embeddedName);
            if (stream is not null)
                return stream;
        }

        // 2) 磁盘回退（Debug：wwwroot 直产产物目录）
        var root = Path.Combine(AppContext.BaseDirectory, "wwwroot");
        var full = Path.GetFullPath(Path.Combine(root, relativePath));
        // 防止目录穿越：解析结果必须落在 wwwroot 内
        if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            return null;
        return File.Exists(full) ? File.OpenRead(full) : null;
    }

    /// <summary>已加载程序集里含 wwwroot 嵌入资源的集合，结果缓存。
    /// Release 下前端 dll 已由应用模块初始化器强制加载（见 FrontendLoad/FrontendHost），只扫已加载程序集即命中，
    /// 无需运行时按名加载（NativeAOT 不支持）。</summary>
    private static Assembly[] GetEmbeddedCandidates()
    {
        var candidates = _embeddedCandidates;
        if (candidates is not null)
            return candidates;

        lock (Sync)
        {
            if (_embeddedCandidates is not null)
                return _embeddedCandidates;

            var found = new List<Assembly>();
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                if (HasWwwrootResources(asm))
                    found.Add(asm);

            _embeddedCandidates = [.. found];
            return _embeddedCandidates;
        }
    }

    private static bool HasWwwrootResources(Assembly asm)
    {
        try
        {
            foreach (var name in asm.GetManifestResourceNames())
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
