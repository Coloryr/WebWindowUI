using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace WebWindowUI.Natives.Windows;

/// <summary>
/// Win32 API 的 P/Invoke 封装与共享窗口工具（Windows/CEF 平台共用）。
/// 隐藏消息窗口的 WM_RUN 调度经 <see cref="SetMarshalMessageHandler"/> 由各平台接入自己的
/// MessageLoopSynchronizationContext，本类不引用任何平台类型。
/// </summary>
internal static partial class Win32
{
    public const uint CS_HREDRAW = 0x0001;
    public const uint CS_VREDRAW = 0x0002;
    public const int CW_USEDEFAULT = unchecked((int)0x80000000);
    public const int WS_OVERLAPPEDWINDOW = 0x00CF0000;
    public const int WS_CLIPCHILDREN = 0x02000000;
    public const uint WS_EX_APPWINDOW = 0x00040000;
    public const uint WS_EX_NOREDIRECTIONBITMAP = 0x00200000;
    public const uint WS_EX_WINDOWEDGE = 0x00000100;
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

    public const uint MB_OK = 0x00000000;
    public const uint MB_ICONINFORMATION = 0x00000040;
    public const uint MB_ICONWARNING = 0x00000030;
    public const uint MB_ICONERROR = 0x00000010;

    public const uint OFN_READONLY = 0x00000001;
    public const uint OFN_OVERWRITEPROMPT = 0x00000002;
    public const uint OFN_HIDEREADONLY = 0x00000004;
    public const uint OFN_NOCHANGEDIR = 0x00000008;
    public const uint OFN_SHOWHELP = 0x00000010;
    public const uint OFN_ENABLEHOOK = 0x00000020;
    public const uint OFN_ENABLETEMPLATE = 0x00000040;
    public const uint OFN_ENABLETEMPLATEHANDLE = 0x00000080;
    public const uint OFN_NOVALIDATE = 0x00000100;
    public const uint OFN_ALLOWMULTISELECT = 0x00000200;
    public const uint OFN_EXTENSIONDIFFERENT = 0x00000400;
    public const uint OFN_PATHMUSTEXIST = 0x00000800;
    public const uint OFN_FILEMUSTEXIST = 0x00001000;
    public const uint OFN_CREATEPROMPT = 0x00002000;
    public const uint OFN_SHAREAWARE = 0x00004000;
    public const uint OFN_NOREADONLYRETURN = 0x00008000;
    public const uint OFN_NOTESTFILECREATE = 0x00010000;
    public const uint OFN_NONETWORKBUTTON = 0x00020000;
    public const uint OFN_NOLONGNAMES = 0x00040000;
    public const uint OFN_EXPLORER = 0x00080000;
    public const uint OFN_NODEREFERENCELINKS = 0x00100000;
    public const uint OFN_LONGNAMES = 0x00200000;
    public const uint OFN_ENABLEINCLUDENOTIFY = 0x00400000;
    public const uint OFN_ENABLESIZING = 0x00800000;
    public const uint OFN_DONTADDTORECENT = 0x02000000;
    public const uint OFN_FORCESHOWHIDDEN = 0x10000000;

    public const int OFN_MULTISELECT_BUFFER = 32768;
    public const int OFN_SINGLE_SELECT_BUFFER = 4096;

    public const uint BIF_RETURNONLYFSDIRS = 0x00000001;
    public const uint BIF_NEWDIALOGSTYLE = 0x00000040;

    public const int HWND_MESSAGE = -3;

    public const uint NIN_BALLOONUSERCLICK = WM_USER + 5;
    public const uint NIM_ADD = 0x0000;
    public const uint NIM_MODIFY = 0x0001;
    public const uint NIM_DELETE = 0x0002;
    public const uint NIM_SETVERSION = 0x0004;
    public const uint NIF_MESSAGE = 0x0001;
    public const uint NIF_ICON = 0x0002;
    public const uint NIF_TIP = 0x0004;
    public const uint NIF_INFO = 0x0010;
    public const uint NIF_STATE = 0x0008;
    public const uint NIS_HIDDEN = 0x00000001;
    public const uint NIIF_INFO = 0x00000001;
    public const uint NIIF_WARNING = 0x00000002;
    public const uint NIIF_ERROR = 0x00000003;
    public const uint NOTIFYICON_VERSION = 0x3;
    public const uint NOTIFYICON_VERSION_4 = 0x4;
    public const uint WM_USER = 0x0400;
    public const uint WM_TRAYICON = WM_USER + 1;
    public const uint WM_LBUTTONDOWN = 0x0201;
    public const uint WM_LBUTTONUP = 0x0202;
    public const uint WM_LBUTTONDBLCLK = 0x0203;
    public const uint WM_RBUTTONDOWN = 0x0204;
    public const uint WM_RBUTTONUP = 0x0205;
    public const uint WM_MBUTTONDOWN = 0x0207;
    public const uint WM_MBUTTONUP = 0x0208;
    public const uint WM_MOUSEMOVE = 0x0200;
    public const uint WM_CONTEXTMENU = 0x007B;
    public const uint WM_COMMAND = 0x0111;
    public const uint WM_NULL = 0x0000;

