using System.Runtime.InteropServices;
using AppKit;
using Foundation;
using WebWindowUI.Platforms.MacOS;

namespace WebWindowUI.Tests.Macos;

/// <summary>
/// macOS 端到端测试入口：独立可执行程序（自带 Main，不走 dotnet test）。主队列与 WKWebView 回调都绑定
/// 主线程，testhost 让不出主线程 → Exe 的 Main 即主线程，裸 CFRunLoopRunInMode 泵排干主队列顺次跑全部场景。
/// TerminateGuard 吞掉「最后窗口关闭 → NSApplication.Terminate」（测试进程 Main 返回即退出）。
/// </summary>
public static class MacOSTestProgram
{
    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern nint CFRunLoopRunInMode(IntPtr mode, double seconds, bool returnAfterSourceHandled);

    /// <summary>
    /// 强引用持有 app delegate（.NET 绑定不 retain ObjC delegate，被 GC 会静默失效）。
    /// </summary>
    private static TerminateGuard? _terminateGuard;

    public static int Main(string[] args)
    {
        // 强制平台程序集加载 → 其 [ModuleInitializer] 里 new MacOSPlatform()：NSApplication.Init + SC 注册
        // （主线程，Assembly.Load 在 typeof 求值时完成）。
        _ = typeof(MacOSPlatform);

        // 吞掉"最后一个窗口关闭 → NSApplication.Terminate"：测试进程用 Main 返回退出，不走 App 终止流程。
        _terminateGuard = new TerminateGuard();
        NSApplication.SharedApplication.Delegate = _terminateGuard;

        var runner = new MacOSTestRunner();
        MacOSBridgeSuite.Register(runner);
        MacOSMessageLoopSynchronizationContext.Instance.Post(_ => _ = runner.RunAllAsync(), null);

        // 主线程泵：排干主队列（SC 的 DispatchAsync 唤醒路径）+ 派发 WKWebView 事件（导航/消息/scheme/JS 求值）。
        var modeStr = new NSString("kCFRunLoopDefaultMode"); // 强引用保住 CFString（Handle 别名不 retain）
        IntPtr mode = modeStr.Handle;
        while (!runner.Completed)
            CFRunLoopRunInMode(mode, 0.05, false);

        Console.WriteLine(runner.FailedCount == 0 ? "ALL PASS" : $"FAILURES: {runner.FailedCount}");
        return runner.FailedCount == 0 ? 0 : 1;
    }

    private sealed class TerminateGuard : NSApplicationDelegate
    {
        public override NSApplicationTerminateReply ApplicationShouldTerminate(NSApplication sender)
            => NSApplicationTerminateReply.Cancel;
    }
}
