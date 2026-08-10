using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace WebWindowUI.Cef;

/// <summary>
/// 每个窗口一套 CEF 回调对象（client + life_span_handler + load_handler）。对象是 flat 结构，
/// AllocHGlobal 分配、方法槽填本类静态 trampoline 的函数指针。CEF 回调（on_after_created /
/// on_load_end / on_before_close / do_close）都在 UI 线程（== 主线程，单线程消息循环）到达，
/// trampoline 可直接触碰 CefWindow 状态，无需跨线程 marshal。
///
/// 引用语义：本侧分配的对象初始 count=1（我方持有）；CEF 经 add_ref/release 增删。client 随浏览器
/// 生命周期被 CEF 引用，release 减到 0 也不释放内存（进程期对象，泄漏可忽略、换安全）。
/// get_life_span_handler/get_load_handler 返回前 add_ref（对应 C++ 包装器 CefRefPtr 采纳一引用的契约）。
/// </summary>
internal static class CefHandlers
{
    // native 回调对象指针 → 所属窗口（client / life_span / load 三个指针都映射到同一窗口）
    private static readonly ConcurrentDictionary<IntPtr, CefWindow> ByPtr = new();

    // 本侧分配对象的引用计数（CEF 会 add_ref/release；对象由 CEF 生命周期引用，从 1 起步）
    private static readonly ConcurrentDictionary<IntPtr, int> RefCounts = new();

    // ---- 保活：native 侧持有这些函数指针，Marshal.GetFunctionPointerForDelegate 的桩委托不能给 GC ----
    private static readonly CefBaseAddRef AddRefFn = self => RefCounts.AddOrUpdate(self, 1, (_, c) => c + 1);
    private static readonly CefBaseRelease ReleaseFn = self
        => RefCounts.AddOrUpdate(self, 0, (_, c) => c - 1) <= 0 ? 1 : 0; // release 返回「引用数是否为 0」
    private static readonly CefBaseHasOneRef HasOneRefFn = self => RefCounts.TryGetValue(self, out int c) && c == 1 ? 1 : 0;
    private static readonly CefBaseHasAtLeastOneRef HasAtLeastOneRefFn = self => RefCounts.TryGetValue(self, out int c) && c >= 1 ? 1 : 0;

    private static readonly CefClientGetLifeSpanHandler GetLifeSpanHandlerFn = self =>
    {
        CefWindow w = ByPtr[self];
        AddRefFn(w.LifeSpanHandler); // C++ CefRefPtr 采纳契约：返回前 +1
        return w.LifeSpanHandler;
    };
    private static readonly CefClientGetLoadHandler GetLoadHandlerFn = self =>
    {
        CefWindow w = ByPtr[self];
        AddRefFn(w.LoadHandler);
        return w.LoadHandler;
    };

    private static readonly CefLifeSpanOnAfterCreated OnAfterCreatedFn = (self, browser) => ByPtr[self].OnBrowserCreated(browser);
    private static readonly CefLifeSpanDoClose DoCloseFn = (self, browser) => 0; // false：让 CEF 继续关闭流程
    private static readonly CefLifeSpanOnBeforeClose OnBeforeCloseFn = (self, browser) => ByPtr[self].OnBrowserClosing();

    private static readonly CefLoadOnLoadEnd OnLoadEndFn = (self, browser, frame, httpCode) =>
    {
        // frame 在回调内有效（不持有引用）；is_main 命中即主页面导航完成
        if (CefNative.Frame_IsMain(frame))
            ByPtr[self].OnNavigationCompleted();
    };

    /// <summary>为窗口分配 client + life_span_handler + load_handler，注册路由，返回 client 指针（create_browser 用）。</summary>
    public static IntPtr CreateFor(CefWindow window)
    {
        IntPtr client = Alloc(new CefClientPtr
        {
            Base = MakeBase(Marshal.SizeOf<CefClientPtr>()),
            GetLifeSpanHandler = GetFnPtr(GetLifeSpanHandlerFn),
            GetLoadHandler = GetFnPtr(GetLoadHandlerFn),
        });
        IntPtr lifeSpan = Alloc(new CefLifeSpanHandlerPtr
        {
            Base = MakeBase(Marshal.SizeOf<CefLifeSpanHandlerPtr>()),
            OnAfterCreated = GetFnPtr(OnAfterCreatedFn),
            DoClose = GetFnPtr(DoCloseFn),
            OnBeforeClose = GetFnPtr(OnBeforeCloseFn),
        });
        IntPtr load = Alloc(new CefLoadHandlerPtr
        {
            Base = MakeBase(Marshal.SizeOf<CefLoadHandlerPtr>()),
            OnLoadEnd = GetFnPtr(OnLoadEndFn),
        });

        window.AttachHandlers(client, lifeSpan, load);
        ByPtr[client] = window;
        ByPtr[lifeSpan] = window;
        ByPtr[load] = window;
        RefCounts[client] = 1;
        RefCounts[lifeSpan] = 1;
        RefCounts[load] = 1;
        return client;
    }

    /// <summary>窗口销毁（WM_DESTROY）时摘掉路由，避免回调落到已关闭窗口。</summary>
    public static void Remove(CefWindow window)
    {
        foreach (var kv in ByPtr)
            if (ReferenceEquals(kv.Value, window))
                ByPtr.TryRemove(kv.Key, out _);
    }

    private static CefRefBase MakeBase(int size) => new()
    {
        Size = (ulong)size,
        AddRef = GetFnPtr(AddRefFn),
        Release = GetFnPtr(ReleaseFn),
        HasOneRef = GetFnPtr(HasOneRefFn),
        HasAtLeastOneRef = GetFnPtr(HasAtLeastOneRefFn),
    };

    private static IntPtr Alloc<T>(T value) where T : struct
    {
        IntPtr ptr = Marshal.AllocHGlobal(Marshal.SizeOf<T>());
        Marshal.StructureToPtr(value, ptr, false);
        return ptr;
    }

    private static IntPtr GetFnPtr(Delegate d) => Marshal.GetFunctionPointerForDelegate(d);
}

// ===== CEF → C# 回调委托（CEF 以 CEF_CALLBACK = __stdcall 调用本侧函数指针）=====

[UnmanagedFunctionPointer(CallingConvention.StdCall)]
internal delegate int CefBaseHasOneRef(IntPtr self);
[UnmanagedFunctionPointer(CallingConvention.StdCall)]
internal delegate int CefBaseHasAtLeastOneRef(IntPtr self);

[UnmanagedFunctionPointer(CallingConvention.StdCall)]
internal delegate IntPtr CefClientGetLifeSpanHandler(IntPtr self);
[UnmanagedFunctionPointer(CallingConvention.StdCall)]
internal delegate IntPtr CefClientGetLoadHandler(IntPtr self);

[UnmanagedFunctionPointer(CallingConvention.StdCall)]
internal delegate void CefLifeSpanOnAfterCreated(IntPtr self, IntPtr browser);
[UnmanagedFunctionPointer(CallingConvention.StdCall)]
internal delegate int CefLifeSpanDoClose(IntPtr self, IntPtr browser);
[UnmanagedFunctionPointer(CallingConvention.StdCall)]
internal delegate void CefLifeSpanOnBeforeClose(IntPtr self, IntPtr browser);

[UnmanagedFunctionPointer(CallingConvention.StdCall)]
internal delegate void CefLoadOnLoadEnd(IntPtr self, IntPtr browser, IntPtr frame, int httpCode);
