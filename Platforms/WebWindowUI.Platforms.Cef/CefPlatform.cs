using WebWindowUI.Core;
using WebWindowUI.Natives.Windows;
using Xilium.CefGlue;

namespace WebWindowUI.Cef;

/// <summary>
/// CEF 平台实现（Windows：CefGlue 托管包装 + 裸 Win32 子窗口 + 启动自动下载运行时）。
/// 与 WebWindowUI.Platforms.Windows 互斥：入口在 UseCEF=true 时改引本包（WWUIPlatform 必为 Windows）。
///
/// 消息循环：单线程模式（multi_threaded_message_loop=false）→ CEF UI 线程 == 主线程。
/// RunMessageLoop 用 CefRuntime.RunMessageLoop()（CEF 内部完整消息环：泵 Windows 消息 + CEF 任务，
/// 隐式处理 WM_RUN 隐藏窗调度与末窗 WM_QUIT 退出），返回后同线程 CefRuntime.Shutdown()。
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

        // 单线程 CEF 消息循环：CefRuntime.RunMessageLoop 内部泵 Windows 消息 + CEF 任务，
        // 末窗关闭（WM_QUIT）后返回。
        CefRuntime.RunMessageLoop();

        // 同线程（UI 线程）关停 CEF。Shutdown 会等剩余浏览器上下文销毁，必须最后调用。
        CefRuntime.Shutdown();
    }

    private static void InstallMessageLoopSynchronizationContext()
    {
        Win32.SetMarshalMessageHandler(HandleMarshalMessage);
        var marshalHwnd = Win32.GetOrCreateMarshalWindow("CefMarshalWindow");
        MessageLoopSynchronizationContext.Initialize(marshalHwnd);
        SynchronizationContext.SetSynchronizationContext(MessageLoopSynchronizationContext.Instance);
    }

    private static IntPtr? HandleMarshalMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == MessageLoopSynchronizationContext.WM_RUN)
        {
            MessageLoopSynchronizationContext.Instance.RunQueued();
            return IntPtr.Zero;
        }
        return null;
    }
}
