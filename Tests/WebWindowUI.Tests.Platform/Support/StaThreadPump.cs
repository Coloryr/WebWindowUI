#if WINDOWS
using System.Collections.Concurrent;
using WebWindowUI.Core;
using WebWindowUI.Natives.Windows;

namespace WebWindowUI.Tests.Platform.Support;

/// <summary>
/// STA 泵：一根独占的 STA 线程承载所有触碰平台的测试工作。
///
/// 为什么必须这样：
///   WebWindowPlatform.Current 是静态单例，平台 [ModuleInitializer] 在「加载平台程序集的线程」上
///   构造 WindowsPlatform 并绑定单例 MessageLoopSynchronizationContext（隐藏消息窗口 + UiThreadId）。
///   隐藏消息窗口归创建线程所有，SC.Post（async 延续）经 PostMessageW(WM_RUN) 只投给它的创建线程——
///   若创建线程不是本泵线程，消息落进那个线程（无泵）的队列，延续永不派发 → 测试挂在 await。
///   故平台注册必须发生在本泵线程，且泵线程必须先于一切创建自己的隐藏消息窗口。
///
/// 泵线程初始化顺序：
///   创建隐藏消息窗口（GetOrCreateMarshalWindow 进程单例，泵线程首次创建即归泵）
///   → 绑定 SC → 本线程加载平台程序集（[ModuleInitializer] 在本线程 Register，
///   WindowsPlatform ctor 复用上面的 hwnd、把 UiThreadId 重绑为泵线程）→ 进入泵循环。
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

    /// <summary>
    /// 在泵线程执行一段 async 工作；返回的 Task 由 xUnit 线程 await。
    /// </summary>
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

    /// <summary>
    /// 确保泵已初始化完成（消息窗口 + SynchronizationContext 已绑定泵线程）。
    /// </summary>
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
            // 1. 泵线程先创建自己的隐藏消息窗口并绑定 SC —— 谁创建窗口谁拥有其消息队列。
            //    所有 SC.Post（async 延续）经 PostMessageW(WM_RUN) 投到该窗口，只能由拥有它的线程
            //    （本泵）派发。平台 [ModuleInitializer] 的 WindowsPlatform 构造也会 InitMessageLoop
            //    （GetOrCreateMarshalWindow 是进程单例）——只要这里抢到首次创建，窗口就归泵线程。
            Win32.SetMarshalMessageHandler(HandleMarshalMessage);
            var hwnd = Win32.GetOrCreateMarshalWindow("WebView2MarshalWindow");
            MessageLoopSynchronizationContext.Initialize(hwnd);
            SynchronizationContext.SetSynchronizationContext(MessageLoopSynchronizationContext.Instance);

            // 2. 在本线程注册平台：加载平台程序集触发 [ModuleInitializer]（Register ??=），
            //    WindowsPlatform ctor 复用上面的 hwnd、把 UiThreadId 重绑为泵线程。
            //    绝不能先让别的线程加载平台程序集——module init 会在该线程建 marshal 窗口，
            //    WM_RUN 落进那个线程（无泵）的队列，async 延续永不派发（历史死锁根因）。
            EnsurePlatformRegistered();
            Trace.Log("pump: platform registered");
        }
        catch (Exception ex)
        {
            _initError = ex;
            Trace.Log($"pump: init error {ex}");
            _ready.Set();
            return;
        }
        _ready.Set();
        Trace.Log("pump: ready, entering loop");

        while (true)
        {
            // 1. 排干工作队列（每个 job 立即返回，await 延续走 SC → WM_RUN）
            while (_work.TryDequeue(out Func<Task>? job))
            {
                try { job(); } catch { /* job 内部已捕获异常并设置 tcs */ }
            }

            Trace.Log("pump: cycle");
            PumpPendingMessages();

            // 3. 挂起等待：工作信号 / 新消息 / 200ms 兜底
            var handle = _workReady.SafeWaitHandle.DangerousGetHandle();
            PumpWin32.MsgWaitForMultipleObjectsEx(
                1, new[] { handle }, 200,
                PumpWin32.QS_ALLINPUT, PumpWin32.MWMO_INPUTAVAILABLE);
        }
    }

    /// <summary>
    /// marshal 窗口的 WM_RUN 处理器：排干 SC 队列、在泵线程执行 async 延续（同 Win32MessageLoop）。
    /// </summary>
    private static IntPtr? HandleMarshalMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == Win32.WM_RUN)
        {
            MessageLoopSynchronizationContext.Instance.RunQueued();
            return IntPtr.Zero;
        }
        return null;
    }

    /// <summary>
    /// 在本线程（泵线程）加载平台程序集并注册：平台 [ModuleInitializer] 在加载线程上
    /// Register(new WindowsPlatform())。GetOrCreateMarshalWindow 是进程单例——泵线程已在
    /// 前面创建过，WindowsPlatform ctor 的 InitMessageLoop 只会复用泵线程的窗口。
    /// 若 module init 因故未生效（Register ??= 已注册则跳过），显式 Register 兜底。
    /// </summary>
    private static void EnsurePlatformRegistered()
    {
        _ = typeof(WebWindowUI.Platforms.Windows.WindowsPlatform);
        try
        {
            _ = WebWindowPlatform.Current; // module init 已注册即返回
        }
        catch (PlatformNotSupportedException)
        {
            WebWindowPlatform.Register(new WebWindowUI.Platforms.Windows.WindowsPlatform());
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

