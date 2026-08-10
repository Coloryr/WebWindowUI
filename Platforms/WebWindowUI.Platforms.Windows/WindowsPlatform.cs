using WebWindowUI.Core;
using WebWindowUI.Natives.Windows;

namespace WebWindowUI.Windows;

/// <summary>
/// Windows 平台的实现：WebView2 + Win32 消息循环。
/// 消息窗口、SynchronizationContext 的初始化都封装在这里，调用方无需接触 Win32。
/// </summary>
public sealed class WindowsPlatform : IWebWindowPlatform
{
    public WindowsPlatform()
    {
        // 尽早安装 SynchronizationContext（平台是懒加载单例，首次创建窗口即触发，
        // 一定先于任何窗口 Show()/InitWebViewAsync()）：否则 async 延续会恢复在
        // 线程池线程上，访问 CoreWebView2Controller 会抛 "can only be accessed from the UI thread"。
        InstallMessageLoopSynchronizationContext();
    }

    public string Name => "Windows";

    public IWindowBackend CreateWindow(string title, WebWindowOptions options, int width, int height)
        => WindowsWindow.Create(title, options, width, height);

    public void RunMessageLoop()
    {
        // 隐藏消息窗口：所有 async 延续都通过它调度回 UI 线程。
        // 构造里已装过一次，这里是幂等兜底（覆盖未经过窗口创建直接调消息循环的宿主）。
        InstallMessageLoopSynchronizationContext();

        // Win32 消息循环，收到 WM_QUIT（最后一个窗口关闭）后返回
        Win32.MessageLoop();
    }

    private static void InstallMessageLoopSynchronizationContext()
    {
        Win32.SetMarshalMessageHandler(HandleMarshalMessage);
        var marshalHwnd = Win32.GetOrCreateMarshalWindow("WebView2MarshalWindow");
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
