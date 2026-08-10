using System.Runtime.InteropServices;
using System.Security.Cryptography;
using SharpCompress.Compressors;
using SharpCompress.Compressors.BZip2;
using SharpCompress.Readers;

namespace WebWindowUI.Cef;

/// <summary>
/// CEF 运行时管理：首启动把钉版发行包（CEF 151.3.16，Chromium 151.0.7922.109）下载到
/// <c>%LocalAppData%\WebWindowUI\cef\&lt;版本&gt;\</c>，SHA256 校验后经 SharpCompress 解压出
/// Release/Resources，再 SetDllDirectory(Release) 使 DllImport("libcef") 可解析（libcef.dll 及
/// chrome_elf/libEGL 等依赖同目录）。子进程复进主 exe 时走同一缓存——命中即跳过下载（幂等）。
/// 下载包放 Root\_download（与版本目录平级），校验失败的残留下载下次启动重下。
/// </summary>
public static class CefRuntimeManager
{
    public const string Version = "151.3.16+gbe1e15d+chromium-151.0.7922.109";

    private const string FileName = "cef_binary_151.3.16+gbe1e15d+chromium-151.0.7922.109_windows64_minimal.tar.bz2";
    private const string DownloadUrl = "https://cef-builds.spotifycdn.com/" + FileName;
    private const string ExpectedSha256 = "5d07afa168feadb61292e37ad1e4e4ff15ad328f4957c05c55b6b6321ca49751";

    private static readonly string Root = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WebWindowUI", "cef");
    private static readonly string DownloadDir = Path.Combine(Root, "_download");
    private static readonly string DownloadPath = Path.Combine(DownloadDir, FileName);

    /// <summary>版本化缓存根：%LocalAppData%\WebWindowUI\cef\&lt;版本&gt;。</summary>
    public static string CacheRoot => Path.Combine(Root, Version);

    /// <summary>
    /// 自包含框架目录 = 解压后的 Release/（含合并进来的 Resources/* 的 icudtl.dat + *.pak + locales/）。
    /// CEF 151 on Windows 的 DIR_ASSETS（ICU 数据位置）从框架目录解析（libcef.dll 所在目录），
    /// 不理会 resources_dir_path——实测 resources_dir_path 指 Resources 时 cef_initialize 报
    /// "Invalid file descriptor to ICU data received"。故全部资源并进框架目录，resources/locales 也指向它。
    /// </summary>
    public static string ReleaseDir => Path.Combine(CacheRoot, "Release");

    /// <summary>cef_settings_t.cache_path——CEF 自己建目录，这里预建保证可写。</summary>
    public static string CacheDir => Path.Combine(CacheRoot, "cache");

    private static string MarkerPath => Path.Combine(CacheRoot, "version.txt");

    private static bool _ready;

    /// <summary>幂等（进程内一次）。首次调用下载/校验/解压并 SetDllDirectory；必须先于任何 cef_* 调用。</summary>
    public static void EnsureRuntime()
    {
        if (_ready)
            return;
        EnsureRuntimeCore();
        _ready = true;
    }

    private static void EnsureRuntimeCore()
    {
        if (!IsRuntimePresent())
        {
            Directory.CreateDirectory(DownloadDir);
            if (!IsDownloadValid())
            {
                Download();
                VerifyDownload();
            }
            Extract();
        }
        // 关键：libcef.dll 在 %LocalAppData% 缓存目录，须先 SetDllDirectory 才谈得上 DllImport("libcef")。
        SetDllDirectory(ReleaseDir);
    }

    private static bool IsRuntimePresent()
        => Directory.Exists(ReleaseDir)
           && File.Exists(MarkerPath)
           && File.ReadAllText(MarkerPath).Trim() == Version;

    private static bool IsDownloadValid()
        => File.Exists(DownloadPath)
           && ComputeSha256(DownloadPath).Equals(ExpectedSha256, StringComparison.OrdinalIgnoreCase);

