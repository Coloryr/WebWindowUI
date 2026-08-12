namespace WebWindowUI.Platforms.Linux;

/// <summary>
/// 把 async 延续派发回 GTK 主循环的 SynchronizationContext（Linux 版）。
/// 与 Windows 版同契约：Post 入队 + 唤醒主循环；Send 在 UI 线程直跑、否则 Post + 阻塞等待。
///
/// 唤醒用 GLib 默认 MainContext 的一次性 idle source：回调 RunQueued() 后返回 false（只跑一次）。
/// 主循环可能尚未运行（首次 Post 早于 RunMessageLoop）：source 挂到默认 MainContext，
/// 循环一开始即执行；已在运行则立即唤醒。g_main_context_invoke_full 线程安全，可从任意线程调用。
///
/// UI 线程判断用 <see cref="Environment.CurrentManagedThreadId"/> 与 UiThreadId 比较，
/// 不用 SynchronizationContext.Current——理由同 Windows：System.Threading.Timer 会随回调把创建时
/// 捕获的 ExecutionContext（含本上下文）流到线程池线程，SynchronizationContext.Current 会误判。
/// </summary>
public sealed class LinuxMessageLoopSynchronizationContext : SynchronizationContext
{
    public static readonly LinuxMessageLoopSynchronizationContext Instance = new();

    private readonly object _lock = new();
    private readonly Queue<(SendOrPostCallback Callback, object? State)> _queue = new();

    private LinuxMessageLoopSynchronizationContext()
    {
    }

    /// <summary>UI 线程（主循环线程）的托管线程 id，供「当前是否在 UI 线程」判断使用。</summary>
    public static int UiThreadId { get; private set; } = -1;

    /// <summary>记录 UI 线程 id。总是在主线程调用（平台构造 + RunMessageLoop），幂等。</summary>
    public static void Initialize()
    {
        if (UiThreadId == -1)
            UiThreadId = Environment.CurrentManagedThreadId;
    }

    public override void Post(SendOrPostCallback d, object? state)
    {
        lock (_lock)
            _queue.Enqueue((d, state));
        MainContext.Default().InvokeFull(0, () =>
        {
            RunQueued();
            return false;
        });
    }

    public override void Send(SendOrPostCallback d, object? state)
    {
        // Send 契约：投递到目标线程并阻塞直到执行完成，回调必须在上下文线程上运行。
        // UI 线程直接执行；非 UI 线程 marshal 回 UI 线程（Post → idle source → RunQueued）并等待。
        if (Environment.CurrentManagedThreadId == UiThreadId)
        {
            d(state);
            return;
        }
        var done = new ManualResetEventSlim(false);
        Exception? error = null;
        Post(_ =>
        {
            try { d(state); }
            catch (Exception ex) { error = ex; }
            finally { done.Set(); }
        }, null);
        done.Wait();
        if (error is not null)
            throw error;
    }

    public void RunQueued()
    {
        while (true)
        {
            (SendOrPostCallback Callback, object? State) item;
            lock (_lock)
            {
                if (_queue.Count == 0)
                    return;
                item = _queue.Dequeue();
            }
            item.Callback(item.State);
        }
    }
}
