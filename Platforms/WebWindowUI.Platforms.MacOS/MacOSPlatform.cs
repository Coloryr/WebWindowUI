using WebWindowUI.Core;

namespace WebWindowUI.MacOS;

/// <summary>
/// macOS 平台实现：WKWebView。用 NSApplication 跑主事件循环。
///
/// 盲写状态：net10.0-macos 无法在 Windows 上编译（需 Mac + macOS workload），本实现严格对齐
/// .NET macOS 绑定的已验证签名，编译与运行时行为需在 Mac 上最终确认（见 README 的平台说明）。
/// </summary>
public sealed class MacOSPlatform : IWebWindowPlatform
{
    public MacOSPlatform()
    {
        // NSApplication 初始化须在创建任何窗口前、且在主线程调用（与 Linux 版 WebKit.Module.Initialize 同角色）。
        NSApplication.Init();
        // 终端启动的进程默认不激活为前台 App；设为 Regular 让窗口进 Dock 并可正常激活/成为 key window。
        NSApplication.SharedApplication.ActivationPolicy = NSApplicationActivationPolicy.Regular;
        MacOSMessageLoopSynchronizationContext.Initialize();
        SynchronizationContext.SetSynchronizationContext(MacOSMessageLoopSynchronizationContext.Instance);
    }

    public string Name => "macOS";

    public IWindowBackend CreateWindow(WebWindowOptions options)
        => MacOSWindow.Create(options.Title, options, options.Width, options.Height);

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
            result[i] = urls[i].Path;
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
            panel.AllowedFileTypes = new[] { defaultExt };
        if (panel.RunModal() != 1) // NSModalResponseOK = 1
            return null;
        return panel.Url?.Path;
    }
}
