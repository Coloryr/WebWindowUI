using System.Drawing;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace WebWindowUI.Natives.Windows;

[UnmanagedFunctionPointer(CallingConvention.Winapi)]
public delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

[UnmanagedFunctionPointer(CallingConvention.Winapi)]
public delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData);

[CustomMarshaller(typeof(WNDCLASSEXW), MarshalMode.Default, typeof(WndClassExMarshaller))]
internal static class WndClassExMarshaller
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct Native
    {
        public int cbSize;
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
    public int cbSize;
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

    public WNDCLASSEXW()
    {
        cbSize = Marshal.SizeOf<WNDCLASSEXW>();
    }
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

    public OPENFILENAME()
    {
        // 原生 OPENFILENAMEW 尺寸（含 lpEditInfo/lpstrPrompt 两个保留占位，x64=168）；曾误用 NOTIFYICONDATA
        // 尺寸（976）导致 GetOpenFileNameW/GetSaveFileNameW 校验 lStructSize 失败、对话框直接按取消返回。
        lStructSize = Marshal.SizeOf<OpenFileNameMarshaller.Native>();
    }
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

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
public struct NOTIFYICONDATA
{
    public int cbSize;
    public IntPtr hWnd;
    public uint uID;
    public uint uFlags;
    public uint uCallbackMessage;
    public IntPtr hIcon;
    public string szTip;
    public uint dwState;
    public uint dwStateMask;
    public string szInfo;
    public uint uTimeout;
    public uint uVersion;
    public string szInfoTitle;
    public uint dwInfoFlags;
    public Guid guidItem;
    public IntPtr hBalloonIcon;

    /// <summary>
    /// 原生结构大小（fixed char 布局 + guidItem，976 字节；Shell_NotifyIcon 按此校验 cbSize）。
    /// </summary>
    public static int Size => Marshal.SizeOf<NotifyIconDataMarshaller.Native>();

    public NOTIFYICONDATA()
    {
        cbSize = Size;
    }
}

[CustomMarshaller(typeof(NOTIFYICONDATA), MarshalMode.Default, typeof(NotifyIconDataMarshaller))]
internal static class NotifyIconDataMarshaller
{
    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct Native
    {
        public int cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        public fixed char szTip[128];
        public uint dwState;
        public uint dwStateMask;
        public fixed char szInfo[256];
        public NativeTimeVersion timeVersion;   // 联合（uTimeout/uVersion 共 4 字节）：分开会虚增 4 字节、后置字段右移
        public fixed char szInfoTitle[64];
        public uint dwInfoFlags;
        public fixed byte guidItem[16];
        public IntPtr hBalloonIcon;

        /// <summary>
        /// NOTIFYICONDATAW 的 uTimeout/uVersion 联合（同一偏移；V3 尺寸=976，分开即虚增到 984）。
        /// </summary>
        [StructLayout(LayoutKind.Explicit)]
        internal struct NativeTimeVersion
        {
            [FieldOffset(0)] public uint uTimeout;
            [FieldOffset(0)] public uint uVersion;
        }
    }

    public unsafe static Native ConvertToUnmanaged(NOTIFYICONDATA managed)
    {
        var native = new Native()
        {
            cbSize = managed.cbSize,
            hWnd = managed.hWnd,
            uID = managed.uID,
            uFlags = managed.uFlags,
            uCallbackMessage = managed.uCallbackMessage,
            hIcon = managed.hIcon,
            dwState = managed.dwState,
            dwStateMask = managed.dwStateMask,
            timeVersion = new Native.NativeTimeVersion { uTimeout = managed.uTimeout, uVersion = managed.uVersion },
            dwInfoFlags = managed.dwInfoFlags,
            hBalloonIcon = managed.hBalloonIcon,
        };

        var temp = managed.szTip == null ? [] : managed.szTip.ToCharArray();
        for (int i = 0; i < Math.Min(temp.Length, 128); i++)
        { 
            native.szTip[i] = temp[i];
        }
        temp = managed.szInfo == null ? [] : managed.szInfo.ToCharArray();
        for (int i = 0; i < Math.Min(temp.Length, 256); i++)
        {
            native.szInfo[i] = temp[i];
        }
        temp = managed.szInfoTitle == null ? [] : managed.szInfoTitle.ToCharArray();
        for (int i = 0; i < Math.Min(temp.Length, 64); i++)
        {
            native.szInfoTitle[i] = temp[i];
        }
        var temp1 = managed.guidItem.ToByteArray();
        for (int i = 0; i < 16; i++)
        {
            native.guidItem[i] = temp1[i];
        }

        return native;
    }

    public unsafe static NOTIFYICONDATA ConvertToManaged(Native unmanaged)
    {
        var guid = new byte[16];
        for (int i = 0; i < 16; i++)
        {
            guid[i] = unmanaged.guidItem[i];
        }

        var data = new NOTIFYICONDATA
        {
            cbSize = unmanaged.cbSize,
            hWnd = unmanaged.hWnd,
            uID = unmanaged.uID,
            uFlags = unmanaged.uFlags,
            hIcon = unmanaged.hIcon,
            szTip = new string(unmanaged.szTip),
            dwState = unmanaged.dwState,
            dwStateMask = unmanaged.dwStateMask,
            szInfo = new string(unmanaged.szInfo),
            uTimeout = unmanaged.timeVersion.uTimeout,
            uVersion = unmanaged.timeVersion.uVersion,
            szInfoTitle = new string(unmanaged.szInfoTitle),
            dwInfoFlags = unmanaged.dwInfoFlags,
            guidItem = new Guid(guid),
            hBalloonIcon = unmanaged.hBalloonIcon,
        };

        return data;
    }

    public static void Free(Native unmanaged)
    {
        
    }
}

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

    public MONITORINFO()
    { 
        cbSize = Marshal.SizeOf<MONITORINFO>();
    }
}