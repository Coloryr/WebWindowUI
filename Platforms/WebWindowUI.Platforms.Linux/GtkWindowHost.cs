namespace WebWindowUI.Platforms.Linux;

/// <summary>
/// GTK3 窗口宿主：封装窗口句柄（IntPtr）与 destroy 信号桥。用 g_signal_connect_data 把窗口的
/// "destroy" 信号接到 Cdecl 静态 trampoline（保活），经单个 GCHandle 路由回本实例的 <see cref="Destroyed"/>
/// 事件。销毁时 <see cref="Dispose"/> 断开信号并释放 GCHandle。
/// 信号在主循环线程触发，trampoline 内不做重入。
/// </summary>
internal sealed class GtkWindowHost : IDisposable
{
    // 保活：native 只持有函数指针，委托实例必须被静态字段强引用。
    private static readonly SignalDestroyCallback _destroyTrampoline = OnDestroyedTrampoline;

    private readonly IntPtr _window;
    private readonly GCHandle _handle;
    private ulong _destroyHandlerId;

    /// <summary>窗口被销毁时触发（用户关标题栏或 Close() → destroy）。</summary>
    public event EventHandler? Destroyed;

    public GtkWindowHost(string title, int width, int height)
    {
        _window = GtkNative.CreateWindow(title, width, height);
        _handle = GCHandle.Alloc(this);
        _destroyHandlerId = WebKit2Native.ConnectSignal(_window, "destroy", _destroyTrampoline, _handle);
    }

    public void SetChild(IntPtr webView) => GtkNative.SetChild(_window, webView);

    public void SetTitle(string title) => GtkNative.SetTitle(_window, title);

    public void Show() => GtkNative.Show(_window);

    public void Activate() => GtkNative.Activate(_window);

    public void Hide() => GtkNative.Hide(_window);

    public void Close() => GtkNative.Close(_window);

    /// <summary>断开 destroy 信号并释放路由 GCHandle。窗口已销毁时 DisconnectSignal 吞掉异常。</summary>
    public void Dispose()
    {
        WebKit2Native.DisconnectSignal(_window, _destroyHandlerId);
        _destroyHandlerId = 0;
        if (_handle.IsAllocated)
            _handle.Free();
    }

    private static void OnDestroyedTrampoline(IntPtr window, IntPtr userData)
    {
        try
        {
            var host = GCHandle.FromIntPtr(userData).Target as GtkWindowHost;
            host?.Destroyed?.Invoke(host, EventArgs.Empty);
        }
        catch
        {
            // 窗口已销毁 / GCHandle 已释放等，忽略
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void SignalDestroyCallback(IntPtr instance, IntPtr userData);
}
