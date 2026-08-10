using System;
using System.Runtime.InteropServices;

namespace WebWindowUI.Cef;

/// <summary>
/// 对 CEF-native 对象的调用助手：PtrToStructure 读扁平结构 → GetDelegateForFunctionPointer 取方法 → 调用。
/// 方法指针均为 CEF_CALLBACK = __stdcall（x64 上无实际差异）。
/// </summary>
internal static partial class CefNative
{
    private static T Fn<T>(IntPtr slot) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(slot);

    // ===== ref 计数基元（调用 CEF 自有对象的 add_ref/release——browser/host/frame 的 vtbl 是 CEF 的）=====
    internal static void Base_AddRef(IntPtr obj)
    {
        var s = Marshal.PtrToStructure<CefRefBase>(obj);
        Fn<CefBaseAddRef>(s.AddRef)(obj);
    }
    internal static int Base_Release(IntPtr obj)
    {
        var s = Marshal.PtrToStructure<CefRefBase>(obj);
        return Fn<CefBaseRelease>(s.Release)(obj);
    }

    // ===== cef_browser_t =====
    internal static IntPtr Browser_GetHost(IntPtr b)
    {
        var s = Marshal.PtrToStructure<CefBrowserPtr>(b);
        return Fn<CefBrowserGetHost>(s.GetHost)(b);
    }
    internal static IntPtr Browser_GetMainFrame(IntPtr b)
    {
        var s = Marshal.PtrToStructure<CefBrowserPtr>(b);
        return Fn<CefBrowserGetMainFrame>(s.GetMainFrame)(b);
    }
    internal static int Browser_GetIdentifier(IntPtr b)
    {
        var s = Marshal.PtrToStructure<CefBrowserPtr>(b);
        return Fn<CefBrowserGetIdentifier>(s.GetIdentifier)(b);
    }

    // ===== cef_browser_host_t =====
    internal static void BrowserHost_CloseBrowser(IntPtr h, int force)
    {
        var s = Marshal.PtrToStructure<CefBrowserHostPtr>(h);
        Fn<CefBrowserHostCloseBrowser>(s.CloseBrowser)(h, force);
    }
    internal static void BrowserHost_WasResized(IntPtr h)
    {
        var s = Marshal.PtrToStructure<CefBrowserHostPtr>(h);
        Fn<CefBrowserHostWasResized>(s.WasResized)(h);
    }
    internal static IntPtr BrowserHost_GetWindowHandle(IntPtr h)
    {
        var s = Marshal.PtrToStructure<CefBrowserHostPtr>(h);
        return Fn<CefBrowserHostGetWindowHandle>(s.GetWindowHandle)(h);
    }

    // ===== cef_frame_t =====
    internal static void Frame_ExecuteJavaScript(IntPtr f, ref CefString code, ref CefString scriptUrl)
    {
        var s = Marshal.PtrToStructure<CefFramePtr>(f);
        Fn<CefFrameExecuteJavaScript>(s.ExecuteJavaScript)(f, ref code, ref scriptUrl, 1);
    }
    internal static bool Frame_IsMain(IntPtr f)
    {
        var s = Marshal.PtrToStructure<CefFramePtr>(f);
        return Fn<CefFrameIsMain>(s.IsMain)(f) != 0;
    }
    internal static void Frame_LoadUrl(IntPtr f, ref CefString url)
    {
        var s = Marshal.PtrToStructure<CefFramePtr>(f);
        Fn<CefFrameLoadUrl>(s.LoadUrl)(f, ref url);
    }

    // ===== cef_request_t =====
    internal static IntPtr Request_GetUrl(IntPtr r)
    {
        var s = Marshal.PtrToStructure<CefRequestPtr>(r);
        return Fn<CefRequestGetUrl>(s.GetUrl)(r);
    }
    internal static IntPtr Request_GetMethod(IntPtr r)
    {
        var s = Marshal.PtrToStructure<CefRequestPtr>(r);
        return Fn<CefRequestGetMethod>(s.GetMethod)(r);
    }
    internal static IntPtr Request_GetPostData(IntPtr r)
    {
        var s = Marshal.PtrToStructure<CefRequestPtr>(r);
        return Fn<CefRequestGetPostData>(s.GetPostData)(r);
    }

    // ===== cef_post_data_t / element =====
    internal static IntPtr[] PostData_GetElements(IntPtr postData)
    {
        var s = Marshal.PtrToStructure<CefPostDataPtr>(postData);
        var countFn = Fn<CefPostDataGetElementCount>(s.GetElementCount);
        var elemsFn = Fn<CefPostDataGetElements>(s.GetElements);
        var count = countFn(postData);
        if (count == 0)
            return Array.Empty<IntPtr>();
        var arr = new IntPtr[count];
        var handle = GCHandle.Alloc(arr, GCHandleType.Pinned);
        try
        {
            elemsFn(postData, ref count, handle.AddrOfPinnedObject());
        }
        finally
        {
            handle.Free();
        }
        Array.Resize(ref arr, (int)count);
        return arr;
    }

    internal static byte[] PostDataElement_ReadBytes(IntPtr element)
    {
        var s = Marshal.PtrToStructure<CefPostDataElementPtr>(element);
        var countFn = Fn<CefPostDataElementGetBytesCount>(s.GetBytesCount);
        var readFn = Fn<CefPostDataElementGetBytes>(s.GetBytes);
        var count = (int)countFn(element);
        if (count <= 0)
            return Array.Empty<byte>();
        var buf = new byte[count];
        var handle = GCHandle.Alloc(buf, GCHandleType.Pinned);
        try
        {
            readFn(element, (nuint)count, handle.AddrOfPinnedObject());
        }
        finally
        {
            handle.Free();
        }
        return buf;
    }

