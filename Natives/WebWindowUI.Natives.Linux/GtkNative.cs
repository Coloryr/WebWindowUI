namespace WebWindowUI.Natives.Linux;

/// <summary>
/// GTK3 手写 P/Invoke 层（libgtk-3.so.0）。libwebkit2gtk-4.1 是 GTK3 端口（本机 2.52.3 链接
/// libgtk-3.so.0），而 GirCore 只发布 GTK4 绑定（无 Gtk-3.0/Gdk-3.0），故窗口壳全手写。
/// 仅覆盖框架用到的窗口 API 子集，所有函数按 soname 引用、运行时不依赖 dev 符号链接。
///
/// 所有权约定：
///  - <see cref="SetChild"/> 用 gtk_container_add：收 WebView 的浮点引用（GTK3），窗口接管一个引用；
///  - 窗口句柄生命周期由 <see cref="LinuxNativeWindow"/> 管理（含 destroy 信号路由与释放）。
/// </summary>
internal static partial class GtkNative
{
    private const string GtkLib = "libgtk-3.so.0";

    // GtkWindowType 枚举：GTK_WINDOW_TOPLEVEL = 0
    private const int GtkWindowTopLevel = 0;

    [LibraryImport(GtkLib, EntryPoint = "gtk_init")]
    private static partial void gtk_init(IntPtr argc, IntPtr argv);

    [LibraryImport(GtkLib, EntryPoint = "gtk_window_new")]
    private static partial IntPtr gtk_window_new(int type);

    [LibraryImport(GtkLib, EntryPoint = "gtk_window_set_title")]
    private static partial void gtk_window_set_title(IntPtr window, [MarshalAs(UnmanagedType.LPUTF8Str)] string title);

    [LibraryImport(GtkLib, EntryPoint = "gtk_window_set_default_size")]
    private static partial void gtk_window_set_default_size(IntPtr window, int width, int height);

    [LibraryImport(GtkLib, EntryPoint = "gtk_window_get_size")]
    private static partial void gtk_window_get_size(IntPtr window, out int width, out int height);

    [LibraryImport(GtkLib, EntryPoint = "gtk_container_add")]
    private static partial void gtk_container_add(IntPtr container, IntPtr child);

    [LibraryImport(GtkLib, EntryPoint = "gtk_widget_show_all")]
    private static partial void gtk_widget_show_all(IntPtr widget);

    [LibraryImport(GtkLib, EntryPoint = "gtk_widget_hide")]
    private static partial void gtk_widget_hide(IntPtr widget);

    [LibraryImport(GtkLib, EntryPoint = "gtk_window_close")]
    private static partial void gtk_window_close(IntPtr window);

    [LibraryImport(GtkLib, EntryPoint = "gtk_window_present")]
    private static partial void gtk_window_present(IntPtr window);

    /// <summary>
    /// 初始化 GTK（gtk_init(null, null)：不处理命令行参数）。创建任何 GTK 控件前必须调用一次。
    /// </summary>
    public static void Initialize() => gtk_init(IntPtr.Zero, IntPtr.Zero);

    /// <summary>
    /// 创建顶层窗口并设置标题/默认尺寸。
    /// </summary>
    public static IntPtr CreateWindow(string title, int width, int height)
    {
        var window = gtk_window_new(GtkWindowTopLevel);
        gtk_window_set_title(window, title);
        gtk_window_set_default_size(window, width, height);
        return window;
    }

    /// <summary>
    /// 把 WebView（GtkWidget*）挂到窗口。gtk_container_add 收浮点引用，窗口接管一个引用。
    /// </summary>
    public static void SetChild(IntPtr window, IntPtr child) => gtk_container_add(window, child);

    /// <summary>
    /// 显示窗口及其全部子控件（GTK3 子控件默认不可见，须递归 show）。
    /// </summary>
    public static void Show(IntPtr window) => gtk_widget_show_all(window);

    /// <summary>
    /// 把窗口带到前台并聚焦。
    /// </summary>
    public static void Activate(IntPtr window) => gtk_window_present(window);

    public static void Hide(IntPtr window) => gtk_widget_hide(window);

    /// <summary>
    /// 关闭窗口（close-request → 默认处理器 destroy → destroy 信号）。
    /// </summary>
    public static void Close(IntPtr window) => gtk_window_close(window);

    public static void SetTitle(IntPtr window, string title) => gtk_window_set_title(window, title);

    /// <summary>
    /// 取窗口当前尺寸（GTK3 中 gtk_window_get_size 已废弃但可用；返回的是窗口外框尺寸）。
    /// </summary>
    public static void GetSize(IntPtr window, out int width, out int height) => gtk_window_get_size(window, out width, out height);

    // ------------------------------------------------------------------
    // 对话框（消息框 + 文件选择）
    // ------------------------------------------------------------------

