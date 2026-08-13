using UniformTypeIdentifiers;
using WebWindowUI.Core;

namespace WebWindowUI.Platforms.MacOS;

/// <summary>
/// macOS 平台实现：WKWebView。用 NSApplication 跑主事件循环。
///
/// 盲写状态：net10.0-macos 无法在 Windows 上编译（需 Mac + macOS workload），本实现严格对齐
/// .NET macOS 绑定的已验证签名，编译与运行时行为需在 Mac 上最终确认（见 README 的平台说明）。
/// </summary>
public sealed class MacOSPlatform : IWebWindowPlatform
{
    /// <summary>
    /// 窗口注册表：跟踪已打开窗口，最后一个关闭时退出主事件循环（镜像 Linux 的 _windows）。
    /// </summary>
    private static readonly HashSet<MacOSWindow> _windows = [];

    /// <summary>
    /// 窗口注册（CreateWindow 时登记）。
    /// </summary>
    internal static void WindowOpen(MacOSWindow window)
    {
        lock (_windows)
            _windows.Add(window);
    }

    /// <summary>
    /// 窗口注销：最后一个窗口关闭时结束 NSApplication 主事件循环（Terminate → Run() 返回）。
    /// 回调发生在主线程（windowWillClose:），Terminate 调用安全。
    /// </summary>
    internal static void WindowClose(MacOSWindow window)
    {
        lock (_windows)
        {
            _windows.Remove(window);
            if (_windows.Count == 0)
                NSApplication.SharedApplication.Terminate(null);
        }
    }

    private static bool _nsApplicationInitialized;

    /// <summary>
    /// 初始化 NSApplication（幂等，静态标志守卫：Init 非幂等，第二次构造抛异常）并设置激活策略。
    /// </summary>
    public MacOSPlatform()
    {
        // NSApplication 初始化须在创建任何窗口前、且在主线程调用（与 Linux 版 WebKit.Module.Initialize 同角色）。
        // 平台会被构造两次（平台程序集自身的 [ModuleInitializer] + 应用侧注入的 bootstrap 各 new 一次，
        // 与 Windows/Linux 相同；那里 ctor 幂等无碍），而 NSApplication.Init() 不是幂等 API——
        // 第二次调用抛 InvalidOperationException，须以静态标志守卫。
        if (!_nsApplicationInitialized)
        {
            _nsApplicationInitialized = true;
            NSApplication.Init();
        }
        // 终端启动的进程默认不激活为前台 App；设为 Regular 让窗口进 Dock 并可正常激活/成为 key window。
        NSApplication.SharedApplication.ActivationPolicy = NSApplicationActivationPolicy.Regular;
        MacOSMessageLoopSynchronizationContext.Initialize();
        SynchronizationContext.SetSynchronizationContext(MacOSMessageLoopSynchronizationContext.Instance);
    }

    /// <summary>
    /// 平台名。
    /// </summary>
    public string Name => "macOS";

    /// <summary>
    /// 创建窗口后端并登记。
    /// </summary>
    /// <param name="options">窗口选项。</param>
    /// <returns>窗口后端。</returns>
    public IWindowBackend CreateWindow(WebWindowOptions options)
    {
        var window = MacOSWindow.Create(options.Title, options, options.Width, options.Height);
        WindowOpen(window);
        return window;
    }

    /// <summary>
    /// 运行主事件循环，直到最后一个窗口关闭（Terminate → Run() 返回）。
    /// </summary>
    public void RunMessageLoop()
    {
        // 幂等兜底（覆盖未经过平台构造直接调消息循环的宿主）
        MacOSMessageLoopSynchronizationContext.Initialize();
        SynchronizationContext.SetSynchronizationContext(MacOSMessageLoopSynchronizationContext.Instance);

        NSApplication.SharedApplication.Run(); // 最后一个窗口关闭 → Terminate() → 返回
    }

    /// <summary>
    /// 把动作 marshal 到 UI（主事件循环）线程同步执行：UI 线程直接运行；非 UI 线程经
    /// MacOSMessageLoopSynchronizationContext.Send 回 UI 线程并阻塞等待。
    /// </summary>
    public void RunOnUiThread(Action action)
        => MacOSMessageLoopSynchronizationContext.Instance.Send(_ => action(), null);

    /// <summary>
    /// 当前线程是否 UI（主事件循环）线程。
    /// </summary>
    /// <returns>是否 UI 线程。</returns>
    public bool IsUiThread()
        => Environment.CurrentManagedThreadId == MacOSMessageLoopSynchronizationContext.UiThreadId;

    /// <summary>
    /// 系统弹窗（NSAlert，主线程调用）。
    /// </summary>
    public void ShowMessageBox(string title, string message, bool error)
    {
        var alert = new NSAlert
        {
            MessageText = title,
            InformativeText = message,
            AlertStyle = error ? NSAlertStyle.Critical : NSAlertStyle.Informational,
        };
        alert.RunModal();
    }

    /// <summary>
    /// 文件选择对话框（NSOpenPanel）。返回 null = 取消。
    /// filter 为 Windows 格式，macOS 暂不支持（忽略）。
    /// </summary>
    public string[]? OpenFileDialog(string title, string filter, string? initialDirectory = null, bool fileMustExist = true, bool allowMultiSelect = true)
    {
        var panel = NSOpenPanel.OpenPanel;
        panel.Title = title;
        panel.CanChooseFiles = true;
        panel.CanChooseDirectories = false;
        panel.AllowsMultipleSelection = allowMultiSelect;
        if (initialDirectory is not null)
            panel.DirectoryUrl = NSUrl.FromFilename(initialDirectory);
        if (panel.RunModal() != 1) // NSModalResponseOK = 1
            return null;
        var urls = panel.Urls;
        var result = new string[urls.Length];
        for (int i = 0; i < urls.Length; i++)
            result[i] = urls[i].Path!;
        return result;
    }

    /// <summary>
    /// 保存对话框（NSSavePanel）。返回 null = 取消。
    /// filter 为 Windows 格式，macOS 暂不支持（忽略）。
    /// </summary>
    public string? SaveFileDialog(string title, string filter, string? defaultFileName = null, string? defaultExt = null)
    {
        var panel = NSSavePanel.SavePanel;
        panel.Title = title;
        if (defaultFileName is not null)
            panel.NameFieldStringValue = defaultFileName;
        if (defaultExt is not null)
            panel.AllowedContentTypes = new[] { UTType.CreateFromExtension(defaultExt)! };
        if (panel.RunModal() != 1) // NSModalResponseOK = 1
            return null;
        return panel.Url?.Path;
    }
}
