#if LINUX
using GLib;
using Thread = System.Threading.Thread; // GLib 也有 Thread 类型，避免歧义
using WebWindowUI.Linux;

namespace WebWindowUI.Tests.Support;

/// <summary>
/// GTK 主循环泵：一根独占线程跑 GLib MainLoop，承载所有触碰 WebKit/GTK 的测试工作。
/// 与 Windows 的 <see cref="StaThreadPump"/> 对应——WebKit/GTK 对象只能主线程访问，且
/// LinuxMessageLoopSynchronizationContext 是进程单例（UiThreadId 记录首次构造平台的线程），
/// 必须在同一根线程上构造平台、跑循环、执行测试体。
///
/// 泵线程初始化顺序（与本库 LinuxPlatform 构造逻辑一致）：
///   访问 WebWindowPlatform.Current（懒创建静态单例）→ 在泵线程执行 gtk_init、WebKit 初始化、
///   LinuxMessageLoopSynchronizationContext.Initialize()（UiThreadId=泵线程）、SetSynchronizationContext。
///   然后 MainLoop 跑 GLib 默认 MainContext（与 SyncContext.Post 的 idle source 同一上下文）。
///
/// 测试体经 LinuxMessageLoopSynchronizationContext.Post 投递到泵线程（idle source → 循环迭代执行），
/// async 延续捕获泵线程的 SynchronizationContext 自动回到泵线程，全程串行在泵线程。
///
/// 泵线程是后台线程：testhost 退出即终止（与 StaThreadPump 一致），无需显式 Shutdown。
/// 不调用 LinuxPlatform.RunMessageLoop()（那是宿主 App 的入口）；测试期间窗口全关时框架的
/// QuitMainLoop() 因 _mainLoop 为空而 no-op，泵的主循环持续运行。
///
/// 运行要求：进程需要显示会话（gtk_init 需要 DISPLAY/WAYLAND）且 WebKit 沙箱已按宿主环境处理
/// （与本库 sample 相同，运行时用户自行决定 WEBKIT_DISABLE_SANDBOX_THIS_IS_DANGEROUS=1）。
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

    /// <summary>在泵线程执行一段 async 工作；返回的 Task 由 xUnit 线程 await。</summary>
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

    /// <summary>确保泵已初始化完成（GTK/WebKit 已初始化、主循环已就绪）。</summary>
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
            // 平台构造放本线程：gtk_init、WebKit 初始化、UiThreadId=泵线程、SetSynchronizationContext。
            // 直接访问 WebWindowPlatform.Current（懒创建单例），后续所有 WebWindow 复用同一实例，
            // 不会出现第二次 gtk_init。
            _ = WebWindowPlatform.Current;
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
#endif
