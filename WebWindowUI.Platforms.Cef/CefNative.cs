using System;
using System.Runtime.InteropServices;
using System.Text;

namespace WebWindowUI.Cef;

/// <summary>
/// 手写 CEF C API 绑定（cef.h → C# P/Invoke，钉版 CEF 151.3.16）。核心事实：
///  - CEF 对象是扁平结构（size + 引用计数函数指针 + 方法函数指针直接顺序排布），无 vtable 间接；
///  - 对象方法指针为 CEF_CALLBACK = __stdcall；顶层导出函数为普通 C 函数 = cdecl（x64 上二者无差，语义对齐声明）；
///  - cef_string_t = { char16_t* str; size_t length; dtor }，UTF-16。
/// 布局对照权威源 = 同版本发行包 include/capi/*.h（本文件逐个字段转写，顺序即偏移）。
/// </summary>
internal static partial class CefNative
{
    // ===== cef_scheme_options_t =====
    internal const int SchemeOptionStandard = 1 << 0;
    internal const int SchemeOptionLocal = 1 << 1;
    internal const int SchemeOptionDisplayIsolated = 1 << 2;
    internal const int SchemeOptionSecure = 1 << 3;
    internal const int SchemeOptionCorsEnabled = 1 << 4;
    internal const int SchemeOptionCspBypassing = 1 << 5;
    internal const int SchemeOptionFetchEnabled = 1 << 6;

    // ===== kernel32（进程镜像句柄，cef_main_args_t.instance）=====

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    internal static extern IntPtr GetModuleHandle(string? lpModuleName);

    // ===== 顶层导出（libcef.dll，cdecl）=====