    private const string GLibLib = "libglib-2.0.so.0";

    // gtktypes.h：GtkResponseType.OK = -5、GtkFileChooserAction（Open=0 / Save=1）
    private const int GtkResponseOk = -5;
    private const int GtkFileChooserActionOpen = 0;
    private const int GtkFileChooserActionSave = 1;

    [LibraryImport(GtkLib, EntryPoint = "gtk_dialog_new")]
    private static partial IntPtr gtk_dialog_new();

    [LibraryImport(GtkLib, EntryPoint = "gtk_dialog_get_content_area")]
    private static partial IntPtr gtk_dialog_get_content_area(IntPtr dialog);

    [LibraryImport(GtkLib, EntryPoint = "gtk_dialog_add_button")]
    private static partial IntPtr gtk_dialog_add_button(
        IntPtr dialog, [MarshalAs(UnmanagedType.LPUTF8Str)] string buttonText, int responseId);

    [LibraryImport(GtkLib, EntryPoint = "gtk_dialog_run")]
    private static partial int gtk_dialog_run(IntPtr dialog);

    [LibraryImport(GtkLib, EntryPoint = "gtk_label_new")]
    private static partial IntPtr gtk_label_new([MarshalAs(UnmanagedType.LPUTF8Str)] string text);

    [LibraryImport(GtkLib, EntryPoint = "gtk_widget_destroy")]
    private static partial void gtk_widget_destroy(IntPtr widget);

    [LibraryImport(GtkLib, EntryPoint = "gtk_file_chooser_native_new")]
    private static partial IntPtr gtk_file_chooser_native_new(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string title, IntPtr parent, int action,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string? acceptLabel,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string? cancelLabel);

    [LibraryImport(GtkLib, EntryPoint = "gtk_native_dialog_run")]
    private static partial int gtk_native_dialog_run(IntPtr nativeDialog);

    [LibraryImport(GtkLib, EntryPoint = "gtk_native_dialog_destroy")]
    private static partial void gtk_native_dialog_destroy(IntPtr nativeDialog);

    [LibraryImport(GtkLib, EntryPoint = "gtk_file_chooser_set_select_multiple")]
    private static partial void gtk_file_chooser_set_select_multiple(IntPtr chooser, [MarshalAs(UnmanagedType.I4)] bool selectMultiple);

    [LibraryImport(GtkLib, EntryPoint = "gtk_file_chooser_set_current_folder")]
    [return: MarshalAs(UnmanagedType.I4)]
    private static partial bool gtk_file_chooser_set_current_folder(IntPtr chooser, [MarshalAs(UnmanagedType.LPUTF8Str)] string filename);

    [LibraryImport(GtkLib, EntryPoint = "gtk_file_chooser_set_current_name")]
    private static partial void gtk_file_chooser_set_current_name(IntPtr chooser, [MarshalAs(UnmanagedType.LPUTF8Str)] string name);

    [LibraryImport(GtkLib, EntryPoint = "gtk_file_chooser_set_do_overwrite_confirmation")]
    private static partial void gtk_file_chooser_set_do_overwrite_confirmation(IntPtr chooser, [MarshalAs(UnmanagedType.I4)] bool doOverwriteConfirmation);

    [LibraryImport(GtkLib, EntryPoint = "gtk_file_chooser_get_filename")]
    private static partial IntPtr gtk_file_chooser_get_filename(IntPtr chooser);

    [LibraryImport(GtkLib, EntryPoint = "gtk_file_chooser_get_filenames")]
    private static partial IntPtr gtk_file_chooser_get_filenames(IntPtr chooser);

    [LibraryImport(GLibLib, EntryPoint = "g_slist_length")]
    private static partial uint g_slist_length(IntPtr list);

    [LibraryImport(GLibLib, EntryPoint = "g_slist_nth_data")]
    private static partial IntPtr g_slist_nth_data(IntPtr list, uint n);

    [LibraryImport(GLibLib, EntryPoint = "g_slist_free")]
    private static partial void g_slist_free(IntPtr list);

    [LibraryImport(GLibLib, EntryPoint = "g_free")]
    private static partial void g_free(IntPtr mem);

    /// <summary>
    /// 消息框：GtkDialog + 内容区 GtkLabel + OK 按钮。用非 varargs 的组合拼 GtkMessageDialog，
    /// 避开 gtk_message_dialog_new 的 C 变参（LibraryImport 不支持变参）。须在 GTK 主循环运行中调用。
    /// </summary>
    public static void ShowMessageBox(string title, string message)
    {
        var dialog = gtk_dialog_new();
        gtk_window_set_title(dialog, title);
        var content = gtk_dialog_get_content_area(dialog);
        var label = gtk_label_new(message);
        gtk_container_add(content, label);
        gtk_dialog_add_button(dialog, "OK", GtkResponseOk);
        gtk_widget_show_all(dialog);
        gtk_dialog_run(dialog);
        gtk_widget_destroy(dialog);
    }

