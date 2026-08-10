using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace WebWindowUI.Demo.ImageGallery;

/// <summary>
/// 图片画廊模型：后端扫描磁盘图片目录并「把图片字节发给前端」；前端双模式上传 →
/// 命令保存到磁盘（文件上传存储）→ 列表即时刷新；删除/刷新命令操作磁盘后同步列表。
/// 上传两模式：字节上传（前端读成 byte[] 回传）/ 路径上传（系统原生文件选择器，后端自读源文件）。
/// 演示 typed repeated List&lt;ImageItemModel&gt; 元素携带 byte[]（blob 传输）。
/// </summary>
public partial class ImageGalleryModel : WebWindowModel
{
    private static readonly HashSet<string> ImageExts = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp", ".ico", ".svg",
    };

    private readonly string _storeDir;

    /// <summary>图片列表：get-only ObservableCollection（免 [ObservableProperty]），原地增删自动推前端。</summary>
    public ObservableCollection<ImageItemModel> Items { get; } = new();

    /// <summary>状态提示（如「已保存 xxx.png（12 KB）」）。</summary>
    [ObservableProperty]
    public partial string Status { get; set; } = "就绪";

    /// <summary>存储目录（%LocalAppData%\WebWindowUI.Demo.ImageGallery\images）。</summary>
    [ObservableProperty]
    public partial string StoreDir { get; set; } = "";

    public ImageGalleryModel()
    {
        string dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WebWindowUI.Demo.ImageGallery");
        _storeDir = Path.Combine(dir, "images");
        Directory.CreateDirectory(_storeDir);
        StoreDir = _storeDir;
        foreach (ImageItemModel item in LoadItems())
            Items.Add(item);
        Status = $"已加载 {Items.Count} 张图片";
    }

    /// <summary>
    /// 模式一 · 字节上传：前端把文件读成 byte[] 经命令参数回传（{ Name, Data } 对象 → 反射重建 UploadFile），
    /// .NET 把字节写盘（同名自动加序号）后插到列表头部，补丁差量推送前端。
    /// </summary>
    [RelayCommand]
    public void UploadBytes(UploadFile file)
    {
        if (file is null || file.Data is null || file.Data.Length == 0)
        {
            Status = "未收到字节数据";
            return;
        }
        StoreBytes(file.Name, file.Data, file.Path);
    }

    /// <summary>
    /// 模式二 · 路径上传（系统原生文件选择器）：前端点「路径上传」→ 本命令在 .NET 侧弹系统原生
    /// OpenFileDialog（Windows）选文件 → 直接读源文件拷入存储目录（源文件不动）。非 Windows 平台
    /// 无原生对话框（状态提示）。
    /// </summary>
    [RelayCommand]
    public void PickFile()
    {
#if WINDOWS
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择要上传的图片",
            Filter = "图片文件|*.png;*.jpg;*.jpeg;*.gif;*.webp;*.bmp;*.ico;*.svg|所有文件|*.*",
            CheckFileExists = true,
            Multiselect = false,
        };
        if (dialog.ShowDialog() != true)
        {
            Status = "已取消选择";
            return;
        }
        string src = dialog.FileName;
        if (!File.Exists(src))
        {
            Status = $"源文件不存在：{src}";
            return;
        }
        StoreBytes(Path.GetFileName(src), File.ReadAllBytes(src), src);
#else
        Status = "原生文件选择仅支持 Windows";
#endif
    }

    /// <summary>两种上传模式共用的落盘逻辑：字节写盘 → 新条目插列表头 → 状态含源路径。</summary>
    private void StoreBytes(string name, byte[] data, string srcPath)
    {
        try
        {
            string safe = SanitizeFileName(name);
            string path = Path.Combine(_storeDir, safe);
            int n = 1;
            while (File.Exists(path))
            {
                string stem = Path.GetFileNameWithoutExtension(safe);
                string ext = Path.GetExtension(safe);
                path = Path.Combine(_storeDir, $"{stem} ({n++}){ext}");
            }
            File.WriteAllBytes(path, data);
            Items.Insert(0, MakeItem(path));
            string src = string.IsNullOrWhiteSpace(srcPath) ? "" : $"（来自 {srcPath}）";
            Status = $"已保存 {Path.GetFileName(path)}（{data.Length / 1024} KB）{src}";
        }
        catch (Exception e)
        {
            Status = $"保存失败：{e.Message}";
        }
    }

    /// <summary>删除图片：前端传列表下标 → 命令 → 删磁盘文件 + 移除条目（补丁差量推送）。</summary>
    [RelayCommand]
    public void Remove(int index)
    {
        if (index < 0 || index >= Items.Count)
            return;
        ImageItemModel item = Items[index];
        try
        {
            File.Delete(Path.Combine(_storeDir, item.Name));
        }
        catch
        {
            // 磁盘文件缺失/占用：仍移除列表条目（内存与磁盘最终一致）
        }
        Items.RemoveAt(index);
        Status = $"已删除 {item.Name}";
    }

    /// <summary>重新扫描存储目录（如外部放入图片后点刷新，后端重新发送图片）。</summary>
    [RelayCommand]
    public void Refresh()
    {
        Items.Clear();
        foreach (ImageItemModel item in LoadItems())
            Items.Add(item);
        Status = $"已扫描 {Items.Count} 张图片";
    }

    private IEnumerable<ImageItemModel> LoadItems()
    {
        if (!Directory.Exists(_storeDir))
            yield break;
        foreach (string f in Directory.GetFiles(_storeDir).OrderByDescending(File.GetLastWriteTime))
        {
            if (!ImageExts.Contains(Path.GetExtension(f)))
                continue;
            yield return MakeItem(f);
        }
    }

    private static ImageItemModel MakeItem(string path)
    {
        return new ImageItemModel
        {
            Name = Path.GetFileName(path),
            Size = new FileInfo(path).Length,
            Modified = File.GetLastWriteTime(path).ToString("yyyy-MM-dd HH:mm"),
            Path = path,
            Data = File.ReadAllBytes(path),
        };
    }

    /// <summary>文件名清洗：去掉路径分隔符与非法字符，防止写入越出存储目录。</summary>
    private static string SanitizeFileName(string name)
    {
        string s = name.Trim();
        foreach (char c in Path.GetInvalidFileNameChars())
            s = s.Replace(c, '_');
        if (s.Length == 0)
            s = "image.png";
        return s;
    }
}
