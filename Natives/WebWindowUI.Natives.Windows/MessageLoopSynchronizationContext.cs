namespace WebWindowUI.Natives.Windows;

/// <summary>
/// 把 async 延续派发回 UI 线程消息循环的 SynchronizationContext：经 WM_RUN 投递到隐藏消息窗口，
/// 由 Win32 GetMessage 循环驱动（UI 线程 == 主线程）。
/// </summary>
internal sealed class MessageLoopSynchronizationContext : SynchronizationContext
{
    /// <summary>
    /// 进程内单例。
    /// </summary>
    public static readonly MessageLoopSynchronizationContext Instance = new();

    private readonly Lock _lock = new();
    private readonly Queue<(SendOrPostCallback Callback, object? State)> _queue = new();
    private IntPtr _targetHwnd;

    private MessageLoopSynchronizationContext()
    {
    }

    /// <summary>
    /// UI 线程的托管线程 id，供「当前是否在 UI 线程」判断使用。不用 SynchronizationContext.Current——
    /// System.Threading.Timer 会把捕获的 ExecutionContext 流到线程池线程，SynchronizationContext.Current 会误判。
    /// </summary>
    public static int UiThreadId { get; private set; } = -1;

    /// <summary>
    /// 绑定目标消息窗口并记录当前线程为 UI 线程。总是在 UI 线程调用，幂等。
    /// </summary>
    /// <param name="targetHwnd">接收 WM_RUN 的隐藏消息窗口。</param>
    public static void Initialize(IntPtr targetHwnd)
    {
        Instance._targetHwnd = targetHwnd;
        UiThreadId = Environment.CurrentManagedThreadId;
    }

    /// <summary>
    /// 异步投递：入队 + PostMessageW 唤醒消息窗口。
    /// </summary>
    /// <param name="d">回调。</param>
    /// <param name="state">回调状态。</param>
    public override void Post(SendOrPostCallback d, object? state)
    {
        lock (_lock)
            _queue.Enqueue((d, state));

        Win32.PostMessageW(_targetHwnd, Win32.WM_RUN, IntPtr.Zero, IntPtr.Zero);
    }

    /// <summary>
    /// 同步投递：UI 线程直跑；非 UI 线程 marshal 回 UI 线程并阻塞等待（Post → WM_RUN → RunQueued）。
    /// </summary>
    /// <param name="d">回调。</param>
    /// <param name="state">回调状态。</param>
    public override void Send(SendOrPostCallback d, object? state)
    {
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

    /// <summary>
    /// 排干队列：逐个执行直到队列空（在消息窗口 WM_RUN 处理中被调用）。
    /// </summary>
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
