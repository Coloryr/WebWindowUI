#if WINDOWS
using System.Collections.Concurrent;
using WebWindowUI.Windows;

namespace WebWindowUI.Tests.Support;

/// <summary>
/// STA 泵：一根独占的 STA 线程承载所有触碰平台的测试工作。
///
/// 为什么必须这样：
///   WebWindowPlatform.Current 是静态单例，首次构造 WindowsPlatform 时会在「当前线程」
///   创建隐藏消息窗口并绑定单例 MessageLoopSynchronizationContext（UiThreadId 记为当前线程）。
///   之后任何 off-thread 的 PostMessage/ExecuteScriptAsync 都会 marshal 回这个线程。
///   如果测试在多个线程各建一次窗口，UiThreadId 被污染 → 死循环。
///
/// 泵线程初始化顺序（与本库 WindowsPlatform 构造逻辑一致）：
///   GetOrCreateMarshalWindow() → MessageLoopSynchronizationContext.Initialize(hwnd)
///   → SetSynchronizationContext(Instance)，之后进入泵循环。
///
/// 泵循环 = 排干工作队列 → 派发所有就绪消息（吞掉 WM_QUIT，关最后一个窗口会
/// PostQuitMessage）→ MsgWaitForMultipleObjectsEx 挂起等「工作信号 / 新消息 / 200ms 兜底」。
/// async 延续经 SynchronizationContext.Post → WM_RUN → 下一轮派发，天然回到泵线程。
///
/// 为什么构造不等待泵就绪：
///   模块初始化（TestBootstrap）期间启动的线程要等 CLR loader lock 释放才能执行首段托管代码，
///   而 loader lock 又持有到模块初始化返回——构造里若 _ready.Wait() 等线程就绪即死锁。
///   因此构造只负责「启动线程」，就绪等待放在首次使用（RunAsync）时：此时装配已完成、
///   锁已释放，泵线程早已进入循环，等待必然立即返回。
/// </summary>
internal sealed class StaThreadPump
{
    public static readonly StaThreadPump Instance = new();

    private readonly ConcurrentQueue<Func<Task>> _work = new();
    private readonly AutoResetEvent _workReady = new(false);
    private readonly ManualResetEventSlim _ready = new(false);
    private Exception? _initError;

    private StaThreadPump()
    {
        var thread = new Thread(PumpLoop)
        {
            Name = "StaThreadPump",
            IsBackground = true, // 后台线程，不阻碍进程退出；testhost 退出即清理
        };
        thread.SetApartmentState(ApartmentState.STA); // WebView2/COM 要求 UI 线程 STA
        thread.Start();
        // 注意：不能在这里等 _ready —— 见类注释的 loader lock 说明。
    }

    /// <summary>在泵线程执行一段 async 工作；返回的 Task 由 xUnit 线程 await。</summary>
    public Task RunAsync(Func<Task> body)
    {
        WaitReady();
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _work.Enqueue(async () =>
        {
            try { await body(); tcs.SetResult(); }
            catch (Exception ex) { tcs.SetException(ex); }
        });
        _workReady.Set();
        return tcs.Task;
    }

    public Task<T> RunAsync<T>(Func<Task<T>> body)
    {
        WaitReady();
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        _work.Enqueue(async () =>
        {
            try { tcs.SetResult(await body()); }
            catch (Exception ex) { tcs.SetException(ex); }
        });
        _workReady.Set();
        return tcs.Task;
    }

    /// <summary>确保泵已初始化完成（消息窗口 + SynchronizationContext 已绑定泵线程）。</summary>
    private void WaitReady()
    {
        if (_ready.IsSet)
            return;
        if (!_ready.Wait(10_000))
            throw new TimeoutException("STA 泵初始化超时", _initError);
        if (_initError is not null)
            throw new InvalidOperationException("STA 泵初始化失败", _initError);
    }

    private void PumpLoop()
    {
        try
        {
            // 与 WindowsPlatform 构造一致：在本线程绑定平台单例
            IntPtr hwnd = Win32.GetOrCreateMarshalWindow();
            MessageLoopSynchronizationContext.Initialize(hwnd);
            SynchronizationContext.SetSynchronizationContext(MessageLoopSynchronizationContext.Instance);
        }
        catch (Exception ex)
        {
            _initError = ex;
            _ready.Set();
            return;
        }
        _ready.Set();

        while (true)
        {
            // 1. 排干工作队列（每个 job 立即返回，await 延续走 SC → WM_RUN）
            while (_work.TryDequeue(out Func<Task>? job))
            {
                try { job(); } catch { /* job 内部已捕获异常并设置 tcs */ }
            }

            // 2. 派发所有就绪消息
            PumpPendingMessages();

            // 3. 挂起等待：工作信号 / 新消息 / 200ms 兜底
            IntPtr handle = _workReady.SafeWaitHandle.DangerousGetHandle();
            PumpWin32.MsgWaitForMultipleObjectsEx(
                1, new[] { handle }, 200,
                PumpWin32.QS_ALLINPUT, PumpWin32.MWMO_INPUTAVAILABLE);
        }
    }

    private static void PumpPendingMessages()
    {
        while (PumpWin32.PeekMessageW(out PumpWin32.MSG msg, IntPtr.Zero, 0, 0, PumpWin32.PM_REMOVE))
        {
            if (msg.message == PumpWin32.WM_QUIT)
                continue; // 吞掉 WM_QUIT：泵不能因最后一个窗口关闭而退出
            PumpWin32.TranslateMessage(ref msg);
            PumpWin32.DispatchMessageW(ref msg);
        }
    }
}
#endif