    [DllImport("libcef", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int cef_execute_process(ref CefMainArgs args, IntPtr application, IntPtr windows_sandbox_info);

    [DllImport("libcef", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int cef_initialize(ref CefMainArgs args, ref CefSettings settings, IntPtr application, IntPtr windows_sandbox_info);

    [DllImport("libcef", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void cef_shutdown();

    [DllImport("libcef", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void cef_do_message_loop_work();

    [DllImport("libcef", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int cef_get_exit_code();

    /// <summary>
    /// 单线程 CEF 消息循环（multi_threaded_message_loop=false）：Win32 GetMessage 循环，每次 dispatch 后调
    /// cef_do_message_loop_work() 让 CEF 处理自己的消息——CEF UI 线程 == 本线程（主线程）。
    /// 队列空时 GetMessage 阻塞（CEF 会投 Windows 消息唤醒），符合 CEF「消息循环空闲时给 CEF 干活机会」的要求。
    /// 收到 WM_QUIT（最后一个窗口关闭）返回；调用方随后同线程 cef_shutdown()。
    /// </summary>
    internal static void RunMessageLoop()
    {
        Win32.MSG msg;
        while (Win32.GetMessageW(out msg, IntPtr.Zero, 0, 0) > 0)
        {
            Win32.TranslateMessage(ref msg);
            Win32.DispatchMessageW(ref msg);
            cef_do_message_loop_work();
        }
    }

    [DllImport("libcef", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int cef_browser_host_create_browser(ref CefWindowInfo window_info, IntPtr client, ref CefString url, IntPtr settings, IntPtr extra_info, IntPtr request_context);

    [DllImport("libcef", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int cef_register_scheme_handler_factory(ref CefString scheme_name, ref CefString domain_name, IntPtr factory);

    // ===== 字符串助手（UTF-16）=====

    [DllImport("libcef", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int cef_string_utf16_set(IntPtr src, nuint src_len, ref CefString output, int copy);

    [DllImport("libcef", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void cef_string_utf16_clear(ref CefString str);

    [DllImport("libcef", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void cef_string_userfree_utf16_free(IntPtr str);

    // ===== CefString：用 copy=1 让 CEF 自持缓冲，调用方事后 cef_string_utf16_clear =====

    internal static CefString CreateString(string? value)
    {
        var s = new CefString();
        if (value is null)
            return s;
        // 经临时 native 缓冲设置（copy=1：CEF 自己拷贝、自持内存，返回后即可释放临时缓冲）
        var bytes = Encoding.Unicode.GetBytes(value);
        var tmp = Marshal.AllocHGlobal(Math.Max(bytes.Length, 1));
        try
        {
            Marshal.Copy(bytes, 0, tmp, bytes.Length);
            cef_string_utf16_set(tmp, (nuint)value.Length, ref s, 1);
        }
        finally
        {
            Marshal.FreeHGlobal(tmp);
        }
        return s;
    }

    /// <summary>释放 CreateString(copy=1) 产生的 CEF 自持缓冲。勿对 borrowed/空结构调用。</summary>
    internal static void FreeString(ref CefString s)
    {
        if (s.Str != IntPtr.Zero)
            cef_string_utf16_clear(ref s);
        s = default;
    }

    /// <summary>读取 borrowed cef_string_t（不释放）。</summary>
    internal static string? ReadString(ref CefString s)
        => s.Str == IntPtr.Zero || s.Length == 0 ? null : Marshal.PtrToStringUni(s.Str, (int)s.Length);

    /// <summary>读取 userfree 字符串（返回值须显式释放）。</summary>
    internal static string? ReadUserfree(IntPtr userfree)
    {
        if (userfree == IntPtr.Zero)
            return null;
        try
        {
            var s = Marshal.PtrToStructure<CefString>(userfree);
            return s.Str == IntPtr.Zero || s.Length == 0 ? null : Marshal.PtrToStringUni(s.Str, (int)s.Length);
        }
        finally
        {
            cef_string_userfree_utf16_free(userfree);
        }
    }

    /// <summary>把 .NET 字符串写入已分配的 borrowed 缓冲（copy=0，缓冲区须保持存活到 CEF 消费完）。</summary>
    internal static CefString BorrowedString(string value)
    {
        var bytes = Encoding.Unicode.GetBytes(value);
        var h = Marshal.AllocHGlobal(bytes.Length);
        Marshal.Copy(bytes, 0, h, bytes.Length);
        var s = new CefString();
        cef_string_utf16_set(h, (nuint)value.Length, ref s, 0);
        return s;
    }
}

/// <summary>cef_string_t（UTF-16）。3 指针 = 24 字节（x64）。</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct CefString
{
    public IntPtr Str;
    public nuint Length;
    public IntPtr Dtor;
}

/// <summary>cef_rect_t：{ int x, y, width, height } = 16 字节。</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct CefRect
{
    public int X;
    public int Y;
    public int Width;
    public int Height;
}

/// <summary>cef_main_args_t（Windows）：{ HINSTANCE instance } = 8 字节。</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct CefMainArgs
{
    public IntPtr Instance;
}

/// <summary>
/// cef_settings_t（CEF 151.3.16，全序转写 include/internal/cef_types.h:204）。31 字段，Size=448（x64）。
/// 字段顺序即偏移，任何增删都会让 cef_initialize 静默失败（内置 size 检查）。
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct CefSettings
{
    public ulong Size;
    public int NoSandbox;
    public CefString BrowserSubprocessPath;
    public CefString FrameworkDirPath;
    public CefString MainBundlePath;
    public int MultiThreadedMessageLoop;
    public int ExternalMessagePump;
    public int WindowlessRenderingEnabled;
    public int CommandLineArgsDisabled;
    public CefString CachePath;
    public CefString RootCachePath;
    public int PersistSessionCookies;
    public CefString UserAgent;
    public CefString UserAgentProduct;
    public CefString Locale;
    public CefString LogFile;
    public int LogSeverity;
    public int LogItems;
    public CefString JavascriptFlags;
    public CefString ResourcesDirPath;
    public CefString LocalesDirPath;
    public int RemoteDebuggingPort;
    public int UncaughtExceptionStackSize;
    public uint BackgroundColor;
    public CefString AcceptLanguageList;
    public CefString CookieableSchemesList;
    public int CookieableSchemesExcludeDefaults;
    public CefString ChromePolicyId;
    public int ChromeAppIconId;
    public int DisableSignalHandlers;
    public int UseViewsDefaultPopup; // CEF_API_ADDED(14600)，151 含
}

/// <summary>
/// cef_window_info_t（Windows，include/internal/cef_types_win.h:74）。12 字段，Size=112（x64）。
/// 窗口式渲染：ParentWindow=宿主 hwnd，Window=CEF 创建的子窗口句柄（由 CEF 回填）。
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct CefWindowInfo
{
    public ulong Size;
    public uint ExStyle;
    public CefString WindowName;
    public uint Style;
    public CefRect Bounds;
    public IntPtr ParentWindow;
    public IntPtr Menu;
    public int WindowlessRenderingEnabled;
    public int SharedTextureEnabled;
    public int ExternalBeginFrameEnabled;
    public IntPtr Window;
    public int RuntimeStyle; // cef_runtime_style_t，0=DEFAULT
}