    public const uint MF_STRING = 0x00000000;
    public const uint MF_SEPARATOR = 0x00000800;
    public const uint MF_POPUP = 0x00000010;
    public const uint MF_DISABLED = 0x00000002;
    public const uint MF_GRAYED = 0x00000001;
    public const uint MF_CHECKED = 0x00000008;
    public const uint MF_UNCHECKED = 0x00000000;
    public const uint MF_ENABLED = 0x00000000;
    public const uint MF_BYCOMMAND = 0x00000000;

    public const uint TPM_RIGHTBUTTON = 0x0002;
    public const uint TPM_NONOTIFY = 0x0080;
    public const uint TPM_RETURNCMD = 0x0100;

    public const uint CF_TEXT = 1;
    public const uint CF_BITMAP = 2;
    public const uint CF_DIB = 8;
    public const uint CF_UNICODETEXT = 13;
    public const uint CF_HDROP = 15;

    public const uint GMEM_MOVEABLE = 0x0002;
    public const uint GMEM_ZEROINIT = 0x0040;

    public const int GWL_STYLE = -16;
    public const int GWL_EXSTYLE = -20;

    public const int WS_OVERLAPPED = 0x00000000;
    public const int WS_CAPTION = 0x00C00000;
    public const int WS_SYSMENU = 0x00080000;
    public const int WS_THICKFRAME = 0x00040000;
    public const int WS_MINIMIZEBOX = 0x00020000;
    public const int WS_MAXIMIZEBOX = 0x00010000;
    public const int WS_MINIMIZE = 0x20000000;
    public const int WS_MAXIMIZE = 0x01000000;
    public const int WS_VISIBLE = 0x10000000;

    public const uint WS_EX_TOOLWINDOW = 0x00000080;
    public const uint WS_EX_DLGMODALFRAME = 0x00000001;

    public const int SW_MINIMIZE = 6;
    public const int SW_MAXIMIZE = 3;

    public const uint SWP_NOSIZE = 0x0001;
    public const uint SWP_NOMOVE = 0x0002;
    public const uint SWP_NOZORDER = 0x0004;
    public const uint SWP_NOACTIVATE = 0x0010;
    public const uint SWP_FRAMECHANGED = 0x0020;

    public const uint WM_GETMINMAXINFO = 0x0024;
    public const uint WM_MOVE = 0x0003;
    public const uint WM_ACTIVATE = 0x0006;

    public const int SIZE_RESTORED = 0;
    public const int SIZE_MINIMIZED = 1;
    public const int SIZE_MAXIMIZED = 2;

    public const int WA_INACTIVE = 0;
    public const int WA_ACTIVE = 1;

    public const uint MONITOR_DEFAULTTONEAREST = 0x00000002;
    public const uint MONITORINFOF_PRIMARY = 0x00000001;

    private static IntPtr _marshalHwnd;
    private static WndProcDelegate _marshalWndProc = null!; // 保活，防止被 GC 回收
    private static Func<IntPtr, uint, IntPtr, IntPtr, IntPtr?>? _marshalHandler;

