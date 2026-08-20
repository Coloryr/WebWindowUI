using System.Diagnostics;
using WebWindowUI.Core;
using WebWindowUI.Sample;

namespace WebWindowUI.Tests.Windows.Support;

/// <summary>
/// 测试用窗口控制器：经 <see cref="WebWindowPlatform.Current.CreateWindow"/> 建窗（真实 CoreWebView2 +
/// 真实构建产物 wwwroot，经 ProjectReference 传递复制到测试 bin）。无头模式
/// （<see cref="WebWindowOptions.Headless"/>）：窗口永不显示，但导航/DOM/JS/消息通道照常，
/// 测试全程不出现在屏幕与任务栏。
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

    public TestWindow(string windowPath, string title, string? modelSelector = null)
    {
        Window = WebWindowPlatform.Current.CreateWindow(new WebWindowOptions(windowPath)
        {
            Title = title,
            Headless = true,
            Width = 720,
            Height = 480,
            Query = modelSelector is null ? null : $"model={modelSelector}"
        });
        Window.Loaded += (_, _) => Loaded?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 显示窗口（无头模式只初始化 WebView）。
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
/// 真 WebView2 端到端测试宿主。流程：模型挂到窗口 → Show() → 等 Loaded → 等页面桥接
/// （window.__model）就绪 → 执行测试体；全部在 STA 泵线程内经 StaThreadPump.RunAsync 承载。
/// </summary>
internal static class WebView2TestHarness
{
    public static Task RunMainWindowAsync(MainWindowModel model, Func<TestWindow, Task> body, TimeSpan? timeout = null)
        => RunWindowAsync("main", "测试", model, body, timeout);

    public static Task RunWindowAsync(string windowPath, string title, WebWindowModel model, Func<TestWindow, Task> body, TimeSpan? timeout = null)
    {
        TimeSpan t = timeout ?? TimeSpan.FromSeconds(60);
        string? selector = ModelSelector(model);
        string path = selector is null ? windowPath : "demo"; // 已删独立页 → 综合演示窗口 test 模式（?model=）直达
        return StaThreadPump.Instance.RunAsync(async () =>
        {
            var win = new TestWindow(path, title, selector);
            try
            {
                win.Model = model; // 必须在 Show() 前设置，快照才含初始值

                var nav = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                win.Loaded += (_, _) => nav.TrySetResult(true);

                win.Show(); // 无头：只初始化 WebView，窗口永不显示
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
    /// 双窗口共享模型宿主：两个 TestWindow 绑同一个 model 实例，验证跨窗口广播。
    /// 两窗口都走"multi"页面（演示「一个 model 给多个窗口用」）；窗口 A 先 Show 等导航，
    /// 再 Show B，各自收初始快照。
    /// </summary>
    public static Task RunTwoWindowsSharedModelAsync(MultiWindowModel model, Func<TestWindow, TestWindow, Task> body, TimeSpan? timeout = null)
        => RunTwoWindowsSharedModelCoreAsync("multi", "共享A", "共享B", model, body, timeout);

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
        string? selector = ModelSelector(model);
        string path = selector is null ? windowPath : "demo"; // 已删独立页 → 综合演示窗口 test 模式（?model=）直达
        await StaThreadPump.Instance.RunAsync(async () =>
        {
            var winA = new TestWindow(path, titleA, selector);
            var winB = new TestWindow(path, titleB, selector);
            try
            {
                winA.Model = model;
                winB.Model = model;

                var navA = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var navB = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                winA.Loaded += (_, _) => navA.TrySetResult(true);
                winB.Loaded += (_, _) => navB.TrySetResult(true);

                winA.Show(); // 无头：只初始化 WebView，窗口永不显示
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
    /// 模型实例 → 综合演示页 query 选择器：独立页面已删除的模型映射到 demo?model=&lt;选择器&gt; 直达；
    /// 仍保留真实子窗口页面的模型（multi / nested-detail / nested-list-item）返回 null 维持原路径。
    /// </summary>
    /// <param name="model">注入窗口的模型实例。</param>
    /// <returns>query 选择器；无对应综合页 tab 返回 null。</returns>
    private static string? ModelSelector(WebWindowModel model) => model switch
    {
        MainWindowModel => "main",
        TodoListModel => "todos",
        SettingsModel => "settings",
        LauncherModel => "launcher",
        DemoModel => "demo",
        NestedParentModel => "nested",
        NestedListModel => "nested-list",
        _ => null,
    };

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
                // 控制器可能尚未就绪，忽略并重试
            }
            await Task.Delay(50);
        }
        throw new TimeoutException("页面桥接未就绪（window.__model 未出现）");
    }

    /// <summary>
    /// 轮询 JS 表达式直到为 true（ExecuteScriptAsync 对布尔返回 "true"/"false"）。
    /// </summary>
    public static async Task WaitJsAsync(TestWindow win, string jsExpr, string description, TimeSpan? timeout = null)
        => await WaitJsAsync(win.Window, jsExpr, description, timeout);

    /// <summary>
    /// 轮询 JS 表达式直到为 true（ExecuteScriptAsync 对布尔返回 "true"/"false"）。
    /// 原始 <see cref="WebWindow"/> 重载（真实 DemoWindow 控制器测试用，ExecuteScriptAsync 经 InternalsVisibleTo）。
    /// </summary>
    public static async Task WaitJsAsync(WebWindow win, string jsExpr, string description, TimeSpan? timeout = null)
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

