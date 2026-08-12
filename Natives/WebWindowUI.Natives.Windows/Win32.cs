using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace WebWindowUI.Natives.Windows;

/// <summary>
/// Win32 API 的 P/Invoke 封装与共享窗口工具（Windows/CEF 平台共用）。
/// 隐藏消息窗口的 WM_RUN 调度经 <see cref="SetMarshalMessageHandler"/> 由各平台接入自己的
/// MessageLoopSynchronizationContext，本类不引用任何平台类型。
/// </summary>
public static partial class Win32
{
    // ---- 常量 ----
    public const uint CS_HREDRAW = 0x0001;
    public const uint CS_VREDRAW = 0x0002;
    public const int CW_USEDEFAULT = unchecked((int)0x80000000);
    public const int WS_OVERLAPPEDWINDOW = 0x00CF0000;
    public const int SW_HIDE = 0;
    public const int SW_SHOW = 5;
    public const int SW_RESTORE = 9;
    public const int COLOR_WINDOW = 5;
    public const int IDC_ARROW = 32512;
    public const int IDI_APPLICATION = 32512;
    public const uint WM_CLOSE = 0x0010;
    public const uint WM_DESTROY = 0x0002;
    public const uint WM_SIZE = 0x0005;
    public const uint WM_SETICON = 0x0080;
    public const int ICON_SMALL = 0;
    public const int ICON_BIG = 1;
    public const int IMAGE_ICON = 1;
    public const uint LR_LOADFROMFILE = 0x0010;
    public const uint LR_DEFAULTSIZE = 0x0040;
    public const uint WM_RUN = 0x8000; // WM_APP

    private const int HWND_MESSAGE = -3;

    private static IntPtr _marshalHwnd;
    private static WndProcDelegate _marshalWndProc = null!; // 保活，防止被 GC 回收
    private static Func<IntPtr, uint, IntPtr, IntPtr, IntPtr?>? _marshalHandler;

    /// <summary>跑 Win32 消息循环，直到收到 WM_QUIT 为止。</summary>
    public static void MessageLoop()
    {
        while (GetMessageW(out MSG msg, IntPtr.Zero, 0, 0) > 0)
        {
            TranslateMessage(ref msg);
            DispatchMessageW(ref msg);
        }
    }

    /// <summary>
    /// 创建（或复用）隐藏的消息窗口。
    /// 所有 async 延续都通过它调度回 UI 线程的消息循环。
    /// </summary>
    public static IntPtr GetOrCreateMarshalWindow(string windowClassName)
    {
        if (_marshalHwnd != IntPtr.Zero)
            return _marshalHwnd;

        _marshalWndProc = MarshalWndProc;
        var wc = new WNDCLASSEXW
        {
            cbSize = (uint)Marshal.SizeOf<WNDCLASSEXW>(),
            lpfnWndProc = _marshalWndProc,
            hInstance = GetModuleHandleW(null),
            lpszClassName = windowClassName,
        };
        if (RegisterClassExW(ref wc) == 0)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "注册消息窗口类失败 (RegisterClassExW)");

