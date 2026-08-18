using System.Runtime.InteropServices;
using WebWindowUI.Core.Platform;

namespace WebWindowUI.Natives.Linux;

/// <summary>
/// Linux 系统通知（libnotify）：标题/正文/类型映射 urgency + 点击回调（closed 信号，
/// 关闭方式为 dismissed 时触发）。libnotify 版本（.so.7/.so.4）运行时探测，不可用时
/// Show/Close 静默跳过。GObject 调用须在主线程。
/// </summary>
public sealed class LinuxNotification : INotification
{
    /// <summary>
    /// 共享实例（平台 <c>Notification</c> 属性返回）。
    /// </summary>
    public static readonly LinuxNotification Instance = new();

    // 保活：native 只持有函数指针，委托实例必须被静态字段强引用。
    private static readonly NotifyClosedCallback _closedTrampoline = OnClosed;

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
    /// 显示系统通知（libnotify 不可用时静默跳过）。重复调用替换旧通知。
    /// </summary>
    /// <param name="title">标题。</param>
    /// <param name="text">正文。</param>
    /// <param name="type">通知类型（映射 libnotify urgency）。</param>
    public void Show(string title, string text, NotificationType type = NotificationType.Info)
    {
        if (!LibNotify.IsAvailable)
            return;

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
    /// 关闭当前通知（无通知时无效果）。
    /// </summary>
    public void Close()
    {
        if (!LibNotify.IsAvailable)
            return;
        CloseInternal();
    }

    /// <summary>
    /// 关闭并释放当前通知实例（幂等；closed 信号已释放时跳过）。
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
