using CoreFoundation;

namespace WebWindowUI.Platforms.MacOS;

/// <summary>
/// 把 async 延续派发回 Cocoa 主事件循环的 SynchronizationContext（macOS 版）。
/// Post 入队 + 唤醒主循环；Send 在 UI 线程直跑、否则 Post + 阻塞等待。
/// </summary>
public sealed class MacOSMessageLoopSynchronizationContext : SynchronizationContext
{
    /// <summary>
    /// 进程内单例。
    /// </summary>
    public static readonly MacOSMessageLoopSynchronizationContext Instance = new();

    private readonly object _lock = new();
    private readonly Queue<(SendOrPostCallback Callback, object? State)> _queue = new();

    private MacOSMessageLoopSynchronizationContext()
    {
    }

    /// <summary>
    /// UI 线程（主事件循环线程）的托管线程 id，供「当前是否在 UI 线程」判断使用。
    /// </summary>
    public static int UiThreadId { get; private set; } = -1;

    /// <summary>
    /// 记录 UI 线程 id。总是在主线程调用（平台构造 + RunMessageLoop），幂等。
    /// </summary>
    public static void Initialize()
    {
        if (UiThreadId == -1)
            UiThreadId = Environment.CurrentManagedThreadId;
    }

    /// <summary>
    /// 异步投递：入队 + 经主 dispatch 队列一次性 block 唤醒主循环（主循环未运行则排队等它启动）。
    /// </summary>
    /// <param name="d">回调。</param>
    /// <param name="state">回调状态。</param>
    public override void Post(SendOrPostCallback d, object? state)
    {
        lock (_lock)
            _queue.Enqueue((d, state));
        DispatchQueue.MainQueue.DispatchAsync(RunQueued);
    }

    /// <summary>
    /// 同步投递：UI 线程直跑；非 UI 线程 marshal 回主队列并阻塞等待（Post → block → RunQueued）。
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
    /// 排干队列：逐个执行直到队列空（在主线程被 block 调用）。
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
