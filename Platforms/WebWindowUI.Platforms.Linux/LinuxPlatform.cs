using WebWindowUI.Core;

namespace WebWindowUI.Linux;

/// <summary>
/// Linux 平台实现：GTK3 宿主 + libwebkit2gtk-4.1（webkit2gtk-4.1 是 GTK3 端口；WebKit/GTK 均为手写
/// P/Invoke，见 Native/WebKit2Native.cs + Native/GtkNative.cs）。用 GLib.MainLoop 跑主循环（不用
/// Gtk.Application），契合本框架「创建窗口 → Show → 再 RunMessageLoop」的模型，也避开 Gtk.Application
/// 的 D-Bus 唯一实例限制。
/// </summary>
public sealed class LinuxPlatform : IWebWindowPlatform
{
    private static MainLoop? _mainLoop;

    public LinuxPlatform()
    {
        // GirCore 只发布 GLib-2.0 绑定（消息循环用），注册其 DllImport 解析器（把 "GLib" 解析到真实 soname），
        // 缺失时 GLib.MainLoop/MainContext 的 DllImport 直接 DllNotFoundException。须在创建任何窗口前调用。
        GLib.Module.Initialize();

        // 初始化手写 GTK3 绑定：gtk_init(null, null)，创建任何 GTK 控件前必须调用。
        GtkNative.Initialize();
        // 初始化手写 WebKit2 绑定：webkit_web_context_get_default 触发 WebKit 子系统注册。
        WebKit2Native.Initialize();
        LinuxMessageLoopSynchronizationContext.Initialize();
        SynchronizationContext.SetSynchronizationContext(LinuxMessageLoopSynchronizationContext.Instance);
    }

    public string Name => "Linux";

    public IWindowBackend CreateWindow(WebWindowOptions options)
        => LinuxWindow.Create(options);

    public void RunMessageLoop()
    {
        // 幂等兜底（覆盖未经过窗口创建直接调消息循环的宿主）
        LinuxMessageLoopSynchronizationContext.Initialize();
        SynchronizationContext.SetSynchronizationContext(LinuxMessageLoopSynchronizationContext.Instance);

        var loop = MainLoop.New(null, false); // null = 默认 MainContext（与 SyncContext.Post 用的同一上下文）
        _mainLoop = loop;
        loop.RunWithSynchronizationContext(); // 最后一个窗口关闭 → Quit() → 返回
        _mainLoop = null;
    }

    /// <summary>最后一个窗口销毁时调用，退出主循环。</summary>
    internal static void QuitMainLoop() => _mainLoop?.Quit();
}
