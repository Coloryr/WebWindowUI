using System;
using System.Runtime.InteropServices;

namespace CefDemo;

/// <summary>
/// 跨平台 GPU 设备选择与预热（对齐 cefsimple_capi 的 PreloadGpuStack）。
/// 在浏览器进程初始化 CEF 前调用：选中真实硬件显卡并初始化其用户态驱动栈，
/// 避免 CEF 默认 ANGLE/D3D11 后端在 GPU 子进程内初始化驱动时
/// IMMEDIATE_CRASH（0x80000003，libcef 内部 "create shared context for
/// virtualization" CHECK 失败，多显卡/虚拟显示器 VM 上复现）。
/// Windows 用 DllImport 静态绑定（不手写 COM vtable，避免 OVERRUN）。
/// </summary>
internal static class GpuDevicePreload
{
    /// <summary>
    /// 预热 GPU：按平台选择硬件显卡并初始化驱动栈。幂等，失败静默。
    /// </summary>
    public static void Preload()
    {
        if (OperatingSystem.IsWindows())
        {
            PreloadWindows();
        }
        else if (OperatingSystem.IsLinux())
        {
            PreloadLinux();
        }
        else if (OperatingSystem.IsMacOS())
        {
            PreloadMacOS();
        }
    }

    // ---- Windows: DXGI 枚举 + D3D11 设备 + EGL 上下文 ----