    /// <summary>
    /// 文件选择对话框（GtkFileChooserNative，GTK ≥ 3.20；libwebkit2gtk-4.1 依赖 GTK3 ≥ 3.22）。
    /// 返回 null = 取消。filter 为 Windows 格式，Linux 暂不支持（忽略）。
    /// </summary>
    public static string[]? OpenFileDialog(string title, string? initialDirectory, bool allowMultiSelect)
    {
        var chooser = gtk_file_chooser_native_new(title, IntPtr.Zero, GtkFileChooserActionOpen, null, null);
        if (chooser == IntPtr.Zero)
            return null;
        try
        {
            gtk_file_chooser_set_select_multiple(chooser, allowMultiSelect);
            if (initialDirectory is not null)
                gtk_file_chooser_set_current_folder(chooser, initialDirectory);

            if (gtk_native_dialog_run(chooser) != GtkResponseOk)
                return null;

            if (!allowMultiSelect)
            {
                var single = gtk_file_chooser_get_filename(chooser);
                if (single == IntPtr.Zero)
                    return null;
                try { return new[] { Marshal.PtrToStringUTF8(single)! }; }
                finally { g_free(single); }
            }

            var list = gtk_file_chooser_get_filenames(chooser);
            if (list == IntPtr.Zero)
                return [];
            try
            {
                uint count = g_slist_length(list);
                var files = new List<string>((int)count);
                for (uint i = 0; i < count; i++)
                {
                    var p = g_slist_nth_data(list, i);
                    if (p != IntPtr.Zero)
                    {
                        files.Add(Marshal.PtrToStringUTF8(p)!);
                        g_free(p);
                    }
                }
                return files.ToArray();
            }
            finally
            {
                g_slist_free(list);
            }
        }
        finally
        {
            gtk_native_dialog_destroy(chooser);
        }
    }

    /// <summary>
    /// 保存对话框（GtkFileChooserNative，ACTION_SAVE + 覆盖确认）。
    /// 返回 null = 取消。filter/defaultExt 暂不支持（忽略）。
    /// </summary>
    public static string? SaveFileDialog(string title, string? defaultFileName)
    {
        var chooser = gtk_file_chooser_native_new(title, IntPtr.Zero, GtkFileChooserActionSave, null, null);
        if (chooser == IntPtr.Zero)
            return null;
        try
        {
            gtk_file_chooser_set_do_overwrite_confirmation(chooser, true);
            if (defaultFileName is not null)
                gtk_file_chooser_set_current_name(chooser, defaultFileName);

            if (gtk_native_dialog_run(chooser) != GtkResponseOk)
                return null;

            var p = gtk_file_chooser_get_filename(chooser);
            if (p == IntPtr.Zero)
                return null;
            try { return Marshal.PtrToStringUTF8(p); }
            finally { g_free(p); }
        }
        finally
        {
            gtk_native_dialog_destroy(chooser);
        }
    }

    // ------------------------------------------------------------------
    // GObject 信号（libgobject-2.0.so.0）：窗口 destroy/configure 信号桥。GTK 与 CEF 平台共用本层。
    // ------------------------------------------------------------------

    private const string GObjectLib = "libgobject-2.0.so.0";

    [LibraryImport(GObjectLib, EntryPoint = "g_signal_connect_data")]
    private static partial ulong g_signal_connect_data(
        IntPtr instance,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string detailedSignal,
        IntPtr handler,
        IntPtr data,
        IntPtr destroyData,
        uint connectFlags);

    [LibraryImport(GObjectLib, EntryPoint = "g_signal_handler_disconnect")]
    private static partial void g_signal_handler_disconnect(IntPtr instance, ulong handlerId);

    /// <summary>连接 GObject 信号到托管回调。data 是调用方预先分配的 GCHandle（由调用方释放）；
    /// handler 委托必须被强引用保活。detail 支持 "signal::detail"。</summary>
    public static ulong ConnectSignal(IntPtr instance, string detailedSignal, Delegate handler, GCHandle data)
        => g_signal_connect_data(instance, detailedSignal,
            Marshal.GetFunctionPointerForDelegate(handler), GCHandle.ToIntPtr(data), IntPtr.Zero, 0);

    /// <summary>
    /// 断开信号。实例已销毁时忽略错误。
    /// </summary>
    public static void DisconnectSignal(IntPtr instance, ulong handlerId)
    {
        if (handlerId != 0 && instance != IntPtr.Zero)
        {
            try { g_signal_handler_disconnect(instance, handlerId); }
            catch { /* 实例已销毁 */ }
        }
    }
}