    private static void Download()
    {
        Console.WriteLine($"[WebWindowUI] 首次使用 CEF 渲染器：下载 CEF {Version}（约 170MB）...");
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
            using var src = client.GetStreamAsync(DownloadUrl).GetAwaiter().GetResult();
            using var dst = File.Create(DownloadPath);
            src.CopyTo(dst);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"下载 CEF 运行时失败：{ex.Message}\n" +
                $"请检查网络后重试，或手动下载 {FileName} 放到 {DownloadDir}（须匹配 SHA256={ExpectedSha256}）。",
                ex);
        }
        Console.WriteLine($"[WebWindowUI] 下载完成（{new FileInfo(DownloadPath).Length / 1024 / 1024} MB），校验 SHA256...");
    }

    private static void VerifyDownload()
    {
        var actual = ComputeSha256(DownloadPath);
        if (actual.Equals(ExpectedSha256, StringComparison.OrdinalIgnoreCase))
            return;
        File.Delete(DownloadPath);
        throw new InvalidDataException(
            $"CEF 运行时 SHA256 校验失败：期望 {ExpectedSha256}，实际 {actual}。\n已删除损坏文件，下次启动自动重新下载。");
    }

    private static string ComputeSha256(string path)
    {
        using var fs = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(fs));
    }

    /// <summary>
    /// 解压 tar.bz2：Release/ 与 Resources/ 两棵子树都并进 tmpRoot\Release\（框架目录自包含，
    /// 见 <see cref="ReleaseDir"/>）。原子落位 CacheRoot + 写 marker。
    /// </summary>
    private static void Extract()
    {
        var tmpRoot = CacheRoot + ".tmp";
        if (Directory.Exists(tmpRoot))
            Directory.Delete(tmpRoot, true);
        Directory.CreateDirectory(tmpRoot);
        try
        {
            // SharpCompress 0.50.x：BZip2Stream 无 ctor（用 Create 工厂），TarArchive 无读侧 Open
            // → 解压层 + ReaderFactory 读 tar。tar 条目键恒为 '/' 分隔。
            using var src = File.OpenRead(DownloadPath);
            using var bz2 = BZip2Stream.Create(src, CompressionMode.Decompress, decompressConcatenated: false);
            using var reader = ReaderFactory.OpenReader(bz2, new ReaderOptions());
            while (reader.MoveToNextEntry())
            {
                var key = reader.Entry.Key;
                if (key is null || reader.Entry.IsDirectory)
                    continue;
                var slash = key.IndexOf('/');
                if (slash < 0)
                    continue;
                var rel = key[(slash + 1)..]; // "Release/libcef.dll" / "Resources/icudtl.dat"
                string? sub;
                if (rel.StartsWith("Release/", StringComparison.Ordinal))
                    sub = rel["Release/".Length..];
                else if (rel.StartsWith("Resources/", StringComparison.Ordinal))
                    sub = rel["Resources/".Length..]; // 资源并进框架目录（CEF 151 从框架目录找 ICU/资源）
                else
                    continue;
                var dest = Path.Combine(tmpRoot, "Release", sub.Replace("/", Path.DirectorySeparatorChar.ToString()));
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                using var entryStream = reader.OpenEntryStream();
                using var dst = File.Create(dest);
                entryStream.CopyTo(dst);
            }
            if (Directory.Exists(CacheRoot)) // 上次解压失败残留 → 清掉再原子落位
                Directory.Delete(CacheRoot, true);
            Directory.Move(tmpRoot, CacheRoot);
            File.WriteAllText(MarkerPath, Version);
        }
        finally
        {
            if (Directory.Exists(tmpRoot))
                Directory.Delete(tmpRoot, true);
        }
        Console.WriteLine($"[WebWindowUI] CEF 运行时就绪：{CacheRoot}");
    }

    /// <summary>把 Release 目录加进 DLL 搜索路径，DllImport("libcef") 才能解析到缓存里的 libcef.dll 及其依赖。</summary>
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetDllDirectory(string? lpPathName);
}
