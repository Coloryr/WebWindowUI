using System.Diagnostics;
using WebWindowUI.Core;
using WebWindowUI.Sample;

namespace WebWindowUI.Tests.Macos;

/// <summary>
/// 测试用窗口控制器：经 <see cref="WebWindowPlatform.Current.CreateWindow"/> 建窗（真实 WKWebView +
/// 真实构建产物 wwwroot，经 ProjectReference 传递复制到测试 bin）。无头模式
/// （<see cref="WebWindowOptions.Headless"/>）：窗口永不显示（macOS 跳过 MakeKeyAndOrderFront），
/// 但导航/DOM/JS/消息通道照常，测试全程不出现在屏幕与 Dock。
/// </summary>
internal sealed class TestWindow
{
    /// <summary>
    /// 框架窗口（构造即创建；Model 绑定与 Show 由宿主负责）。
    /// </summary>
    public WebWindow Window { get; }

    /// <summary>
    /// 窗口数据模型（转发框架窗口）。
    /// </summary>
    public WebWindowModel? Model { get => Window.Model; set => Window.Model = value; }

    /// <summary>
    /// 页面加载完成（转发框架窗口 Loaded）。
    /// </summary>
    public event EventHandler? Loaded;

    public TestWindow(string windowPath, string title)
    {
        Window = WebWindowPlatform.Current.CreateWindow(new WebWindowOptions(windowPath)
        {
            Title = title,
            Headless = true,
            Width = 720,
            Height = 480
        });
        Window.Loaded += (_, _) => Loaded?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 显示窗口（无头模式只加载 WebView 首页）。
    /// </summary>
    public void Show() => Window.Show();

    /// <summary>
    /// 关闭窗口。
    /// </summary>
    public void Close() => Window.Close(null);

    /// <summary>
    /// 在页面里执行 JS 并返回 JSON 结果。
    /// </summary>
    /// <param name="script">要执行的 JS。</param>
    /// <returns>JS 执行结果（JSON 字符串）。</returns>
    public Task<string> ExecuteScriptAsync(string script) => Window.ExecuteScriptAsync(script);
}

/// <summary>
/// 真 WKWebView 端到端测试宿主（镜像 WebView2TestHarness，但无泵包装：主泵在 MacOSTestProgram.Main，
/// 场景经 MacOSMessageLoopSynchronizationContext 投递回主线程直接 await）。流程：模型挂到窗口 → Show()
/// → 等 Loaded → 等页面桥接就绪 → 执行测试体。
/// </summary>
internal static class MacOSTestHarness
{
    public static Task RunMainWindowAsync(MainWindowModel model, Func<TestWindow, Task> body, TimeSpan? timeout = null)
        => RunWindowAsync("main", "测试", model, body, timeout);

    public static async Task RunWindowAsync(string windowPath, string title, WebWindowModel model, Func<TestWindow, Task> body, TimeSpan? timeout = null)
    {
        TimeSpan t = timeout ?? TimeSpan.FromSeconds(60);
        var win = new TestWindow(windowPath, title);
        try
        {
            win.Model = model; // 必须在 Show() 前设置，快照才含初始值

            var nav = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            win.Loaded += (_, _) => nav.TrySetResult(true);

            win.Show(); // 无头：只加载 WebView 首页，窗口永不显示
            await nav.Task.WaitAsync(t);

            await WaitBridgeReadyAsync(win, t);
            await body(win);
        }
        finally
        {
            win.Close();
        }
    }

    /// <summary>
    /// 双窗口共享模型宿主（泛型）：任意模型任意页面路径，验证跨窗口广播（含元素级 ElementSet 广播）。
    /// 镜像 WebView2TestHarness.RunTwoWindowsSharedModelAsync（本工程多"multi"专用重载被泛型覆盖）。
    /// </summary>
    public static Task RunTwoWindowsSharedModelAsync<T>(string windowPath, string titleA, string titleB, T model, Func<TestWindow, TestWindow, Task> body, TimeSpan? timeout = null)
        where T : WebWindowModel
        => RunTwoWindowsSharedModelCoreAsync(windowPath, titleA, titleB, model, body, timeout);

    private static async Task RunTwoWindowsSharedModelCoreAsync<T>(string windowPath, string titleA, string titleB, T model, Func<TestWindow, TestWindow, Task> body, TimeSpan? timeout = null)
        where T : WebWindowModel
    {
        TimeSpan t = timeout ?? TimeSpan.FromSeconds(60);
        var winA = new TestWindow(windowPath, titleA);
        var winB = new TestWindow(windowPath, titleB);
        try
        {
            winA.Model = model;
            winB.Model = model;

            var navA = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var navB = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            winA.Loaded += (_, _) => navA.TrySetResult(true);
            winB.Loaded += (_, _) => navB.TrySetResult(true);

            winA.Show(); // 无头：只加载 WebView 首页，窗口永不显示
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
    }

    /// <summary>
    /// 轮询直到页面脚本桥接（window.__model）就绪。ExecuteScriptAsync 在 macOS 包一层 JSON.stringify：
    /// typeof undefined → "\"undefined\""；桥接后 → "\"object\""（reactive proxy），与 WebView2 同契约。
    /// </summary>
    private static async Task WaitBridgeReadyAsync(TestWindow win, TimeSpan timeout)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < timeout)
        {
            try
            {
                var result = await win.ExecuteScriptAsync("typeof window.__model");
                if (result != "\"undefined\"")
                    return;
            }
            catch
            {
                // 页面可能尚未就绪，忽略并重试
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
    /// 轮询 .NET 侧条件（在主线程内执行，直接读模型属性）。
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