        _marshalHwnd = CreateWindowExW(
            0, windowClassName, "", 0,
            0, 0, 0, 0,
            (IntPtr)HWND_MESSAGE, IntPtr.Zero, GetModuleHandleW(null), IntPtr.Zero);
        if (_marshalHwnd == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "创建消息窗口失败 (CreateWindowExW)");
        return _marshalHwnd;
    }

    /// <summary>注册隐藏消息窗口的附加消息处理（各平台接自己的 WM_RUN → RunQueued）。返回 null 表示未处理，回落到 DefWindowProcW。</summary>
    public static void SetMarshalMessageHandler(Func<IntPtr, uint, IntPtr, IntPtr, IntPtr?>? handler)
        => _marshalHandler = handler;

    private static IntPtr MarshalWndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        var handled = _marshalHandler?.Invoke(hwnd, msg, wParam, lParam);
        return handled ?? DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    // ------------------------------------------------------------------
    // 委托 / 结构
    // ------------------------------------------------------------------

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    public delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    // WNDCLASSEXW 含委托/字符串字段，非 blittable，LibraryImport 不能直接封送（SYSLIB1051）。
    // 自定义封送器：委托→函数指针、字符串→UTF-16 指针，保持结构体公开形状不变、调用方零改动。
    // 注意：RegisterClassExW 复制类名，字符串指针调用后即可释放；但 stateless 封送器无释放钩子，
    // 每窗口类仅注册一次（进程级一次性，数十字节），可忽略。
    [CustomMarshaller(typeof(WNDCLASSEXW), MarshalMode.Default, typeof(WndClassExMarshaller))]
    internal static class WndClassExMarshaller
    {
        /// <summary>原生布局（blittable，字段序与 WNDCLASSEXW 一致）。</summary>
        [StructLayout(LayoutKind.Sequential)]
        internal struct Native
        {
            public uint cbSize;
            public uint style;
            public IntPtr lpfnWndProc;
            public int cbClsExtra;
            public int cbWndExtra;
            public IntPtr hInstance;
            public IntPtr hIcon;
            public IntPtr hCursor;
            public IntPtr hbrBackground;
            public IntPtr lpszMenuName;
            public IntPtr lpszClassName;
            public IntPtr hIconSm;
        }

        public static Native ConvertToUnmanaged(WNDCLASSEXW managed) => new()
        {
            cbSize = managed.cbSize,
            style = managed.style,
            lpfnWndProc = managed.lpfnWndProc is null ? IntPtr.Zero : Marshal.GetFunctionPointerForDelegate(managed.lpfnWndProc),
            cbClsExtra = managed.cbClsExtra,
            cbWndExtra = managed.cbWndExtra,
            hInstance = managed.hInstance,
            hIcon = managed.hIcon,
            hCursor = managed.hCursor,
            hbrBackground = managed.hbrBackground,
            lpszMenuName = managed.lpszMenuName is null ? IntPtr.Zero : Marshal.StringToCoTaskMemUni(managed.lpszMenuName),
            lpszClassName = managed.lpszClassName is null ? IntPtr.Zero : Marshal.StringToCoTaskMemUni(managed.lpszClassName),
            hIconSm = managed.hIconSm,
        };

        public static WNDCLASSEXW ConvertToManaged(Native unmanaged) => new()
        {
            // RegisterClassExW 不修改结构（const in），回读值调用方不使用；按指针重建仅满足封送器类型契约。
            cbSize = unmanaged.cbSize,
            style = unmanaged.style,
            lpfnWndProc = unmanaged.lpfnWndProc == IntPtr.Zero
                ? null!
                : Marshal.GetDelegateForFunctionPointer<WndProcDelegate>(unmanaged.lpfnWndProc),
            cbClsExtra = unmanaged.cbClsExtra,
            cbWndExtra = unmanaged.cbWndExtra,
            hInstance = unmanaged.hInstance,
            hIcon = unmanaged.hIcon,
            hCursor = unmanaged.hCursor,
            hbrBackground = unmanaged.hbrBackground,
            lpszMenuName = unmanaged.lpszMenuName == IntPtr.Zero
                ? null
                : Marshal.PtrToStringUni(unmanaged.lpszMenuName),
            lpszClassName = unmanaged.lpszClassName == IntPtr.Zero
                ? null!
                : Marshal.PtrToStringUni(unmanaged.lpszClassName)!,
            hIconSm = unmanaged.hIconSm,
        };
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct WNDCLASSEXW
    {
        public uint cbSize;
        public uint style;
        public WndProcDelegate lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public POINT pt;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    // ------------------------------------------------------------------
    // P/Invoke
    // ------------------------------------------------------------------

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial IntPtr GetModuleHandleW(string? lpModuleName);

    [LibraryImport("user32.dll")]
    public static partial ushort RegisterClassExW([MarshalUsing(typeof(WndClassExMarshaller))] ref WNDCLASSEXW lpWndClass);

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial IntPtr CreateWindowExW(
        uint dwExStyle,
        string lpClassName,
        string lpWindowName,
        int dwStyle,
        int x, int y, int nWidth, int nHeight,
        IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

    [LibraryImport("user32.dll")]
    public static partial IntPtr DefWindowProcW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [LibraryImport("user32.dll")]
    public static partial int GetMessageW(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool TranslateMessage(ref MSG lpMsg);

    [LibraryImport("user32.dll")]
    public static partial IntPtr DispatchMessageW(ref MSG lpMsg);

    [LibraryImport("user32.dll")]
    public static partial void PostQuitMessage(int nExitCode);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsIconic(IntPtr hWnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetForegroundWindow(IntPtr hWnd);

    [LibraryImport("user32.dll")]
    public static partial IntPtr SetFocus(IntPtr hWnd);

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetWindowTextW(IntPtr hWnd, string lpString);

    [LibraryImport("user32.dll")]
    public static partial IntPtr SendMessageW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial IntPtr LoadImageW(IntPtr hinst, string lpszName, int type, int cx, int cy, uint fuLoad);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DestroyIcon(IntPtr hIcon);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DestroyWindow(IntPtr hWnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetClientRect(IntPtr hWnd, out RECT lpRect);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool PostMessageW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial IntPtr LoadCursorW(IntPtr hInstance, int lpCursorName);

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial IntPtr LoadIconW(IntPtr hInstance, int lpIconName);
}
