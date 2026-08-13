using System.Drawing;
using WebWindowUI.Core;

namespace WebWindowUI.Natives.Linux;

/// <summary>
/// Linux 原生窗口（GTK3 顶层窗口），镜像 Windows 的 <c>Win32NativeWindow</c>：封装 GTK 窗口句柄
/// 生命周期与 destroy/configure 信号桥，平台经 <see cref="INativeWindow"/> 消费。
///
/// 信号桥用 g_signal_connect_data 把 "destroy" 与 "configure-event" 接到 Cdecl 静态 trampoline（保活），
/// 经单个 GCHandle 路由回本实例的事件。信号在主循环线程触发，trampoline 内不做重入。
///
/// 与 Win32 的差异：
///  - GTK 无 WndProc，用信号代替（destroy → <see cref="Destory"/>、configure-event → <see cref="Resize"/>）；
///  - <see cref="SetChild"/> 挂 WebView（gtk_container_add 收浮点引用，窗口接管一个引用）——WebView2 是
///    子 HWND 由 WebView2 自己建，GTK 的 webview 是 GtkWidget，须由窗口层挂上去；
///  - <see cref="SetIcon"/> 无操作：GTK3 的 gtk_window_set_icon 在 CSD/Wayland 下不显示 per-window 图标。
/// </summary>
public sealed class LinuxNativeWindow : INativeWindow
{
    // 保活：native 只持有函数指针，委托实例必须被静态字段强引用。
    private static readonly SignalDestroyCallback _destroyTrampoline = OnDestroyed;
    private static readonly SignalConfigureCallback _configureTrampoline = OnConfigure;

    private readonly IntPtr _window;
    private readonly GCHandle _handle;
    private ulong _destroyHandlerId;
    private ulong _configureHandlerId;

    public IntPtr WindowHandle => _window;

    public event Action? Destory;
    public event Action? Resize;

    public LinuxNativeWindow(WebWindowOptions options)
    {
        _window = GtkNative.CreateWindow(options.Title, options.Width, options.Height);
        _handle = GCHandle.Alloc(this);
        _destroyHandlerId = GtkNative.ConnectSignal(_window, "destroy", _destroyTrampoline, _handle);
        _configureHandlerId = GtkNative.ConnectSignal(_window, "configure-event", _configureTrampoline, _handle);
    }

    /// <summary>
    /// 把子控件挂到窗口（创建 WebView 后调用）。gtk_container_add 收浮点引用，窗口接管一个引用。
    /// </summary>
    public void SetChild(IntPtr child) => GtkNative.SetChild(_window, child);

    public void Show() => GtkNative.Show(_window);

    public void Hide() => GtkNative.Hide(_window);

    /// <summary>
    /// 关闭窗口。gtk_window_close → 默认 close-request 处理器 destroy → <see cref="Destory"/>。
    /// </summary>
    public void Close() => GtkNative.Close(_window);

    public void Activate() => GtkNative.Activate(_window);

    public void SetTitle(string title) => GtkNative.SetTitle(_window, title);

    public void SetIcon(WindowIcon icon)
    {
        // GTK3 的 gtk_window_set_icon 在 CSD/Wayland 下不显示 per-window 图标，平台限制，无操作。
    }

    /// <summary>
    /// 取窗口当前尺寸。origin 恒为 (0,0)（与 Win32NativeWindow 的 GetClientRect 形状对齐）。
    /// </summary>
    public Rectangle GetSize()
    {
        GtkNative.GetSize(_window, out int width, out int height);
        return new Rectangle(0, 0, width, height);
    }

    /// <summary>
    /// 断开 destroy/configure 信号并释放路由 GCHandle。窗口已销毁时 DisconnectSignal 吞掉异常。
    /// </summary>
    public void Dispose()
    {
        GtkNative.DisconnectSignal(_window, _destroyHandlerId);
        GtkNative.DisconnectSignal(_window, _configureHandlerId);
        _destroyHandlerId = 0;
        _configureHandlerId = 0;
        if (_handle.IsAllocated)
            _handle.Free();
    }

    private static void OnDestroyed(IntPtr window, IntPtr userData)
    {
        try
        {
            (GCHandle.FromIntPtr(userData).Target as LinuxNativeWindow)?.Destory?.Invoke();
        }
        catch
        {
            // 窗口已销毁 / GCHandle 已释放等，忽略
        }
    }

    /// <summary>
    /// configure-event：窗口 move/resize/show 都会触发。返回 FALSE 表示未处理，交给 GTK 继续。
    /// </summary>
    private static int OnConfigure(IntPtr widget, IntPtr event_, IntPtr userData)
    {
        try
        {
            (GCHandle.FromIntPtr(userData).Target as LinuxNativeWindow)?.Resize?.Invoke();
        }
        catch
        {
            // 窗口已销毁 / GCHandle 已释放等，忽略
        }
        return 0; // FALSE：不拦截事件，GTK 默认处理
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void SignalDestroyCallback(IntPtr instance, IntPtr userData);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I4)]
    private delegate int SignalConfigureCallback(IntPtr widget, IntPtr event_, IntPtr userData);
}
