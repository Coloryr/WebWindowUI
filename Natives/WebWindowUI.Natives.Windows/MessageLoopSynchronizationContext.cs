namespace WebWindowUI.Natives.Windows;

/// <summary>
/// 把 async 延续派发回 UI 线程消息循环的 SynchronizationContext。
/// 单例：绑定到一个隐藏消息窗口，所有窗口的 CEF 异步工作与跨线程调用都通过它回到 UI 线程
/// （CEF 单线程消息循环下 UI 线程 == 主线程，Win32 GetMessage 循环同时驱动本上下文与 cef_do_message_loop_work）。
/// </summary>
internal sealed class MessageLoopSynchronizationContext : SynchronizationContext
{
    public static readonly MessageLoopSynchronizationContext Instance = new();

    private readonly Lock _lock = new();
    private readonly Queue<(SendOrPostCallback Callback, object? State)> _queue = new();
    private IntPtr _targetHwnd;

    private MessageLoopSynchronizationContext()
    {
    }

    /// <summary>
    /// UI 线程的托管线程 id，供「当前是否在 UI 线程」判断使用。
    /// 注意：不能用 SynchronizationContext.Current 判断——System.Threading.Timer 等会随回调
    /// 把创建时捕获的 ExecutionContext（含本上下文）流到线程池线程，导致在线程池线程上误判为 UI 线程。
    /// </summary>
    public static int UiThreadId { get; private set; } = -1;

    public static void Initialize(IntPtr targetHwnd)
    {
        Instance._targetHwnd = targetHwnd;
        UiThreadId = Environment.CurrentManagedThreadId;
    }

    public override void Post(SendOrPostCallback d, object? state)
    {
        lock (_lock)
            _queue.Enqueue((d, state));

        Win32.PostMessageW(_targetHwnd, Win32.WM_RUN, IntPtr.Zero, IntPtr.Zero);
    }

    public override void Send(SendOrPostCallback d, object? state)
    {
        // Send 契约：投递到目标线程并阻塞直到执行完成，回调必须在上下文线程上运行。
        // UI 线程直接执行；非 UI 线程 marshal 回 UI 线程（Post → WM_RUN → RunQueued）并等待。
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
