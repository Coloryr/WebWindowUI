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
    // ---- 常量 ----
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

    // OPENFILENAME.lpstrFile 输出缓冲区（字符数）：多选列表可能很长（NUL 分隔多个条目），
    // 官方推荐 32K；单选 4K 足够覆盖长路径。
    public const int OFN_MULTISELECT_BUFFER = 32768;
    public const int OFN_SINGLE_SELECT_BUFFER = 4096;

    public const uint BIF_RETURNONLYFSDIRS = 0x00000001;
    public const uint BIF_NEWDIALOGSTYLE = 0x00000040;

    private const int HWND_MESSAGE = -3;

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
        /// <summary>
        /// 原生布局（blittable，字段序与 WNDCLASSEXW 一致）。
        /// </summary>
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

    [CustomMarshaller(typeof(OPENFILENAME), MarshalMode.Default, typeof(OpenFileNameMarshaller))]
    internal static class OpenFileNameMarshaller
    {
        /// <summary>
        /// 原生布局，字段序与 tagOFNW（OPENFILENAMEW）完全一致，含 lpEditInfo/lpstrPrompt 两个保留字段
        /// ——缺了它们结构体尺寸就小于真实 OPENFILENAMEW（x64 应为 168 字节），对话框按 lStructSize 读越界。
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        internal struct Native
        {
            public int lStructSize;
            public IntPtr hwndOwner;
            public IntPtr hInstance;
            public IntPtr lpstrFilter;          // 输入过滤器（只读）
            public IntPtr lpstrCustomFilter;    // 缓冲 in/out（nMaxCustFilter）
            public int nMaxCustFilter;
            public int nFilterIndex;
            public IntPtr lpstrFile;            // 缓冲 in/out（nMaxFile，返回选中路径/多选列表）
            public int nMaxFile;
            public IntPtr lpstrFileTitle;       // 缓冲 out（nMaxFileTitle，不带路径的文件名）
            public int nMaxFileTitle;
            public IntPtr lpstrInitialDir;
            public IntPtr lpstrTitle;
            public int Flags;
            public short nFileOffset;
            public short nFileExtension;
            public IntPtr lpstrDefExt;
            public IntPtr lCustData;
            public IntPtr lpfnHook;
            public IntPtr lpstrTemplateName;
            public IntPtr lpEditInfo;           // 保留（占位）
            public IntPtr lpstrPrompt;          // 保留（占位，LPCSTR）
            public IntPtr pvReserved;
            public int dwReserved;
            public int FlagsEx;
        }

        public static Native ConvertToUnmanaged(OPENFILENAME managed) => new()
        {
            lStructSize = managed.lStructSize,
            hwndOwner = managed.hwndOwner,
            hInstance = managed.hInstance,
            lpstrFilter = ToCoTaskMem(managed.lpstrFilter),
            lpstrCustomFilter = AllocBuffer(managed.lpstrCustomFilter, managed.nMaxCustFilter),
            nMaxCustFilter = managed.nMaxCustFilter,
            nFilterIndex = managed.nFilterIndex,
            lpstrFile = AllocBuffer(managed.lpstrFile, managed.nMaxFile),
            nMaxFile = managed.nMaxFile,
            lpstrFileTitle = AllocBuffer(managed.lpstrFileTitle, managed.nMaxFileTitle),
            nMaxFileTitle = managed.nMaxFileTitle,
            lpstrInitialDir = ToCoTaskMem(managed.lpstrInitialDir),
            lpstrTitle = ToCoTaskMem(managed.lpstrTitle),
            Flags = managed.Flags,
            nFileOffset = managed.nFileOffset,
            nFileExtension = managed.nFileExtension,
            lpstrDefExt = ToCoTaskMem(managed.lpstrDefExt),
            lCustData = managed.lCustData,
            lpfnHook = managed.lpfnHook,
            lpstrTemplateName = ToCoTaskMem(managed.lpstrTemplateName),
            lpEditInfo = managed.lpEditInfo,
            lpstrPrompt = managed.lpstrPrompt,
            pvReserved = managed.pvReserved,
            dwReserved = managed.dwReserved,
            FlagsEx = managed.FlagsEx,
        };

        public static OPENFILENAME ConvertToManaged(Native unmanaged) => new()
        {
            lStructSize = unmanaged.lStructSize,
            hwndOwner = unmanaged.hwndOwner,
            hInstance = unmanaged.hInstance,
            lpstrFilter = ReadCString(unmanaged.lpstrFilter),
            lpstrCustomFilter = ReadCString(unmanaged.lpstrCustomFilter),
            nMaxCustFilter = unmanaged.nMaxCustFilter,
            nFilterIndex = unmanaged.nFilterIndex,
            lpstrFile = ReadFileBuffer(unmanaged.lpstrFile, unmanaged.nMaxFile),
            nMaxFile = unmanaged.nMaxFile,
            lpstrFileTitle = ReadCString(unmanaged.lpstrFileTitle),
            nMaxFileTitle = unmanaged.nMaxFileTitle,
            lpstrInitialDir = ReadCString(unmanaged.lpstrInitialDir),
            lpstrTitle = ReadCString(unmanaged.lpstrTitle),
            Flags = unmanaged.Flags,
            nFileOffset = unmanaged.nFileOffset,
            nFileExtension = unmanaged.nFileExtension,
            lpstrDefExt = ReadCString(unmanaged.lpstrDefExt),
            lCustData = unmanaged.lCustData,
            lpfnHook = unmanaged.lpfnHook,
            lpstrTemplateName = ReadCString(unmanaged.lpstrTemplateName),
            lpEditInfo = unmanaged.lpEditInfo,
            lpstrPrompt = unmanaged.lpstrPrompt,
            pvReserved = unmanaged.pvReserved,
            dwReserved = unmanaged.dwReserved,
            FlagsEx = unmanaged.FlagsEx,
        };

        /// <summary>
        /// 释放 ConvertToUnmanaged 分配的全部 CoTaskMem（LPWSTR 缓冲区 + 输入字符串）。
        /// 生成器在 P/Invoke 返回后自动调用；只释放我方分配的指针，句柄类字段（hwndOwner/hInstance/lCustData/lpfnHook）不动。
        /// </summary>
        public static void Free(Native unmanaged)
        {
            Marshal.FreeCoTaskMem(unmanaged.lpstrFilter);
            Marshal.FreeCoTaskMem(unmanaged.lpstrCustomFilter);
            Marshal.FreeCoTaskMem(unmanaged.lpstrFile);
            Marshal.FreeCoTaskMem(unmanaged.lpstrFileTitle);
            Marshal.FreeCoTaskMem(unmanaged.lpstrInitialDir);
            Marshal.FreeCoTaskMem(unmanaged.lpstrTitle);
            Marshal.FreeCoTaskMem(unmanaged.lpstrDefExt);
            Marshal.FreeCoTaskMem(unmanaged.lpstrTemplateName);
        }

        private static IntPtr ToCoTaskMem(string? s)
            => s is null ? IntPtr.Zero : Marshal.StringToCoTaskMemUni(s);

        /// <summary>
        /// 分配 LPWSTR 输出缓冲区：容量 max(capacity, 初值长度)+1 个字符（含终止符），整块清零后拷入初值。
        /// </summary>
        private static IntPtr AllocBuffer(string? initial, int capacity)
        {
            int chars = Math.Max(capacity, (initial?.Length ?? 0) + 1);
            IntPtr ptr = Marshal.StringToCoTaskMemUni(new string('\0', chars));
            if (initial is not null)
                Marshal.Copy(initial.ToCharArray(), 0, ptr, initial.Length);
            return ptr;
        }

        private static string? ReadCString(IntPtr ptr)
            => ptr == IntPtr.Zero ? null : Marshal.PtrToStringUni(ptr);

        /// <summary>
        /// 读回文件缓冲区。单选 = 普通 NUL 结尾字符串；多选（OFN_ALLOWMULTISELECT）=
        /// "目录\0文件1\0文件2\0\0"，逐字符扫到双 NUL 收进带内嵌 NUL 的原始字符串，由调用方 Split。
        /// </summary>
        private static string? ReadFileBuffer(IntPtr ptr, int maxChars)
        {
            if (ptr == IntPtr.Zero)
                return null;
            int length = 0;
            while (length < maxChars)
            {
                char c = (char)Marshal.ReadInt16(ptr, length * sizeof(char));
                if (c == '\0')
                {
                    // 双 NUL = 列表结束（单选中 API 只写一个 NUL，其后仍是清零残留 → 同样命中，长度正确）
                    if (length + 1 < maxChars && (char)Marshal.ReadInt16(ptr, (length + 1) * sizeof(char)) == '\0')
                        break;
                }
                length++;
            }
            return Marshal.PtrToStringUni(ptr, length);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct OPENFILENAME
    {
        public int lStructSize;
        public IntPtr hwndOwner;
        public IntPtr hInstance;
        public string? lpstrFilter;          // 输入过滤器（"描述\0*.ext\0"，封送器补终止符成双 NUL）
        public string? lpstrCustomFilter;    // in/out 缓冲（nMaxCustFilter）
        public int nMaxCustFilter;
        public int nFilterIndex;
        public string? lpstrFile;            // in/out 缓冲：初值=默认文件名，返回=选中路径（多选为 NUL 分隔列表）
        public int nMaxFile;
        public string? lpstrFileTitle;       // out 缓冲：返回不带路径的文件名
        public int nMaxFileTitle;
        public string? lpstrInitialDir;
        public string? lpstrTitle;
        public int Flags;
        public short nFileOffset;
        public short nFileExtension;
        public string? lpstrDefExt;
        public IntPtr lCustData;
        public IntPtr lpfnHook;
        public string? lpstrTemplateName;
        public IntPtr lpEditInfo;            // 保留字段（OPENFILENAMEW 布局占位）
        public IntPtr lpstrPrompt;           // 保留字段（LPCSTR，OPENFILENAMEW 布局占位）
        public IntPtr pvReserved;
        public int dwReserved;
        public int FlagsEx;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BROWSEINFOW
    {
        public IntPtr hwndOwner;
        public IntPtr pidlRoot;
        public string? pszDisplayName;   // out 缓冲：返回选中项显示名（传 null 则不需要）
        public string? lpszTitle;        // 对话框标题
        public uint ulFlags;
        public IntPtr lpfn;              // 回调（可为 IntPtr.Zero）
        public IntPtr lParam;
        public int iImage;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DROPFILES
    {
        public int pFiles;   // 文件列表偏移（通常 20）
        public int pt_x;
        public int pt_y;
        public int fNC;
        public int fWide;    // 非 0 = UTF-16 路径列表
    }

    [CustomMarshaller(typeof(BROWSEINFOW), MarshalMode.Default, typeof(BrowseInfoMarshaller))]
    internal static class BrowseInfoMarshaller
    {
        /// <summary>
        /// 原生布局（tagBROWSEINFOW），字段序与 BROWSEINFOW 一致，字符串为 LPWSTR。
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        internal struct Native
        {
            public IntPtr hwndOwner;
            public IntPtr pidlRoot;
            public IntPtr pszDisplayName;
            public IntPtr lpszTitle;
            public uint ulFlags;
            public IntPtr lpfn;
            public IntPtr lParam;
            public int iImage;
        }

        public static Native ConvertToUnmanaged(BROWSEINFOW managed) => new()
        {
            hwndOwner = managed.hwndOwner,
            pidlRoot = managed.pidlRoot,
            pszDisplayName = IntPtr.Zero, // 不需要显示名
            lpszTitle = managed.lpszTitle is null ? IntPtr.Zero : Marshal.StringToCoTaskMemUni(managed.lpszTitle),
            ulFlags = managed.ulFlags,
            lpfn = managed.lpfn,
            lParam = managed.lParam,
            iImage = managed.iImage,
        };

        public static BROWSEINFOW ConvertToManaged(Native unmanaged) => new()
        {
            hwndOwner = unmanaged.hwndOwner,
            pidlRoot = unmanaged.pidlRoot,
            lpszTitle = unmanaged.lpszTitle == IntPtr.Zero ? null : Marshal.PtrToStringUni(unmanaged.lpszTitle),
            ulFlags = unmanaged.ulFlags,
            lpfn = unmanaged.lpfn,
            lParam = unmanaged.lParam,
            iImage = unmanaged.iImage,
        };

        /// <summary>
        /// 释放 ConvertToUnmanaged 分配的字符串（lpszTitle）；句柄/回调字段不动。
        /// </summary>
        public static void Free(Native unmanaged)
            => Marshal.FreeCoTaskMem(unmanaged.lpszTitle);
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

    // ---- 剪贴板（user32/kernel32）----
    public const uint CF_TEXT = 1;
    public const uint CF_BITMAP = 2;
    public const uint CF_DIB = 8;
    public const uint CF_UNICODETEXT = 13;
    public const uint CF_HDROP = 15;

    public const uint GMEM_MOVEABLE = 0x0002;
    public const uint GMEM_ZEROINIT = 0x0040;

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

    // ---- 窗口状态（样式位/消息/显示命令，供 Win32NativeWindow 窗口状态面）----
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

    [StructLayout(LayoutKind.Sequential)]
    public struct MINMAXINFO
    {
        public POINT ptReserved;
        public POINT ptMaxSize;
        public POINT ptMaxPosition;
        public POINT ptMinTrackSize;
        public POINT ptMaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    public delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    public static partial IntPtr GetWindowLongPtrW(IntPtr hWnd, int nIndex);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
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
