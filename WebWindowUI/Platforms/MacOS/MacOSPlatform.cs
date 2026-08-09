#if MACOS
using AppKit;

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

    public IWindowBackend CreateWindow(string title, WebWindowOptions options, int width, int height)
        => MacOSWindow.Create(title, options, width, height);

    public void RunMessageLoop()
    {
        // 幂等兜底（覆盖未经过平台构造直接调消息循环的宿主）
        MacOSMessageLoopSynchronizationContext.Initialize();
        SynchronizationContext.SetSynchronizationContext(MacOSMessageLoopSynchronizationContext.Instance);

        NSApplication.SharedApplication.Run(); // 最后一个窗口关闭 → Terminate() → 返回
    }
}
#endif