    private const uint DXGI_ERROR_NOT_FOUND = 0x887A0002;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DXGI_ADAPTER_DESC1
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string Description;
        public uint VendorId;
        public uint DeviceId;
        public uint SubSysId;
        public uint Revision;
        public nuint DedicatedVideoMemory;
        public nuint DedicatedSystemMemory;
        public nuint SharedSystemMemory;
        public long AdapterLuid;
        public uint Flags;
    }

    private static readonly Guid IID_IDXGIFactory = new(
        unchecked((int)0x7b7166ec), unchecked((short)0x21c7), unchecked((short)0x44ae),
        new byte[] { 0xb2, 0x1a, 0xc9, 0xae, 0x32, 0x1a, 0xe3, 0x69 });

    // IDXGIFactory (full COM interface, vtable order from dxgi.h):
    // IUnknown: QueryInterface AddRef Release
    // IDXGIObject: SetPrivateData SetPrivateDataInterface GetPrivateData GetParent
    // IDXGIFactory: EnumAdapters MakeWindowAssociation GetWindowAssociation
    //               CreateSwapChain CreateSoftwareAdapter
    [ComImport, Guid("7b7166ec-21c7-44ae-b21a-c9ae321ae369")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDXGIFactory
    {
        [PreserveSig] int QueryInterface(ref Guid riid, out IntPtr ppv);
        [PreserveSig] uint AddRef();
        [PreserveSig] uint Release();
        [PreserveSig] int SetPrivateData(ref Guid name, uint dataSize, IntPtr data);
        [PreserveSig] int SetPrivateDataInterface(ref Guid name, IntPtr unknown);
        [PreserveSig] int GetPrivateData(ref Guid name, ref uint dataSize, IntPtr data);
        [PreserveSig] int GetParent(ref Guid riid, out IntPtr ppParent);
        [PreserveSig] int EnumAdapters(uint adapter, out IDXGIAdapter ppAdapter);
        [PreserveSig] int MakeWindowAssociation(IntPtr windowHandle, uint flags);
        [PreserveSig] int GetWindowAssociation(out IntPtr windowHandle);
        [PreserveSig] int CreateSwapChain(IntPtr device, IntPtr desc, out IntPtr swapChain);
        [PreserveSig] int CreateSoftwareAdapter(IntPtr module, out IDXGIAdapter ppAdapter);
    }

    // IDXGIAdapter (full COM interface):
    // IUnknown + IDXGIObject + EnumOutputs GetDesc CheckInterfaceSupport
    [ComImport, Guid("2411e7e1-12ac-4ccf-bd14-9798e8534dc0")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDXGIAdapter
    {
        [PreserveSig] int QueryInterface(ref Guid riid, out IntPtr ppv);
        [PreserveSig] uint AddRef();
        [PreserveSig] uint Release();
        [PreserveSig] int SetPrivateData(ref Guid name, uint dataSize, IntPtr data);
        [PreserveSig] int SetPrivateDataInterface(ref Guid name, IntPtr unknown);
        [PreserveSig] int GetPrivateData(ref Guid name, ref uint dataSize, IntPtr data);
        [PreserveSig] int GetParent(ref Guid riid, out IntPtr ppParent);
        [PreserveSig] int EnumOutputs(uint output, out IntPtr ppOutput);
        [PreserveSig] int GetDesc(out DXGI_ADAPTER_DESC1 desc);
        [PreserveSig] int CheckInterfaceSupport(ref Guid interfaceName, out long pUMDVersion);
    }

    [DllImport("dxgi.dll")]
    private static extern int CreateDXGIFactory(in Guid riid, out IDXGIFactory factory);

    [DllImport("d3d11.dll")]
    private static extern int D3D11CreateDevice(
        IDXGIAdapter? adapter, int driverType, IntPtr software,
        uint flags, IntPtr featureLevels, uint numLevels, uint sdkVersion,
        out IntPtr device, out int featureLevel, out IntPtr context);

    private const int D3D_DRIVER_TYPE_UNKNOWN = 0;
    private const uint D3D11_SDK_VERSION = 7;
    private const uint DXGI_ADAPTER_FLAG_SOFTWARE = 1;

    private static void PreloadWindows()
    {
        // Load discrete GPU user-mode driver DLLs (no-op if absent).
        LoadOptional("nvwgf2umx.dll");
        LoadOptional("nvldumdx.dll");
        LoadOptional("amdvlk64.dll");
        LoadOptional("atiuxpag.dll");
        LoadOptional("ig9icd64.dll");

        try
        {
            if (CreateDXGIFactory(in IID_IDXGIFactory, out var factory) != 0 || factory == null)
                return;

            IDXGIAdapter? selected = null;
            try
            {
                for (uint i = 0; ; i++)
                {
                    uint hr = unchecked((uint)factory.EnumAdapters(i, out var adapter));
                    if (hr == DXGI_ERROR_NOT_FOUND || adapter == null)
                        break;
                    try
                    {
                        adapter.GetDesc(out var desc);
                        bool software = (desc.Flags & DXGI_ADAPTER_FLAG_SOFTWARE) != 0 ||
                                        (desc.VendorId == 0 && desc.DeviceId == 0);
                        if (software)
                            continue;
                        uint v = desc.VendorId;
                        if (v == 0x10DE || v == 0x1002 || v == 0x8086)
                        {
                            if (selected == null || v == 0x10DE)
                            {
                                selected?.Release();
                                selected = adapter;
                                adapter = null; // ownership transferred
                                if (v == 0x10DE)
                                    break;
                            }
                        }
                        else if (selected == null)
                        {
                            selected = adapter;
                            adapter = null; // ownership transferred
                        }
                    }
                    finally
                    {
                        adapter?.Release();
                    }
                }

                if (selected != null)
                {
                    int hr = D3D11CreateDevice(selected, D3D_DRIVER_TYPE_UNKNOWN, IntPtr.Zero,
                        0, IntPtr.Zero, 0, D3D11_SDK_VERSION,
                        out var device, out _, out var context);
                    if (hr == 0)
                    {
                        if (device != IntPtr.Zero)
                            Marshal.Release(device);
                        if (context != IntPtr.Zero)
                            Marshal.Release(context);
                    }
                }
            }
            finally
            {
                selected?.Release();
                Marshal.FinalReleaseComObject(factory);
            }
        }
        catch
        {
            // GPU 预热失败不影响启动（CEF 会自行降级）。
        }

        // Full EGL context warm-up via CEF's own ANGLE (libEGL/libGLESv2).
        PreloadEgl();
    }

    private delegate IntPtr EglGetDisplayDelegate(IntPtr nativeDisplay);
    private delegate int EglInitializeDelegate(IntPtr display, out IntPtr major, out IntPtr minor);
    private delegate int EglChooseConfigDelegate(IntPtr display, int[] attribs, IntPtr[] configs,
        int configSize, ref int numConfigs);
    private delegate IntPtr EglCreateContextDelegate(IntPtr display, IntPtr config, IntPtr share,
        int[] attribs);
    private delegate int EglMakeCurrentDelegate(IntPtr display, IntPtr draw, IntPtr read,
        IntPtr context);

    private static void PreloadEgl()
    {
        IntPtr egl = LoadOptional("libEGL.dll");
        IntPtr gles = LoadOptional("libGLESv2.dll");
        if (egl == IntPtr.Zero || gles == IntPtr.Zero)
            return;

        var eglGetDisplay = GetProc<EglGetDisplayDelegate>(egl, "eglGetDisplay");
        var eglInitialize = GetProc<EglInitializeDelegate>(egl, "eglInitialize");
        var eglChooseConfig = GetProc<EglChooseConfigDelegate>(egl, "eglChooseConfig");
        var eglCreateContext = GetProc<EglCreateContextDelegate>(egl, "eglCreateContext");
        var eglMakeCurrent = GetProc<EglMakeCurrentDelegate>(egl, "eglMakeCurrent");
        if (eglGetDisplay == null || eglInitialize == null || eglChooseConfig == null ||
            eglCreateContext == null || eglMakeCurrent == null)
            return;

        IntPtr display = eglGetDisplay(IntPtr.Zero);
        if (display == IntPtr.Zero || eglInitialize(display, out _, out _) == 0)
            return;

        int[] attribs = { 0x3040 /*EGL_RENDERABLE_TYPE*/, 4 /*EGL_OPENGL_ES2_BIT*/,
                          0x3021 /*EGL_SURFACE_TYPE*/, 8 /*EGL_PBUFFER_BIT*/,
                          0x3038 /*EGL_NONE*/, 0 };
        IntPtr[] configs = new IntPtr[1];
        int numConfigs = 0;
        if (eglChooseConfig(display, attribs, configs, 1, ref numConfigs) == 0 ||
            numConfigs < 1 || configs[0] == IntPtr.Zero)
            return;

        int[] ctxAttribs = { 0x3098 /*EGL_CONTEXT_CLIENT_VERSION*/, 2, 0 };
        IntPtr context = eglCreateContext(display, configs[0], IntPtr.Zero, ctxAttribs);
        if (context == IntPtr.Zero)
            return;
        _ = eglMakeCurrent(display, IntPtr.Zero, IntPtr.Zero, context);
    }

    // ---- Linux: EGL 初始化（libEGL.so.1）+ DRI_PRIME ----

    private static void PreloadLinux()
    {
        IntPtr egl = LoadOptional("libEGL.so.1");
        if (egl == IntPtr.Zero)
            egl = LoadOptional("libEGL.so");
        if (egl == IntPtr.Zero)
            return;

        var eglGetDisplay = GetProc<EglGetDisplayDelegate>(egl, "eglGetDisplay");
        var eglInitialize = GetProc<EglInitializeDelegate>(egl, "eglInitialize");
        var eglChooseConfig = GetProc<EglChooseConfigDelegate>(egl, "eglChooseConfig");
        var eglCreateContext = GetProc<EglCreateContextDelegate>(egl, "eglCreateContext");
        var eglMakeCurrent = GetProc<EglMakeCurrentDelegate>(egl, "eglMakeCurrent");
        if (eglGetDisplay == null || eglInitialize == null || eglChooseConfig == null ||
            eglCreateContext == null || eglMakeCurrent == null)
            return;

        IntPtr display = eglGetDisplay(IntPtr.Zero); // EGL_DEFAULT_DISPLAY
        if (display == IntPtr.Zero || eglInitialize(display, out _, out _) == 0)
            return;

        int[] attribs = { 0x3040 /*EGL_RENDERABLE_TYPE*/, 4 /*EGL_OPENGL_ES2_BIT*/,
                          0x3038 /*EGL_NONE*/, 0 };
        IntPtr[] configs = new IntPtr[1];
        int numConfigs = 0;
        if (eglChooseConfig(display, attribs, configs, 1, ref numConfigs) == 0 ||
            numConfigs < 1 || configs[0] == IntPtr.Zero)
            return;

        int[] ctxAttribs = { 0x3098 /*EGL_CONTEXT_CLIENT_VERSION*/, 2, 0 };
        IntPtr context = eglCreateContext(display, configs[0], IntPtr.Zero, ctxAttribs);
        if (context == IntPtr.Zero)
            return;
        _ = eglMakeCurrent(display, IntPtr.Zero, IntPtr.Zero, context);
    }

    // ---- macOS: CGL 上下文初始化 ----

    private static void PreloadMacOS()
    {
        IntPtr cgl = LoadOptional("/System/Library/Frameworks/OpenGL.framework/OpenGL");
        if (cgl == IntPtr.Zero)
            return;

        var cglChoosePixelFormat = GetProc<CGLChoosePixelFormatDelegate>(cgl, "CGLChoosePixelFormat");
        var cglCreateContext = GetProc<CGLCreateContextDelegate>(cgl, "CGLCreateContext");
        var cglSetCurrentContext = GetProc<CGLSetCurrentContextDelegate>(cgl, "CGLSetCurrentContext");
        var cglDestroyPixelFormat = GetProc<CGLDestroyPixelFormatDelegate>(cgl, "CGLDestroyPixelFormat");
        if (cglChoosePixelFormat == null || cglCreateContext == null ||
            cglSetCurrentContext == null || cglDestroyPixelFormat == null)
            return;

        int[] attrs = { 8 /*kCGLPFAAccelerated*/, 0 };
        if (cglChoosePixelFormat(attrs, out IntPtr pixelFormat, out _) != 0 || pixelFormat == IntPtr.Zero)
            return;
        if (cglCreateContext(pixelFormat, IntPtr.Zero, out IntPtr context) == 0 && context != IntPtr.Zero)
        {
            _ = cglSetCurrentContext(context);
        }
        cglDestroyPixelFormat(pixelFormat);
    }

    private delegate int CGLChoosePixelFormatDelegate(int[] attribs, out IntPtr pixelFormat, out int npix);
    private delegate int CGLCreateContextDelegate(IntPtr pixelFormat, IntPtr share, out IntPtr context);
    private delegate int CGLSetCurrentContextDelegate(IntPtr context);
    private delegate int CGLDestroyPixelFormatDelegate(IntPtr pixelFormat);

    // ---- helpers ----

    private static IntPtr LoadOptional(string name)
    {
        try
        {
            return NativeLibrary.Load(name);
        }
        catch
        {
            return IntPtr.Zero;
        }
    }

    private static T? GetProc<T>(IntPtr lib, string name) where T : Delegate
    {
        try
        {
            IntPtr p = NativeLibrary.GetExport(lib, name);
            return p == IntPtr.Zero ? null : Marshal.GetDelegateForFunctionPointer<T>(p);
        }
        catch
        {
            return null;
        }
    }
}