    /// <summary>
    /// 跑 Win32 消息循环，直到收到 WM_QUIT 为止。
    /// </summary>
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
            cbSize = Marshal.SizeOf<WNDCLASSEXW>(),
            lpfnWndProc = _marshalWndProc,
            hInstance = GetModuleHandleW(null),
            lpszClassName = windowClassName,
        };
        if (RegisterClassExW(ref wc) == 0)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "注册消息窗口类失败 (RegisterClassExW)");

        _marshalHwnd = CreateWindowExW(
            0, windowClassName, "", 0,
            0, 0, 0, 0,
            HWND_MESSAGE, IntPtr.Zero, GetModuleHandleW(null), IntPtr.Zero);
        if (_marshalHwnd == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "创建消息窗口失败 (CreateWindowExW)");
        return _marshalHwnd;
    }

    /// <summary>
    /// 注册隐藏消息窗口的附加消息处理（各平台接自己的 WM_RUN → RunQueued）。返回 null 表示未处理，回落到 DefWindowProcW。
    /// </summary>
    public static void SetMarshalMessageHandler(Func<IntPtr, uint, IntPtr, IntPtr, IntPtr?>? handler)
        => _marshalHandler = handler;

    private static IntPtr MarshalWndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        var handled = _marshalHandler?.Invoke(hwnd, msg, wParam, lParam);
        return handled ?? DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    // ------------------------------------------------------------------
    // P/Invoke
    // ------------------------------------------------------------------

    [LibraryImport("comdlg32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetOpenFileNameW([MarshalUsing(typeof(OpenFileNameMarshaller))] ref OPENFILENAME lpofn);

    [LibraryImport("comdlg32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetSaveFileNameW([MarshalUsing(typeof(OpenFileNameMarshaller))] ref OPENFILENAME lpofn);

    [LibraryImport("shell32.dll")]
    public static partial IntPtr SHBrowseForFolderW([MarshalUsing(typeof(BrowseInfoMarshaller))] ref BROWSEINFOW lpbi);

    [LibraryImport("shell32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SHGetPathFromIDListW(IntPtr pidl, IntPtr pszPath);

    [LibraryImport("ole32.dll")]
    public static partial void CoTaskMemFree(IntPtr pv);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool OpenClipboard(IntPtr hWndNewOwner);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool EmptyClipboard();

    [LibraryImport("user32.dll", SetLastError = true)]
    public static partial IntPtr SetClipboardData(uint uFormat, IntPtr hMem);

    [LibraryImport("user32.dll", SetLastError = true)]
    public static partial IntPtr GetClipboardData(uint uFormat);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CloseClipboard();

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsClipboardFormatAvailable(uint format);

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial uint RegisterClipboardFormatW(string lpszFormat);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static partial IntPtr GlobalAlloc(uint uFlags, nuint dwBytes);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static partial IntPtr GlobalLock(IntPtr hMem);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GlobalUnlock(IntPtr hMem);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static partial IntPtr GlobalFree(IntPtr hMem);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static partial nuint GlobalSize(IntPtr hMem);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.I4)]
    public static partial int MessageBoxW(
        IntPtr hWnd,
        [MarshalAs(UnmanagedType.LPWStr)] string lpText,
        [MarshalAs(UnmanagedType.LPWStr)] string lpCaption,
        uint uType);

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
    public static partial IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool MoveWindow(IntPtr hWnd, int x, int y, int nWidth, int nHeight, [MarshalAs(UnmanagedType.Bool)] bool bRepaint);

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

    [LibraryImport("shell32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool Shell_NotifyIcon(uint dwMessage, [MarshalUsing(typeof(NotifyIconDataMarshaller))] in NOTIFYICONDATA lpData);

    [LibraryImport("user32.dll", SetLastError = true)]
    public static partial uint TrackPopupMenu(IntPtr hMenu, uint uFlags, int x, int y, int nReserved, IntPtr hWnd, IntPtr prcRect);

    [LibraryImport("user32.dll")]
    public static partial IntPtr CreatePopupMenu();

    [LibraryImport("user32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool AppendMenuW(IntPtr hMenu, uint uFlags, IntPtr uIDNewItem, string? lpNewItem);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DestroyMenu(IntPtr hMenu);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetCursorPos(out POINT lpPoint);

    [LibraryImport("user32.dll", SetLastError = true)]
    public static partial IntPtr DefWindowProc(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [LibraryImport("user32.dll")]
    public static partial IntPtr DispatchMessage(ref MSG lpMsg);

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial IntPtr GetModuleHandle(string lpModuleName);

    [LibraryImport("user32.dll", SetLastError = true)]
    public static partial IntPtr GetWindowLongPtrW(IntPtr hWnd, int nIndex);

    [LibraryImport("user32.dll", SetLastError = true)]
    public static partial IntPtr SetWindowLongPtrW(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsZoomed(IntPtr hWnd);

    [LibraryImport("user32.dll")]
    public static partial IntPtr GetForegroundWindow();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool AdjustWindowRectEx(ref RECT lpRect, int dwStyle, [MarshalAs(UnmanagedType.Bool)] bool bMenu, uint dwExStyle);

    [LibraryImport("user32.dll")]
    public static partial IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetMonitorInfoW(IntPtr hMonitor, ref MONITORINFO lpmi);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);
}
