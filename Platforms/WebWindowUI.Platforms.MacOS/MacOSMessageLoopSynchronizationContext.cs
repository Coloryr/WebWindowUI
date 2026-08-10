namespace WebWindowUI.MacOS;

/// <summary>
/// 把 async 延续派发回 Cocoa 主事件循环的 SynchronizationContext（macOS 版）。
/// 与 Windows/Linux 版同契约：Post 入队 + 唤醒主循环；Send 在 UI 线程直跑、否则 Post + 阻塞等待。
///
/// 唤醒用主 dispatch 队列的一次性 block：回调 RunQueued()。主循环（NSApplication.Run）尚未运行时，
/// block 排队等待直到 Run 开始；已在运行则立即执行。DispatchAsync 线程安全，可从任意线程调用。
///
/// UI 线程判断用 <see cref="Environment.CurrentManagedThreadId"/> 与 UiThreadId 比较，
/// 不用 SynchronizationContext.Current——理由同 Windows：System.Threading.Timer 会随回调把创建时
/// 捕获的 ExecutionContext（含本上下文）流到线程池线程，SynchronizationContext.Current 会误判。
/// </summary>
public sealed class MacOSMessageLoopSynchronizationContext : SynchronizationContext
{
    public static readonly MacOSMessageLoopSynchronizationContext Instance = new();

    private readonly object _lock = new();
    private readonly Queue<(SendOrPostCallback Callback, object? State)> _queue = new();

    private MacOSMessageLoopSynchronizationContext()
    {
    }

    /// <summary>UI 线程（主事件循环线程）的托管线程 id，供「当前是否在 UI 线程」判断使用。</summary>
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
        DispatchQueue.MainQueue.DispatchAsync(RunQueued);
    }

    public override void Send(SendOrPostCallback d, object? state)
    {
        // Send 契约：投递到目标线程并阻塞直到执行完成，回调必须在上下文线程上运行。
        // UI 线程直接执行；非 UI 线程 marshal 回主队列（Post → block → RunQueued）并等待。
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
