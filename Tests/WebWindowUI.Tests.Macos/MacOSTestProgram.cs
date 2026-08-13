using System.Runtime.InteropServices;
using AppKit;
using Foundation;
using WebWindowUI.Platforms.MacOS;

namespace WebWindowUI.Tests.Macos;

/// <summary>
/// macOS 端到端测试入口：独立可执行程序（自带 Main，不走 dotnet test/testhost）。
///
/// 为什么必须是一个自带 Main 的可执行程序（durable）：
///  - 主队列（<see cref="MacOSMessageLoopSynchronizationContext.Post"/> 的唤醒路径
///    DispatchQueue.MainQueue）与 WKWebView 回调（导航/script message/scheme/JS 求值）都绑定进程
///    主线程。后台泵线程排不干主队列、收不到导航（/tmp/macpumptest 实测 mainQueueFired=0 navFired=0）。
///  - testhost 占着进程主线程跑 VSTest 消息循环，无法让给 NSApplication/主 run loop。
///  因此本工程是 Exe，Main 就是主线程，用裸 CFRunLoopRunInMode 泵排干主队列并派发 WKWebView 事件，
///  顺次跑全部桥测试场景。
///
/// 终止保护：<see cref="MacOSPlatform.WindowClose"/> 在最后一个窗口关闭时调 NSApplication.Terminate
/// （生产语义：最后一个窗口关闭退出主事件循环）。测试进程不需要走这套终止流程（Main 返回即退出）——
/// 装一个 applicationShouldTerminate: 返回 Cancel 的 app delegate 把 Terminate 吞掉。
/// </summary>
public static class MacOSTestProgram
{
    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern nint CFRunLoopRunInMode(IntPtr mode, double seconds, bool returnAfterSourceHandled);

    /// <summary>强引用持有 app delegate（.NET 绑定不 retain ObjC delegate，被 GC 会静默失效）。</summary>
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
