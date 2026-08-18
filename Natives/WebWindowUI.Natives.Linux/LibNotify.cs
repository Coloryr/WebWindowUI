using System.Runtime.InteropServices;

namespace WebWindowUI.Natives.Linux;

/// <summary>
/// libnotify P/Invoke 层：libnotify.so.7（0.8.x，Ubuntu 22.04+/Debian 12+）优先、回退
/// libnotify.so.4（0.7.x）。两版本导出符号一致，启动时 dlopen 探测（镜像 WebKit2Native 的
/// libsoup 双 API 惰性加载模式），只加载命中版本、只调被选中那套。
/// </summary>
internal static partial class LibNotify
{
    private const string Lib7 = "libnotify.so.7";
    private const string Lib4 = "libnotify.so.4";
    private const string DlLib = "libdl.so.2";

    // NotifyUrgency 枚举（notify.h）：LOW = 0 / NORMAL = 1 / CRITICAL = 2
    internal const int UrgencyNormal = 1;
    internal const int UrgencyCritical = 2;

    // 关闭原因（notify.h）：EXPIRED = 1 / DISMISSED = 2 / API_ERROR = 3
    internal const int ClosedReasonDismissed = 2;

    /// <summary>
    /// libnotify 是否可用（探测成功且 notify_init 成功）。
    /// </summary>
    internal static bool IsAvailable { get; private set; }

    private static bool _useNew = true;

    [LibraryImport(DlLib, EntryPoint = "dlopen")]
    private static partial IntPtr dlopen([MarshalAs(UnmanagedType.LPUTF8Str)] string filename, int flags);

    [LibraryImport(Lib7, EntryPoint = "notify_init")]
    [return: MarshalAs(UnmanagedType.I4)]
    private static partial bool notify_init_7([MarshalAs(UnmanagedType.LPUTF8Str)] string appName);

    [LibraryImport(Lib4, EntryPoint = "notify_init")]
    [return: MarshalAs(UnmanagedType.I4)]
    private static partial bool notify_init_4([MarshalAs(UnmanagedType.LPUTF8Str)] string appName);

    [LibraryImport(Lib7, EntryPoint = "notify_notification_new")]
    private static partial IntPtr notify_notification_new_7(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string summary,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string body,
        IntPtr icon);

    [LibraryImport(Lib4, EntryPoint = "notify_notification_new")]
    private static partial IntPtr notify_notification_new_4(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string summary,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string body,
        IntPtr icon);

    [LibraryImport(Lib7, EntryPoint = "notify_notification_set_urgency")]
    private static partial void notify_notification_set_urgency_7(IntPtr notification, int urgency);

    [LibraryImport(Lib4, EntryPoint = "notify_notification_set_urgency")]
    private static partial void notify_notification_set_urgency_4(IntPtr notification, int urgency);

    [LibraryImport(Lib7, EntryPoint = "notify_notification_show")]
    [return: MarshalAs(UnmanagedType.I4)]
    private static partial bool notify_notification_show_7(IntPtr notification, IntPtr error);

    [LibraryImport(Lib4, EntryPoint = "notify_notification_show")]
    [return: MarshalAs(UnmanagedType.I4)]
    private static partial bool notify_notification_show_4(IntPtr notification, IntPtr error);

    [LibraryImport(Lib7, EntryPoint = "notify_notification_close")]
    [return: MarshalAs(UnmanagedType.I4)]
    private static partial bool notify_notification_close_7(IntPtr notification, IntPtr error);

    [LibraryImport(Lib4, EntryPoint = "notify_notification_close")]
    [return: MarshalAs(UnmanagedType.I4)]
    private static partial bool notify_notification_close_4(IntPtr notification, IntPtr error);

    [LibraryImport(Lib7, EntryPoint = "notify_notification_get_closed_reason")]
    private static partial int notify_notification_get_closed_reason_7(IntPtr notification);

    [LibraryImport(Lib4, EntryPoint = "notify_notification_get_closed_reason")]
    private static partial int notify_notification_get_closed_reason_4(IntPtr notification);

    /// <summary>
    /// 探测 libnotify 版本并初始化；失败标记不可用（Show/Close 静默跳过）。
    /// dlopen 探测后不 dlclose：库保持加载，LibraryImport 后续解析复用已加载实例。
    /// </summary>
    internal static void Initialize()
    {
        IntPtr handle = dlopen(Lib7, 2 /* RTLD_NOW */);
        if (handle == IntPtr.Zero)
        {
            handle = dlopen(Lib4, 2);
            _useNew = false;
        }
        if (handle == IntPtr.Zero)
            return;
        IsAvailable = NotifyInit("WebWindowUI");
    }

    /// <summary>
    /// 初始化 libnotify（app 名）。
    /// </summary>
    internal static bool NotifyInit(string appName)
        => _useNew ? notify_init_7(appName) : notify_init_4(appName);

    /// <summary>
    /// 创建通知（icon 传 null 用默认图标）。
    /// </summary>
    internal static IntPtr CreateNotification(string summary, string body)
        => _useNew ? notify_notification_new_7(summary, body, IntPtr.Zero)
                   : notify_notification_new_4(summary, body, IntPtr.Zero);

    /// <summary>
    /// 设置通知紧急程度（0 LOW / 1 NORMAL / 2 CRITICAL）。
    /// </summary>
    internal static void SetUrgency(IntPtr notification, int urgency)
    {
        if (_useNew)
            notify_notification_set_urgency_7(notification, urgency);
        else
            notify_notification_set_urgency_4(notification, urgency);
    }

    /// <summary>
    /// 显示通知。
    /// </summary>
    internal static void ShowNotification(IntPtr notification)
    {
        if (_useNew)
            notify_notification_show_7(notification, IntPtr.Zero);
        else
            notify_notification_show_4(notification, IntPtr.Zero);
    }

    /// <summary>
    /// 关闭通知。
    /// </summary>
    internal static void CloseNotification(IntPtr notification)
    {
        if (_useNew)
            notify_notification_close_7(notification, IntPtr.Zero);
        else
            notify_notification_close_4(notification, IntPtr.Zero);
    }

    /// <summary>
    /// 取关闭原因（1 超时 / 2 用户关闭 / 3 错误）。
    /// </summary>
    internal static int GetClosedReason(IntPtr notification)
        => _useNew ? notify_notification_get_closed_reason_7(notification)
                   : notify_notification_get_closed_reason_4(notification);
}
