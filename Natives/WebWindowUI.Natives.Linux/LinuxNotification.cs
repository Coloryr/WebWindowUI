using System.Runtime.InteropServices;
using WebWindowUI.Core.Platform;

namespace WebWindowUI.Natives.Linux;

/// <summary>
/// Linux 系统通知（libnotify）：标题/正文/类型映射 urgency + 点击回调（closed 信号，
/// 关闭方式为 dismissed 时触发）。libnotify 版本（.so.7/.so.4）运行时探测，不可用时
/// Show/Close 静默跳过。GObject 调用须在主线程：Show/Close 经 g_idle_add 调度到主循环执行
/// （通知常从后台线程触发，如 Timer/任务完成）。主循环未运行时回调不执行、通知不显示。
/// </summary>
public sealed class LinuxNotification : INotification
{
    /// <summary>
    /// 共享实例（平台 <c>Notification</c> 属性返回）。
    /// </summary>
    public static readonly LinuxNotification Instance = new();

    // 保活：native 只持有函数指针，委托实例必须被静态字段强引用。
    private static readonly NotifyClosedCallback _closedTrampoline = OnClosed;
    private static readonly GtkNative.GSourceFunc _showIdle = OnShowIdle;
    private static readonly GtkNative.GSourceFunc _closeIdle = OnCloseIdle;

    private readonly object _gate = new();
    private string _pendingTitle = "";
    private string _pendingText = "";
    private NotificationType _pendingType = NotificationType.Info;
    private bool _idleQueued;

    private IntPtr _notification;
    private ulong _closedHandlerId;

    /// <summary>
    /// 通知被点击（关闭方式为 dismissed）时触发。
    /// </summary>
    public event Action? Clicked;

    private LinuxNotification()
    {
        LibNotify.Initialize();
    }

    /// <summary>
    /// 显示系统通知（libnotify 不可用时静默跳过）。重复调用替换旧通知；调用可来自任意线程，
    /// 实际显示经 g_idle_add 在主循环空闲时执行。
    /// </summary>
    /// <param name="title">标题。</param>
    /// <param name="text">正文。</param>
    /// <param name="type">通知类型（映射 libnotify urgency）。</param>
    public void Show(string title, string text, NotificationType type = NotificationType.Info)
    {
        if (!LibNotify.IsAvailable)
            return;

        bool queue;
        lock (_gate)
        {
            _pendingTitle = title;
            _pendingText = text;
            _pendingType = type;
            queue = !_idleQueued;
            _idleQueued = true;
        }
        if (queue)
            GtkNative.AddIdle(_showIdle, IntPtr.Zero);
    }

    /// <summary>
    /// 关闭当前通知（无通知时无效果；同样经主循环调度执行）。
    /// </summary>
    public void Close()
    {
        if (!LibNotify.IsAvailable)
            return;

        bool queue;
        lock (_gate)
        {
            queue = !_idleQueued;
            _idleQueued = true;
        }
        if (queue)
            GtkNative.AddIdle(_closeIdle, IntPtr.Zero);
    }

    /// <summary>
    /// 显示 idle 回调（主循环执行）：取最新待显参数后实际构造通知。
    /// </summary>
    private static int OnShowIdle(IntPtr data)
    {
        try
        {
            var inst = Instance;
            string title, text;
            NotificationType type;
            lock (inst._gate)
            {
                inst._idleQueued = false;
                title = inst._pendingTitle;
                text = inst._pendingText;
                type = inst._pendingType;
            }
            inst.ShowOnUiThread(title, text, type);
        }
        catch
        {
            // 通知服务异常等，忽略
        }
        return 0; // G_SOURCE_REMOVE：一次性
    }

    /// <summary>
    /// 关闭 idle 回调（主循环执行）。
    /// </summary>
    private static int OnCloseIdle(IntPtr data)
    {
        try
        {
            lock (Instance._gate)
                Instance._idleQueued = false;
            Instance.CloseInternal();
        }
        catch
        {
            // 通知服务异常等，忽略
        }
        return 0;
    }

    /// <summary>
    /// 主循环线程实际显示：关闭旧的并构造/显示新通知（重复调用替换）。
    /// </summary>
    private void ShowOnUiThread(string title, string text, NotificationType type)
    {
        CloseInternal();

        var n = LibNotify.CreateNotification(title, text);
        if (n == IntPtr.Zero)
            return;
        _notification = n;
        LibNotify.SetUrgency(n, MapUrgency(type));
        _closedHandlerId = GtkNative.ConnectSignal(n, "closed", _closedTrampoline, default);
        LibNotify.ShowNotification(n);
    }

    /// <summary>
    /// 关闭并释放当前通知实例（幂等；closed 信号已释放时跳过）。须在主循环线程。
    /// </summary>
    private void CloseInternal()
    {
        var n = _notification;
        if (n == IntPtr.Zero)
            return;
        _notification = IntPtr.Zero;
        if (_closedHandlerId != 0)
        {
            GtkNative.DisconnectSignal(n, _closedHandlerId);
            _closedHandlerId = 0;
        }
        LibNotify.CloseNotification(n); // 已关闭的关闭请求静默失败，无害
        GtkNative.ObjectUnref(n);
    }

    /// <summary>
    /// closed 信号 trampoline：关闭方式为 dismissed（用户点击/关闭）→ Clicked；
    /// 无论何种关闭都释放当前实例（closed 后对象不再有用）。
    /// </summary>
    private static void OnClosed(IntPtr notification, IntPtr userData)
    {
        try
        {
            if (LibNotify.GetClosedReason(notification) == LibNotify.ClosedReasonDismissed)
                Instance.Clicked?.Invoke();

            if (Instance._notification == notification)
            {
                Instance._notification = IntPtr.Zero;
                Instance._closedHandlerId = 0;
                GtkNative.ObjectUnref(notification);
            }
        }
        catch
        {
            // 通知已销毁 / 版本探测失败等，忽略
        }
    }

    /// <summary>
    /// 把通知类型映射为 libnotify urgency（Info/Warning → NORMAL，Error → CRITICAL）。
    /// </summary>
    /// <param name="type">通知类型。</param>
    private static int MapUrgency(NotificationType type) => type switch
    {
        NotificationType.Error => LibNotify.UrgencyCritical,
        _ => LibNotify.UrgencyNormal,
    };

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void NotifyClosedCallback(IntPtr notification, IntPtr userData);
}
