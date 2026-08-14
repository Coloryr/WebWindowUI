using GLib;
using WebWindowUI.Platforms.Linux;
using Thread = System.Threading.Thread; // GLib 也有 Thread 类型，避免歧义

namespace WebWindowUI.Tests.Linux.Support;

/// <summary>
/// GTK 主循环泵：一根独占线程跑 GLib MainLoop，承载所有触碰 WebKit/GTK 的测试工作（对应 StaThreadPump）。
/// WebKit/GTK 对象只能主线程访问，且 LinuxMessageLoopSynchronizationContext 是进程单例——泵线程
/// typeof(LinuxPlatform) 触发 [ModuleInitializer] new LinuxPlatform()（gtk_init + SC.Initialize，
/// UiThreadId=泵线程），测试体经 SC.Post 投递、async 延续自动回到泵线程。不调 RunMessageLoop()
/// （宿主入口），窗口全关时 QuitMainLoop 因 _mainLoop 为空 no-op。运行需显示会话 + WebKit 沙箱按宿主处理。
/// </summary>
internal sealed class GtkPump
{
    public static readonly GtkPump Instance = new();

    private readonly ManualResetEventSlim _ready = new(false);
    private Exception? _initError;
    private MainLoop? _loop;

    private GtkPump()
    {
        var thread = new Thread(PumpLoop)
        {
            Name = "GtkPump",
            IsBackground = true, // 后台线程，不阻碍进程退出；testhost 退出即清理
        };
        thread.Start();
        // 构造不能等 _ready：loader lock 语义同 StaThreadPump 的说明，就绪等待在首次 RunAsync。
    }

    /// <summary>
    /// 在泵线程执行一段 async 工作；返回的 Task 由 xUnit 线程 await。
    /// </summary>
    public Task RunAsync(Func<Task> body)
    {
        WaitReady();
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        LinuxMessageLoopSynchronizationContext.Instance.Post(async _ =>
        {
            try { await body(); tcs.SetResult(); }
            catch (Exception ex) { tcs.SetException(ex); }
        }, null);
        return tcs.Task;
    }

    public Task<T> RunAsync<T>(Func<Task<T>> body)
    {
        WaitReady();
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        LinuxMessageLoopSynchronizationContext.Instance.Post(async _ =>
        {
            try { tcs.SetResult(await body()); }
            catch (Exception ex) { tcs.SetException(ex); }
        }, null);
        return tcs.Task;
    }

    /// <summary>
    /// 确保泵已初始化完成（GTK/WebKit 已初始化、主循环已就绪）。
    /// </summary>
    private void WaitReady()
    {
        if (_ready.IsSet)
            return;
        if (!_ready.Wait(10_000))
            throw new TimeoutException("GTK 泵初始化超时", _initError);
        if (_initError is not null)
            throw new InvalidOperationException("GTK 泵初始化失败", _initError);
    }

    private void PumpLoop()
    {
        try
        {
            WebWindowUIPlatform.RegisterPlatformLoader(new LinuxPlatform());
            _loop = MainLoop.New(null, false); // null = 默认 MainContext（与 SyncContext.Post 同一上下文）
        }
        catch (Exception ex)
        {
            _initError = ex;
            _ready.Set();
            return;
        }
        _ready.Set();

        _loop.RunWithSynchronizationContext(); // 阻塞跑主循环直到进程退出（后台线程）
    }
}
