using System.Drawing;
using WebWindowUI.Core;

namespace WebWindowUI.Natives.Linux;

/// <summary>
/// Linux 原生窗口（GTK3 顶层窗口），镜像 Win32NativeWindow：封装 GTK 窗口句柄生命周期与
/// destroy/configure 信号桥（GTK 无 WndProc，用信号代替），平台经 <see cref="INativeWindow"/> 消费。
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

    /// <summary>
    /// 窗口句柄。
    /// </summary>
    public IntPtr WindowHandle => _window;

    /// <summary>
    /// 窗口销毁时触发。
    /// </summary>
    public event Action? Destory;

    /// <summary>
    /// 窗口尺寸变化时触发。
    /// </summary>
    public event Action? Resize;

    /// <summary>
    /// 创建 GTK 窗口并连接 destroy/configure 信号。
    /// </summary>
    /// <param name="options">窗口选项（标题/尺寸）。</param>
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

    /// <summary>
    /// 显示窗口。
    /// </summary>
    public void Show() => GtkNative.Show(_window);

    /// <summary>
    /// 隐藏窗口（不销毁）。
    /// </summary>
    public void Hide() => GtkNative.Hide(_window);

    /// <summary>
    /// 关闭窗口。gtk_window_close → 默认 close-request 处理器 destroy → <see cref="Destory"/>。
    /// </summary>
    public void Close() => GtkNative.Close(_window);

    /// <summary>
    /// 激活窗口。
    /// </summary>
    public void Activate() => GtkNative.Activate(_window);

    /// <summary>
    /// 修改标题。
    /// </summary>
    /// <param name="title">新标题。</param>
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

    /// <summary>
    /// destroy 信号 trampoline：经 GCHandle 路由回 Destory。
    /// </summary>
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
