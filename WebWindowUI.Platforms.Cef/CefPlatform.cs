namespace WebWindowUI.Cef;

/// <summary>
/// CEF 平台实现（Windows：手写 cef.h C API 绑定 + 裸 Win32 子窗口 + 启动自动下载运行时）。
/// 与 WebWindowUI.Platforms.Windows 互斥：入口在 UseCEF=true 时改引本包（WWUIPlatform 必为 Windows）。
///
/// 消息循环：单线程模式（multi_threaded_message_loop=false）→ CEF UI 线程 == 主线程。
/// RunMessageLoop 用 Win32 GetMessage 环 + 每次 dispatch 后 cef_do_message_loop_work()，
/// WM_QUIT（末窗关闭）后同线程 cef_shutdown()（必须在 UI 线程、进程退出前）。
/// </summary>
public sealed class CefPlatform : IWebWindowPlatform
{
    public CefPlatform()
    {
        // 尽早安装 SynchronizationContext（平台是懒加载单例，首次创建窗口即触发，
        // 一定先于任何窗口 Show()/浏览器创建）：否则 async 延续会恢复在
        // 线程池线程上，跨线程 marshal 下行/关窗也会失去目标线程。
        InstallMessageLoopSynchronizationContext();
    }

    public string Name => "Cef";

    public IWindowBackend CreateWindow(string title, WebWindowOptions options, int width, int height)
        => CefWindow.Create(title, options, width, height);

    public void RunMessageLoop()
    {
        // 隐藏消息窗口：所有 async 延续都通过它调度回 UI 线程。
        // 构造里已装过一次，这里是幂等兜底（覆盖未经过窗口创建直接调消息循环的宿主）。
        InstallMessageLoopSynchronizationContext();

        // 单线程 CEF 消息循环：Win32 环 + cef_do_message_loop_work，末窗关闭（WM_QUIT）后返回
        CefNative.RunMessageLoop();

        // 同线程（UI 线程）关停 CEF。cef_shutdown 会等待剩余浏览器上下文销毁，必须最后调用。
        CefNative.cef_shutdown();
    }

    private static void InstallMessageLoopSynchronizationContext()
    {
        IntPtr marshalHwnd = Win32.GetOrCreateMarshalWindow();
        MessageLoopSynchronizationContext.Initialize(marshalHwnd);
        SynchronizationContext.SetSynchronizationContext(MessageLoopSynchronizationContext.Instance);
    }
}
