#pragma warning disable CA1416 // 原生 GTK 类型带 [SupportedOSPlatform]
namespace WebWindowUI.Linux;

/// <summary>
/// GTK3 手写 P/Invoke 层（libgtk-3.so.0）。libwebkit2gtk-4.1 是 GTK3 端口（本机 2.52.3 链接
/// libgtk-3.so.0），而 GirCore 只发布 GTK4 绑定（无 Gtk-3.0/Gdk-3.0），故窗口壳全手写。
/// 仅覆盖框架用到的窗口 API 子集，所有函数按 soname 引用、运行时不依赖 dev 符号链接。
///
/// 所有权约定：
///  - <see cref="SetChild"/> 用 gtk_container_add：收 WebView 的浮点引用（GTK3），窗口接管一个引用；
///  - 窗口句柄生命周期由 <see cref="GtkWindowHost"/> 管理（含 destroy 信号路由与释放）。
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

    /// <summary>初始化 GTK（gtk_init(null, null)：不处理命令行参数）。创建任何 GTK 控件前必须调用一次。</summary>
    public static void Initialize() => gtk_init(IntPtr.Zero, IntPtr.Zero);

    /// <summary>创建顶层窗口并设置标题/默认尺寸。</summary>
    public static IntPtr CreateWindow(string title, int width, int height)
    {
        var window = gtk_window_new(GtkWindowTopLevel);
        gtk_window_set_title(window, title);
        gtk_window_set_default_size(window, width, height);
        return window;
    }

    /// <summary>把 WebView（GtkWidget*）挂到窗口。gtk_container_add 收浮点引用，窗口接管一个引用。</summary>
    public static void SetChild(IntPtr window, IntPtr child) => gtk_container_add(window, child);

    /// <summary>显示窗口及其全部子控件（GTK3 子控件默认不可见，须递归 show）。</summary>
    public static void Show(IntPtr window) => gtk_widget_show_all(window);

    /// <summary>把窗口带到前台并聚焦。</summary>
    public static void Activate(IntPtr window) => gtk_window_present(window);

    public static void Hide(IntPtr window) => gtk_widget_hide(window);

    /// <summary>关闭窗口（close-request → 默认处理器 destroy → destroy 信号）。</summary>
    public static void Close(IntPtr window) => gtk_window_close(window);

    public static void SetTitle(IntPtr window, string title) => gtk_window_set_title(window, title);
}