    // ===== cef_response_t =====
    internal static void Response_SetStatus(IntPtr r, int status)
    {
        var s = Marshal.PtrToStructure<CefResponsePtr>(r);
        Fn<CefResponseSetStatus>(s.SetStatus)(r, status);
    }
    internal static void Response_SetStatusText(IntPtr r, ref CefString text)
    {
        var s = Marshal.PtrToStructure<CefResponsePtr>(r);
        Fn<CefResponseSetStatusText>(s.SetStatusText)(r, ref text);
    }
    internal static void Response_SetMimeType(IntPtr r, ref CefString mime)
    {
        var s = Marshal.PtrToStructure<CefResponsePtr>(r);
        Fn<CefResponseSetMimeType>(s.SetMimeType)(r, ref mime);
    }
    internal static void Response_SetHeaderByName(IntPtr r, ref CefString name, ref CefString value)
    {
        var s = Marshal.PtrToStructure<CefResponsePtr>(r);
        Fn<CefResponseSetHeaderByName>(s.SetHeaderByName)(r, ref name, ref value, 1);
    }

    // ===== cef_scheme_registrar_t =====
    internal static int SchemeRegistrar_AddCustomScheme(IntPtr registrar, ref CefString name, int options)
    {
        var s = Marshal.PtrToStructure<CefSchemeRegistrarPtr>(registrar);
        return Fn<CefSchemeRegistrarAddCustomScheme>(s.AddCustomScheme)(registrar, ref name, options);
    }
}

// ===== 调用侧委托（CEF 对象方法，__stdcall）=====

[UnmanagedFunctionPointer(CallingConvention.StdCall)]
internal delegate void CefBaseAddRef(IntPtr self);
[UnmanagedFunctionPointer(CallingConvention.StdCall)]
internal delegate int CefBaseRelease(IntPtr self);

[UnmanagedFunctionPointer(CallingConvention.StdCall)]
internal delegate IntPtr CefBrowserGetHost(IntPtr self);
[UnmanagedFunctionPointer(CallingConvention.StdCall)]
internal delegate IntPtr CefBrowserGetMainFrame(IntPtr self);
[UnmanagedFunctionPointer(CallingConvention.StdCall)]
internal delegate int CefBrowserGetIdentifier(IntPtr self);

[UnmanagedFunctionPointer(CallingConvention.StdCall)]
internal delegate void CefBrowserHostCloseBrowser(IntPtr self, int force_close);
[UnmanagedFunctionPointer(CallingConvention.StdCall)]
internal delegate void CefBrowserHostWasResized(IntPtr self);
[UnmanagedFunctionPointer(CallingConvention.StdCall)]
internal delegate IntPtr CefBrowserHostGetWindowHandle(IntPtr self);

[UnmanagedFunctionPointer(CallingConvention.StdCall)]
internal delegate void CefFrameExecuteJavaScript(IntPtr self, ref CefString code, ref CefString script_url, int start_line);
[UnmanagedFunctionPointer(CallingConvention.StdCall)]
internal delegate int CefFrameIsMain(IntPtr self);
[UnmanagedFunctionPointer(CallingConvention.StdCall)]
internal delegate void CefFrameLoadUrl(IntPtr self, ref CefString url);

[UnmanagedFunctionPointer(CallingConvention.StdCall)]
internal delegate IntPtr CefRequestGetUrl(IntPtr self);            // 返回 userfree
[UnmanagedFunctionPointer(CallingConvention.StdCall)]
internal delegate IntPtr CefRequestGetMethod(IntPtr self);         // 返回 userfree
[UnmanagedFunctionPointer(CallingConvention.StdCall)]
internal delegate IntPtr CefRequestGetPostData(IntPtr self);

[UnmanagedFunctionPointer(CallingConvention.StdCall)]
internal delegate nuint CefPostDataGetElementCount(IntPtr self);
[UnmanagedFunctionPointer(CallingConvention.StdCall)]
internal delegate void CefPostDataGetElements(IntPtr self, ref nuint elements_count, IntPtr elements);
[UnmanagedFunctionPointer(CallingConvention.StdCall)]
internal delegate nuint CefPostDataElementGetBytesCount(IntPtr self);
[UnmanagedFunctionPointer(CallingConvention.StdCall)]
internal delegate nuint CefPostDataElementGetBytes(IntPtr self, nuint size, IntPtr bytes);

[UnmanagedFunctionPointer(CallingConvention.StdCall)]
internal delegate void CefResponseSetStatus(IntPtr self, int status);
[UnmanagedFunctionPointer(CallingConvention.StdCall)]
internal delegate void CefResponseSetStatusText(IntPtr self, ref CefString status_text);
[UnmanagedFunctionPointer(CallingConvention.StdCall)]
internal delegate void CefResponseSetMimeType(IntPtr self, ref CefString mime_type);
[UnmanagedFunctionPointer(CallingConvention.StdCall)]
internal delegate void CefResponseSetHeaderByName(IntPtr self, ref CefString name, ref CefString value, int overwrite);

[UnmanagedFunctionPointer(CallingConvention.StdCall)]
internal delegate int CefSchemeRegistrarAddCustomScheme(IntPtr self, ref CefString scheme_name, int options);
