namespace WebWindowUI.Natives.Linux;

/// <summary>
/// GTK3 手写 P/Invoke 层（libgtk-3.so.0，GirCore 无 GTK3 绑定故窗口壳全手写）。
/// <see cref="SetChild"/> 用 gtk_container_add 收浮点引用（窗口接管一个引用）；窗口句柄生命周期由
/// <see cref="LinuxNativeWindow"/> 管理。
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

    /// <summary>
    /// 隐藏窗口（不销毁）。
    /// </summary>
    /// <param name="window">窗口指针。</param>
    public static void Hide(IntPtr window) => gtk_widget_hide(window);

    /// <summary>
    /// 关闭窗口（close-request → 默认处理器 destroy → destroy 信号）。
    /// </summary>
    /// <param name="window">窗口指针。</param>
    public static void Close(IntPtr window) => gtk_window_close(window);

    /// <summary>
    /// 修改标题。
    /// </summary>
    /// <param name="window">窗口指针。</param>
    /// <param name="title">新标题。</param>
    public static void SetTitle(IntPtr window, string title) => gtk_window_set_title(window, title);

    /// <summary>
    /// 取窗口当前尺寸（GTK3 中 gtk_window_get_size 已废弃但可用；返回的是窗口外框尺寸）。
    /// </summary>
    public static void GetSize(IntPtr window, out int width, out int height) => gtk_window_get_size(window, out width, out height);

    // ------------------------------------------------------------------
    // 窗口状态面（libgtk-3 / libgdk-3）：装饰/状态/位置/尺寸/几何约束/屏幕。
    // GTK 窗口 API 只允许主线程访问，由 LinuxNativeWindow 经 LinuxWindow 的主线程 marshal 调用。
    // ------------------------------------------------------------------

    // GdkWindowTypeHint（gdkenums.h）：GDK_WINDOW_TYPE_HINT_NORMAL = 0 / DIALOG = 1
    public const int GdkWindowTypeHintNormal = 0;
    public const int GdkWindowTypeHintDialog = 1;

    // GdkWindowState（gdkenums.h）
    public const int GdkWindowStateIconified = 1 << 1;
    public const int GdkWindowStateMaximized = 1 << 2;
    public const int GdkWindowStateFullscreen = 1 << 4;

    // GdkWindowHints（gdkgeometry.h）：GDK_HINT_MIN_SIZE / GDK_HINT_MAX_SIZE
    public const int GdkHintMinSize = 1 << 1;
    public const int GdkHintMaxSize = 1 << 2;

    [StructLayout(LayoutKind.Sequential)]
    public struct GdkGeometry
    {
        public int min_width;
        public int min_height;
        public int max_width;
        public int max_height;
        public int base_width;
        public int base_height;
        public int width_inc;
        public int height_inc;
        public double min_aspect;
        public double max_aspect;
        public int win_gravity;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct GdkRectangle
    {
        public int x;
        public int y;
        public int width;
        public int height;
    }

    [LibraryImport(GtkLib, EntryPoint = "gtk_window_set_decorated")]
    private static partial void gtk_window_set_decorated(IntPtr window, [MarshalAs(UnmanagedType.I4)] bool setting);

    [LibraryImport(GtkLib, EntryPoint = "gtk_window_set_resizable")]
    private static partial void gtk_window_set_resizable(IntPtr window, [MarshalAs(UnmanagedType.I4)] bool resizable);

    [LibraryImport(GtkLib, EntryPoint = "gtk_window_move")]
    private static partial void gtk_window_move(IntPtr window, int x, int y);

    [LibraryImport(GtkLib, EntryPoint = "gtk_window_get_position")]
    private static partial void gtk_window_get_position(IntPtr window, out int x, out int y);

    [LibraryImport(GtkLib, EntryPoint = "gtk_window_resize")]
    private static partial void gtk_window_resize(IntPtr window, int width, int height);

    [LibraryImport(GtkLib, EntryPoint = "gtk_window_iconify")]
    private static partial void gtk_window_iconify(IntPtr window);

    [LibraryImport(GtkLib, EntryPoint = "gtk_window_deiconify")]
    private static partial void gtk_window_deiconify(IntPtr window);

    [LibraryImport(GtkLib, EntryPoint = "gtk_window_maximize")]
    private static partial void gtk_window_maximize(IntPtr window);

    [LibraryImport(GtkLib, EntryPoint = "gtk_window_unmaximize")]
    private static partial void gtk_window_unmaximize(IntPtr window);

    [LibraryImport(GtkLib, EntryPoint = "gtk_window_fullscreen")]
    private static partial void gtk_window_fullscreen(IntPtr window);

    [LibraryImport(GtkLib, EntryPoint = "gtk_window_unfullscreen")]
    private static partial void gtk_window_unfullscreen(IntPtr window);

    [LibraryImport(GtkLib, EntryPoint = "gtk_window_is_active")]
    [return: MarshalAs(UnmanagedType.I4)]
    private static partial bool gtk_window_is_active(IntPtr window);

    [LibraryImport(GtkLib, EntryPoint = "gtk_window_set_skip_taskbar_hint")]
    private static partial void gtk_window_set_skip_taskbar_hint(IntPtr window, [MarshalAs(UnmanagedType.I4)] bool setting);

    [LibraryImport(GtkLib, EntryPoint = "gtk_window_set_type_hint")]
    private static partial void gtk_window_set_type_hint(IntPtr window, int hint);

    [LibraryImport(GtkLib, EntryPoint = "gtk_window_set_geometry_hints")]
    private static partial void gtk_window_set_geometry_hints(IntPtr window, IntPtr geometryWidget, ref GdkGeometry geometry, int geomMask);

    [LibraryImport(GtkLib, EntryPoint = "gtk_widget_get_window")]
    private static partial IntPtr gtk_widget_get_window(IntPtr widget);

    [LibraryImport(GdkLib, EntryPoint = "gdk_screen_get_default")]
    private static partial IntPtr gdk_screen_get_default();

    [LibraryImport(GdkLib, EntryPoint = "gdk_screen_get_monitor_count")]
    private static partial int gdk_screen_get_monitor_count(IntPtr screen);

    [LibraryImport(GdkLib, EntryPoint = "gdk_screen_get_monitor_geometry")]
    private static partial void gdk_screen_get_monitor_geometry(IntPtr screen, int monitorNum, out GdkRectangle dest);

    [LibraryImport(GdkLib, EntryPoint = "gdk_screen_get_monitor_at_window")]
    private static partial int gdk_screen_get_monitor_at_window(IntPtr screen, IntPtr window);

    [LibraryImport(GdkLib, EntryPoint = "gdk_window_get_state")]
    private static partial int gdk_window_get_state(IntPtr window);

    /// <summary>
    /// 设置窗口装饰（None 无标题栏；GTK3 装饰是二元，Border/Full 均为带标题栏）。
    /// </summary>
    public static void SetDecorated(IntPtr window, bool decorated) => gtk_window_set_decorated(window, decorated);

    /// <summary>
    /// 设置窗口是否可调整大小。
    /// </summary>
    public static void SetResizable(IntPtr window, bool resizable) => gtk_window_set_resizable(window, resizable);

    /// <summary>
    /// 移动窗口（屏幕坐标）。
    /// </summary>
    public static void Move(IntPtr window, int x, int y) => gtk_window_move(window, x, y);

    /// <summary>
    /// 取窗口位置（屏幕坐标）。
    /// </summary>
    public static void GetPosition(IntPtr window, out int x, out int y) => gtk_window_get_position(window, out x, out y);

    /// <summary>
    /// 设置窗口尺寸。
    /// </summary>
    public static void Resize(IntPtr window, int width, int height) => gtk_window_resize(window, width, height);

    /// <summary>
    /// 最小化窗口。
    /// </summary>
    public static void Iconify(IntPtr window) => gtk_window_iconify(window);

    /// <summary>
    /// 还原最小化窗口。
    /// </summary>
    public static void Deiconify(IntPtr window) => gtk_window_deiconify(window);

    /// <summary>
    /// 最大化窗口。
    /// </summary>
    public static void Maximize(IntPtr window) => gtk_window_maximize(window);

    /// <summary>
    /// 还原最大化窗口。
    /// </summary>
    public static void Unmaximize(IntPtr window) => gtk_window_unmaximize(window);

    /// <summary>
    /// 进入全屏。
    /// </summary>
    public static void Fullscreen(IntPtr window) => gtk_window_fullscreen(window);

    /// <summary>
    /// 退出全屏。
    /// </summary>
    public static void Unfullscreen(IntPtr window) => gtk_window_unfullscreen(window);

    /// <summary>
    /// 窗口当前是否活动。
    /// </summary>
    public static bool IsActive(IntPtr window) => gtk_window_is_active(window);

    /// <summary>
    /// 设置任务栏隐藏提示（skip_taskbar_hint=true 不出现在任务栏）。
    /// </summary>
    public static void SetSkipTaskbarHint(IntPtr window, bool setting) => gtk_window_set_skip_taskbar_hint(window, setting);

    /// <summary>
    /// 设置窗口类型提示（对话框式窗口用 DIALOG）。
    /// </summary>
    public static void SetTypeHint(IntPtr window, int hint) => gtk_window_set_type_hint(window, hint);

    /// <summary>
    /// 设置窗口几何约束（min/max 尺寸；geometry_widget 传 NULL 表示对窗口自身）。
    /// </summary>
    public static void SetGeometryHints(IntPtr window, ref GdkGeometry geometry, int geomMask)
        => gtk_window_set_geometry_hints(window, IntPtr.Zero, ref geometry, geomMask);

    /// <summary>
    /// 取窗口的 GdkWindow（未 realized 前为 null）。
    /// </summary>
    public static IntPtr GetGdkWindow(IntPtr widget) => gtk_widget_get_window(widget);

    /// <summary>
    /// 取默认屏幕。
    /// </summary>
    public static IntPtr GetScreen() => gdk_screen_get_default();

    /// <summary>
    /// 取默认屏幕显示器数。
    /// </summary>
    public static int GetMonitorCount() => gdk_screen_get_monitor_count(gdk_screen_get_default());

    /// <summary>
    /// 取指定显示器的几何（宽高即分辨率）。
    /// </summary>
    public static void GetMonitorGeometry(int monitorNum, out GdkRectangle dest)
        => gdk_screen_get_monitor_geometry(gdk_screen_get_default(), monitorNum, out dest);

    /// <summary>
    /// 取窗口所在显示器序号。
    /// </summary>
    public static int GetMonitorAtWindow(IntPtr gdkWindow)
        => gdk_screen_get_monitor_at_window(gdk_screen_get_default(), gdkWindow);

    /// <summary>
    /// 取 GdkWindow 当前状态位（GdkWindowState 标志）。
    /// </summary>
    public static int GetWindowState(IntPtr gdkWindow) => gdk_window_get_state(gdkWindow);

    // ------------------------------------------------------------------
    // 对话框（消息框 + 文件选择）
    // ------------------------------------------------------------------

    private const string GLibLib = "libglib-2.0.so.0";

    // gtktypes.h：GtkResponseType.OK = -5、GtkFileChooserAction（Open=0 / Save=1 / SelectFolder=2）
    private const int GtkResponseOk = -5;
    private const int GtkFileChooserActionOpen = 0;
    private const int GtkFileChooserActionSave = 1;
    private const int GtkFileChooserActionSelectFolder = 2;

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

    /// <summary>
    /// 目录选择对话框（GtkFileChooserNative，ACTION_SELECT_FOLDER，单选）。
    /// 返回 null = 取消。
    /// </summary>
    public static string? OpenFolderDialog(string title, string? initialDirectory)
    {
        var chooser = gtk_file_chooser_native_new(title, IntPtr.Zero, GtkFileChooserActionSelectFolder, null, null);
        if (chooser == IntPtr.Zero)
            return null;
        try
        {
            if (initialDirectory is not null)
                gtk_file_chooser_set_current_folder(chooser, initialDirectory);

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
    // 剪贴板（libgtk-3 / libgdk-3）：文本 + set_with_data 回调机制（HTML / uri-list）。
    // 全部调用须在 GTK 主线程（gtk_clipboard_* 非线程安全）。
    // ------------------------------------------------------------------

    // GDK_SELECTION_CLIPBOARD 内部原子号（gdktypes.h，= 69）
    private static readonly IntPtr GdkSelectionClipboard = new(69);

    private const string GdkLib = "libgdk-3.so.0";

    [StructLayout(LayoutKind.Sequential)]
    private struct GtkTargetEntry
    {
        public IntPtr target;   // gchar* 目标名
        public uint flags;      // 0 = 目标名是字符串
        public uint info;       // 应用自定义索引
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void GtkClipboardGetFunc(IntPtr clipboard, IntPtr selectionData, uint info, IntPtr userData);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void GtkClipboardClearFunc(IntPtr clipboard, IntPtr userData);

    [LibraryImport(GtkLib, EntryPoint = "gtk_clipboard_get")]
    private static partial IntPtr gtk_clipboard_get(IntPtr selection);

    [LibraryImport(GtkLib, EntryPoint = "gtk_clipboard_set_text")]
    private static partial void gtk_clipboard_set_text(IntPtr clipboard, [MarshalAs(UnmanagedType.LPUTF8Str)] string text, int len);

    [LibraryImport(GtkLib, EntryPoint = "gtk_clipboard_wait_for_text")]
    private static partial IntPtr gtk_clipboard_wait_for_text(IntPtr clipboard);

    [LibraryImport(GtkLib, EntryPoint = "gtk_clipboard_wait_for_uris")]
    private static partial IntPtr gtk_clipboard_wait_for_uris(IntPtr clipboard);

    [LibraryImport(GtkLib, EntryPoint = "gtk_clipboard_set_with_data")]
    [return: MarshalAs(UnmanagedType.I4)]
    private static partial bool gtk_clipboard_set_with_data(
        IntPtr clipboard, IntPtr targets, uint nTargets, GtkClipboardGetFunc getFunc, GtkClipboardClearFunc clearFunc, IntPtr userData);

    [LibraryImport(GtkLib, EntryPoint = "gtk_selection_data_get_target")]
    private static partial IntPtr gtk_selection_data_get_target(IntPtr selectionData);

    [LibraryImport(GtkLib, EntryPoint = "gtk_selection_data_set")]
    private static partial void gtk_selection_data_set(IntPtr selectionData, IntPtr type, int format, IntPtr data, int length);

    [LibraryImport(GdkLib, EntryPoint = "gdk_atom_intern")]
    private static partial IntPtr gdk_atom_intern([MarshalAs(UnmanagedType.LPUTF8Str)] string atomName, [MarshalAs(UnmanagedType.I4)] bool onlyIfExists);

    /// <summary>
    /// 保活的回调委托（另一程序请求数据时才触发，必须静态持有）。
    /// </summary>
    private static GtkClipboardGetFunc? _getFunc;

    /// <summary>
    /// 保活的清除回调委托。
    /// </summary>
    private static GtkClipboardClearFunc? _clearFunc;

    /// <summary>
    /// 写纯文本（target "UTF8_STRING"/"TEXT"，其他程序可粘贴）。
    /// </summary>
    public static void SetClipboardText(string text)
        => gtk_clipboard_set_text(gtk_clipboard_get(GdkSelectionClipboard), text, -1);

    /// <summary>
    /// 写自定义目标（HTML/uri-list/图片/自定义）字节：set_with_data 注册 get_func，另一程序请求时回调读出。
    /// 载荷句柄经 userData 传回调（每 owner 一份），易主时由 clear_func 释放，勿预释放。
    /// </summary>
    /// <param name="targetName">目标名（如 "text/html" / "image/png"）。</param>
    /// <param name="payload">目标字节载荷。</param>
    public static void SetClipboardTarget(string targetName, byte[] payload)
    {
        var cb = gtk_clipboard_get(GdkSelectionClipboard);

        var namePtr = g_strdup(targetName);
        var entry = new GtkTargetEntry { target = namePtr, flags = 0, info = 0 };
        var entryPtr = Marshal.AllocHGlobal(Marshal.SizeOf<GtkTargetEntry>());
        Marshal.StructureToPtr(entry, entryPtr, false);

        _getFunc = ClipboardGetFunc;
        _clearFunc = ClipboardClearFunc;
        var handle = GCHandle.Alloc(payload, GCHandleType.Pinned);
        bool ok = gtk_clipboard_set_with_data(cb, entryPtr, 1, _getFunc, _clearFunc, GCHandle.ToIntPtr(handle));

        Marshal.FreeHGlobal(entryPtr);
        g_free(namePtr);

        // 失败时 GTK 同步回调 clear_func（已释放 handle）；未回调则这里兜底释放
        if (!ok)
        {
            try { if (handle.IsAllocated) handle.Free(); } catch { }
        }
    }

    /// <summary>
    /// 读剪贴板纯文本；无文本返回 null。
    /// </summary>
    /// <returns>文本；不可用为 null。</returns>
    public static string? GetClipboardText()
    {
        var p = gtk_clipboard_wait_for_text(gtk_clipboard_get(GdkSelectionClipboard));
        if (p == IntPtr.Zero)
            return null;
        try { return Marshal.PtrToStringUTF8(p); }
        finally { g_free(p); }
    }

    /// <summary>
    /// 读剪贴板 file:// URI 列表；不可用返回 null。
    /// </summary>
    /// <returns>URI 列表；不可用为 null。</returns>
    public static List<string>? GetClipboardUris()
    {
        var array = gtk_clipboard_wait_for_uris(gtk_clipboard_get(GdkSelectionClipboard));
        if (array == IntPtr.Zero)
            return null;
        try
        {
            var result = new List<string>();
            for (int i = 0; ; i++)
            {
                var p = Marshal.ReadIntPtr(array, i * IntPtr.Size);
                if (p == IntPtr.Zero)
                    break;
                var s = Marshal.PtrToStringUTF8(p);
                if (s is not null)
                    result.Add(s);
            }
            return result.Count == 0 ? null : result;
        }
        finally
        {
            g_strfreev(array);
        }
    }

    /// <summary>
    /// set_with_data 数据服务回调：把本 owner 固定载荷（userData）拷进 selection data。
    /// </summary>
    private static void ClipboardGetFunc(IntPtr clipboard, IntPtr selectionData, uint info, IntPtr userData)
    {
        try
        {
            var handle = GCHandle.FromIntPtr(userData);
            if (!handle.IsAllocated)
                return;
            var payload = (byte[])handle.Target!;
            IntPtr target = gtk_selection_data_get_target(selectionData);
            gtk_selection_data_set(selectionData, target, 8, handle.AddrOfPinnedObject(), payload.Length);
        }
        catch
        {
            // 句柄已释放（owner 已被替换），忽略请求
        }
    }

    /// <summary>
    /// set_with_data 清除回调：释放本 owner 的固定句柄（剪贴板易主或被替换时，userData 对应本 owner）。
    /// </summary>
    private static void ClipboardClearFunc(IntPtr clipboard, IntPtr userData)
    {
        try
        {
            var handle = GCHandle.FromIntPtr(userData);
            if (handle.IsAllocated)
                handle.Free();
        }
        catch
        {
            // 已在 SetClipboardTarget 失败路径释放过，忽略
        }
        _getFunc = null;
        _clearFunc = null;
    }

    /// <summary>
    /// 读剪贴板可用目标名列表（含 image/*、自定义格式等；空剪贴板返回空表）。
    /// </summary>
    /// <returns>目标名列表。</returns>
    public static List<string> GetClipboardTargetNames()
    {
        var cb = gtk_clipboard_get(GdkSelectionClipboard);
        if (!gtk_clipboard_wait_for_targets(cb, out var targets, out int n) || targets == IntPtr.Zero)
            return [];
        try
        {
            var result = new List<string>(n);
            for (int i = 0; i < n; i++)
            {
                var atom = Marshal.ReadIntPtr(targets, i * IntPtr.Size);
                var namePtr = gdk_atom_name(atom);
                if (namePtr != IntPtr.Zero)
                {
                    try { result.Add(Marshal.PtrToStringUTF8(namePtr)!); }
                    finally { g_free(namePtr); }
                }
            }
            return result;
        }
        finally
        {
            g_free(targets);
        }
    }

    /// <summary>
    /// 按目标名读剪贴板字节（如 "image/png"）；目标不可用返回 null。
    /// </summary>
    /// <param name="targetName">目标名。</param>
    /// <returns>目标字节；不可用为 null。</returns>
    public static byte[]? GetClipboardTargetBytes(string targetName)
    {
        var cb = gtk_clipboard_get(GdkSelectionClipboard);
        IntPtr atom = gdk_atom_intern(targetName, true); // 只取已存在的原子
        if (atom == IntPtr.Zero)
            return null;
        IntPtr selection = gtk_clipboard_wait_for_contents(cb, atom);
        if (selection == IntPtr.Zero)
            return null;
        try
        {
            IntPtr data = gtk_selection_data_get_data(selection);
            int len = gtk_selection_data_get_length(selection);
            if (len <= 0 || data == IntPtr.Zero)
                return null;
            var bytes = new byte[len];
            Marshal.Copy(data, bytes, 0, len);
            return bytes;
        }
        finally
        {
            gtk_selection_data_free(selection);
        }
    }

    /// <summary>
    /// g_strdup：复制字符串（GtkTargetEntry 目标名由 gtk_target_list_new 拷贝，调用方随后 g_free 自己的副本）。
    /// </summary>
    [LibraryImport(GLibLib, EntryPoint = "g_strdup")]
    private static partial IntPtr g_strdup([MarshalAs(UnmanagedType.LPUTF8Str)] string str);

    [LibraryImport(GLibLib, EntryPoint = "g_strfreev")]
    private static partial void g_strfreev(IntPtr strArray);

    [LibraryImport(GtkLib, EntryPoint = "gtk_clipboard_wait_for_targets")]
    [return: MarshalAs(UnmanagedType.I4)]
    private static partial bool gtk_clipboard_wait_for_targets(IntPtr clipboard, out IntPtr targets, out int nTargets);

    [LibraryImport(GtkLib, EntryPoint = "gtk_clipboard_wait_for_contents")]
    private static partial IntPtr gtk_clipboard_wait_for_contents(IntPtr clipboard, IntPtr target);

    [LibraryImport(GtkLib, EntryPoint = "gtk_selection_data_get_data")]
    private static partial IntPtr gtk_selection_data_get_data(IntPtr selectionData);

    [LibraryImport(GtkLib, EntryPoint = "gtk_selection_data_get_length")]
    private static partial int gtk_selection_data_get_length(IntPtr selectionData);

    [LibraryImport(GtkLib, EntryPoint = "gtk_selection_data_free")]
    private static partial void gtk_selection_data_free(IntPtr selectionData);

    [LibraryImport(GdkLib, EntryPoint = "gdk_atom_name")]
    private static partial IntPtr gdk_atom_name(IntPtr atom);

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

    /// <summary>
    /// 连接 GObject 信号到托管回调。data 是调用方预先分配的 GCHandle（由调用方释放）；
    /// handler 委托必须被强引用保活。detail 支持 "signal::detail"。
    /// </summary>
    /// <param name="instance">信号源实例。</param>
    /// <param name="detailedSignal">信号名（可带 detail）。</param>
    /// <param name="handler">托管回调。</param>
    /// <param name="data">路由 GCHandle。</param>
    /// <returns>信号处理器 id。</returns>
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
