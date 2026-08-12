using System.Runtime.InteropServices;
using System.Security.Cryptography;
using SharpCompress.Readers;

namespace WebWindowUI.Cef;

/// <summary>
/// CEF 运行时管理：首启动把钉版发行包下载到 <c>%LocalAppData%\WebWindowUI\cef\&lt;版本&gt;\</c>，
/// SHA256 校验后经 SharpCompress 解压出框架目录，再 SetDllDirectory(Release) 使 CefGlue 的
/// DllImport("libcef") 可解析（libcef.dll 及 chrome_elf/libEGL 等依赖同目录）。子进程复进主 exe 时走
/// 同一缓存——命中即跳过下载（幂等）。下载包放 Root\_download（与版本目录平级），校验失败的残留下载
/// 下次启动重下。
///
/// **下载源 = NuGet 发行包而非 spotifycdn**（durable：cef-builds.spotifycdn.com 在本网络实测
/// ~15KB/s，下载 164MB 要几小时；NuGet CDN 实测 ~16MB/s，10 秒下完）。用
/// <c>chromiumembeddedframework.runtime.win-x64 150.0.11</c>（CefSharp 官方转发、CefGlue 150 的
/// Windows redist 同源）——它是 zip（nupkg），nupkg 版本不可变、SHA256 可钉死。解压布局：
/// <c>runtimes/win-x64/native/*</c> → Release/、<c>CEF/win-x64/locales/*</c> → Release/locales/，
/// 其余（nuspec/props/签名）跳过。解压产物与旧 tar.bz2（Release+Resources 合并）等价——框架目录自包含。
///
/// **版本对齐（durable）：CefGlue 150.7871.115 对 libcef 做 API hash 硬校验**
/// （CefRuntime.Load → CheckVersionByApiHash 比对 CEF_API_HASH_PLATFORM_WIN=71146b43…，不匹配抛
/// CefVersionMismatchException）——<see cref="Version"/> 必须与 CefGlue 包的 150.0.11 一致，不能沿用
/// 旧 CEF 151.3.16（hash 9bfd64bd… 不同）。
/// </summary>
public static class CefRuntimeManager
{
    // 与 CefGlue.Next.Core 150.7871.115 的 cef-version.json 一致：150.0.11+gb887805+chromium-150.0.7871.115
    public const string Version = "150.0.11+gb887805+chromium-150.0.7871.115";

    private const string FileName = "chromiumembeddedframework.runtime.win-x64.150.0.11.nupkg";
    private const string DownloadUrl =
        "https://api.nuget.org/v3-flatcontainer/chromiumembeddedframework.runtime.win-x64/150.0.11/" + FileName;
    private const string ExpectedSha256 = "9a455e5595a70b9e76d664f16996041584d6e38fbff738a18d60bda1f8948053";

    private static readonly string Root = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WebWindowUI", "cef");
    private static readonly string DownloadDir = Path.Combine(Root, "_download");
    private static readonly string DownloadPath = Path.Combine(DownloadDir, FileName);

    /// <summary>
    /// 版本化缓存根：%LocalAppData%\WebWindowUI\cef\&lt;版本&gt;。
    /// </summary>
    public static string CacheRoot => Path.Combine(Root, Version);

    /// <summary>
    /// 自包含框架目录 = 解压后的 Release/（含合并进来的 locales/*）。CEF on Windows 的 DIR_ASSETS
    /// （ICU 数据）从框架目录解析（libcef.dll 所在目录），不理会 resources_dir_path——实测
    /// resources_dir_path 指 Resources 时 cef_initialize 报 "Invalid file descriptor to ICU data received"。
    /// 故全部资源并进框架目录，resources/locales 也指向它。
    /// </summary>
    public static string ReleaseDir => Path.Combine(CacheRoot, "Release");

    /// <summary>
    /// cef_settings_t.cache_path——CEF 自己建目录，这里预建保证可写。
    /// </summary>
    public static string CacheDir => Path.Combine(CacheRoot, "cache");

    private static string MarkerPath => Path.Combine(CacheRoot, "version.txt");

    private static bool _ready;

    /// <summary>
    /// 幂等（进程内一次）。首次调用下载/校验/解压并 SetDllDirectory；必须先于任何 cef_* 调用。
    /// </summary>
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
        Console.WriteLine($"[WebWindowUI] 首次使用 CEF 渲染器：下载 CEF {Version}（约 175MB）...");
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
    /// 解压 nupkg（zip）：runtimes/win-x64/native/* → Release/、CEF/win-x64/locales/* → Release/locales/，
    /// 其余条目跳过（框架目录自包含，见 <see cref="ReleaseDir"/>）。原子落位 CacheRoot + 写 marker。
    /// </summary>
    private static void Extract()
    {
        var tmpRoot = CacheRoot + ".tmp";
        if (Directory.Exists(tmpRoot))
            Directory.Delete(tmpRoot, true);
        Directory.CreateDirectory(tmpRoot);
        try
        {
            using var src = File.OpenRead(DownloadPath);
            using var reader = ReaderFactory.OpenReader(src, new ReaderOptions());
            while (reader.MoveToNextEntry())
            {
                var key = reader.Entry.Key;
                if (key is null || reader.Entry.IsDirectory)
                    continue;
                string? sub = null;
                const string nativePrefix = "runtimes/win-x64/native/";
                const string localesPrefix = "CEF/win-x64/locales/";
                if (key.StartsWith(nativePrefix, StringComparison.Ordinal))
                    sub = key[nativePrefix.Length..];
                else if (key.StartsWith(localesPrefix, StringComparison.Ordinal))
                    sub = "locales/" + key[localesPrefix.Length..];
                if (sub is null)
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

    /// <summary>
    /// 把 Release 目录加进 DLL 搜索路径，DllImport("libcef") 才能解析到缓存里的 libcef.dll 及其依赖。
    /// </summary>
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetDllDirectory(string? lpPathName);
}
