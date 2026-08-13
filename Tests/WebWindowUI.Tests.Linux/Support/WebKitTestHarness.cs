using System.Diagnostics;
using WebWindowUI.Core;
using WebWindowUI.Sample;

namespace WebWindowUI.Tests.Linux.Support;

/// <summary>
/// 测试用窗口宿主：真实 libwebkit2gtk-4.1 + 真实构建产物 wwwroot（经 ProjectReference 传递复制到测试 bin）。
/// 无头模式（<see cref="WebWindowOptions.Headless"/>）：GTK 窗口永不 show，但导航/DOM/JS/消息通道照常，
/// 测试全程不出现在屏幕与任务栏。
/// </summary>
internal sealed class TestWindow : WebWindow
{
    public TestWindow(string windowPath, string title)
        : base(windowPath, title, new WebWindowOptions { Headless = true }, width: 720, height: 480)
    {
    }
}

/// <summary>
/// 真 WebKitGTK 端到端测试宿主（Linux 版，对应 Windows 的 WebView2TestHarness）。
///
/// 流程：模型先挂到窗口 → Show()（唯一入口，触发加载；无头模式不显示窗口）
/// → 等 NavigationCompleted 镜像事件（页面已导航完成）→ 等页面桥接（window.__model）就绪 →
/// 执行测试体。全部在 GtkPump 泵线程（GLib 主循环线程）内跑。
/// </summary>
internal static class WebKitTestHarness
{
    public static Task RunMainWindowAsync(MainWindowModel model, Func<TestWindow, Task> body, TimeSpan? timeout = null)
        => RunWindowAsync("main", "测试", model, body, timeout);

    public static Task RunWindowAsync(string windowPath, string title, WebWindowModel model, Func<TestWindow, Task> body, TimeSpan? timeout = null)
    {
        TimeSpan t = timeout ?? TimeSpan.FromSeconds(60);
        return GtkPump.Instance.RunAsync(async () =>
        {
            var win = new TestWindow(windowPath, title);
            try
            {
                win.Model = model; // 必须在 Show() 前设置，快照才含初始值

                var nav = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                win.NavigationCompleted += () => nav.TrySetResult(true);

                win.Show(); // 无头：只加载页面，窗口永不显示
                await nav.Task.WaitAsync(t);

                await WaitBridgeReadyAsync(win, t);
                await body(win);
            }
            finally
            {
                win.Close();
            }
        });
    }

    /// <summary>
    /// 两窗口共享同一模型实例（演示「一个 model 给多个窗口用」）；窗口 A 先 Show 等导航，
    /// 再 Show B，各自收初始快照。
    /// </summary>
    public static Task RunTwoWindowsSharedModelAsync(MultiWindowModel model, Func<TestWindow, TestWindow, Task> body, TimeSpan? timeout = null)
        => RunTwoWindowsSharedModelAsync("multi", "共享A", "共享B", model, body, timeout);

    /// <summary>
    /// 双窗口共享模型宿主（泛型）：任意模型任意页面路径，验证跨窗口广播（含元素级 ElementSet 广播）。
    /// </summary>
    public static Task RunTwoWindowsSharedModelAsync<T>(string windowPath, string titleA, string titleB, T model, Func<TestWindow, TestWindow, Task> body, TimeSpan? timeout = null)
        where T : WebWindowModel
        => RunTwoWindowsSharedModelCoreAsync(windowPath, titleA, titleB, model, body, timeout);

    private static async Task RunTwoWindowsSharedModelCoreAsync<T>(string windowPath, string titleA, string titleB, T model, Func<TestWindow, TestWindow, Task> body, TimeSpan? timeout = null)
        where T : WebWindowModel
    {
        TimeSpan t = timeout ?? TimeSpan.FromSeconds(60);
        await GtkPump.Instance.RunAsync(async () =>
        {
            var winA = new TestWindow(windowPath, titleA);
            var winB = new TestWindow(windowPath, titleB);
            try
            {
                winA.Model = model;
                winB.Model = model;

                var navA = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var navB = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                winA.NavigationCompleted += () => navA.TrySetResult(true);
                winB.NavigationCompleted += () => navB.TrySetResult(true);

                winA.Show(); // 无头：只加载页面，窗口永不显示
                await navA.Task.WaitAsync(t);
                winB.Show();
                await navB.Task.WaitAsync(t);

                await WaitBridgeReadyAsync(winA, t);
                await WaitBridgeReadyAsync(winB, t);

                await body(winA, winB);
            }
            finally
            {
                winA.Close();
                winB.Close();
            }
        });
    }

    /// <summary>
    /// 轮询直到页面脚本桥接（window.__model）就绪。
    /// </summary>
    private static async Task WaitBridgeReadyAsync(TestWindow win, TimeSpan timeout)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < timeout)
        {
            try
            {
                // typeof undefined → "undefined"；桥接后 → "object"（reactive proxy）
                var result = await win.ExecuteScriptAsync("typeof window.__model");
                if (result != "\"undefined\"")
                    return;
            }
            catch
            {
                // 页面可能尚未导航完成，忽略并重试
            }
            await Task.Delay(50);
        }
        throw new TimeoutException("页面桥接未就绪（window.__model 未出现）");
    }

    /// <summary>
    /// 轮询 JS 表达式直到为 true（ExecuteScriptAsync 对布尔返回 "true"/"false"）。
    /// </summary>
    public static async Task WaitJsAsync(TestWindow win, string jsExpr, string description, TimeSpan? timeout = null)
    {
        TimeSpan t = timeout ?? TimeSpan.FromSeconds(20);
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < t)
        {
            try
            {
                if (await win.ExecuteScriptAsync(jsExpr) == "true")
                    return;
            }
            catch
            {
            }
            await Task.Delay(50);
        }
        throw new TimeoutException($"{description} 超时：{jsExpr} 未在 {t.TotalSeconds}s 内为 true");
    }

    /// <summary>
    /// 轮询 .NET 侧条件（在泵线程内执行，直接读模型属性）。
    /// </summary>
    public static async Task WaitDotNetAsync(Func<bool> condition, string description, TimeSpan? timeout = null)
    {
        TimeSpan t = timeout ?? TimeSpan.FromSeconds(20);
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < t)
        {
            if (condition())
                return;
            await Task.Delay(50);
        }
        throw new TimeoutException($"{description} 超时");
    }
}
