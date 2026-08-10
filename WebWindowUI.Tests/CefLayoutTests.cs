using System.Runtime.InteropServices;
using WebWindowUI.Cef;
using Xunit;

namespace WebWindowUI.Tests;

/// <summary>
/// CEF 绑定结构布局锁定（对照钉版 CEF 151.3.16 的 include/capi/*.h）。
/// CEF 结构体是扁平顺序布局，字段顺序即偏移——任何转写错误（多删字段、类型宽度错）都会让
/// cef_initialize 静默失败（内置 size 检查）或方法指针错位。这些断言把布局钉死在转写时的值。
/// </summary>
public class CefLayoutTests
{
    [Fact]
    public void CefSettings_Layout_Matches_151_3_16()
    {
        Assert.Equal(448, Marshal.SizeOf<CefSettings>());
        Assert.Equal(0, Marshal.OffsetOf<CefSettings>(nameof(CefSettings.Size)).ToInt64());
        Assert.Equal(8, Marshal.OffsetOf<CefSettings>(nameof(CefSettings.NoSandbox)).ToInt64());
        Assert.Equal(16, Marshal.OffsetOf<CefSettings>(nameof(CefSettings.BrowserSubprocessPath)).ToInt64());
        Assert.Equal(88, Marshal.OffsetOf<CefSettings>(nameof(CefSettings.MultiThreadedMessageLoop)).ToInt64());
        Assert.Equal(104, Marshal.OffsetOf<CefSettings>(nameof(CefSettings.CachePath)).ToInt64());
        Assert.Equal(336, Marshal.OffsetOf<CefSettings>(nameof(CefSettings.RemoteDebuggingPort)).ToInt64());
        Assert.Equal(352, Marshal.OffsetOf<CefSettings>(nameof(CefSettings.AcceptLanguageList)).ToInt64());
        Assert.Equal(432, Marshal.OffsetOf<CefSettings>(nameof(CefSettings.ChromeAppIconId)).ToInt64());
        Assert.Equal(440, Marshal.OffsetOf<CefSettings>(nameof(CefSettings.UseViewsDefaultPopup)).ToInt64());
    }

    [Fact]
    public void CefWindowInfo_Layout_Matches_Windows()
    {
        Assert.Equal(112, Marshal.SizeOf<CefWindowInfo>());
        Assert.Equal(0, Marshal.OffsetOf<CefWindowInfo>(nameof(CefWindowInfo.Size)).ToInt64());
        Assert.Equal(8, Marshal.OffsetOf<CefWindowInfo>(nameof(CefWindowInfo.ExStyle)).ToInt64());
        Assert.Equal(16, Marshal.OffsetOf<CefWindowInfo>(nameof(CefWindowInfo.WindowName)).ToInt64());
        Assert.Equal(80, Marshal.OffsetOf<CefWindowInfo>(nameof(CefWindowInfo.WindowlessRenderingEnabled)).ToInt64());
        Assert.Equal(96, Marshal.OffsetOf<CefWindowInfo>(nameof(CefWindowInfo.Window)).ToInt64());
        Assert.Equal(104, Marshal.OffsetOf<CefWindowInfo>(nameof(CefWindowInfo.RuntimeStyle)).ToInt64());
    }

    [Fact]
    public void CefString_And_MainArgs_Layout()
    {
        Assert.Equal(24, Marshal.SizeOf<CefString>());
        Assert.Equal(8, Marshal.OffsetOf<CefString>(nameof(CefString.Length)).ToInt64());
        Assert.Equal(8, Marshal.SizeOf<CefMainArgs>());
    }

    [Fact]
    public void CefRefBase_And_ScopedBase_Layout()
    {
        Assert.Equal(40, Marshal.SizeOf<CefRefBase>());    // size + 4 refcount fns
        Assert.Equal(16, Marshal.SizeOf<CefScopedBase>()); // size + del
    }

    [Fact]
    public void PointerStructs_Have_Expected_Size()
    {
        // 每个 = base(N) + 方法槽数，全部 8 字节指针
        Assert.Equal(40 + 5 * 8, Marshal.SizeOf<CefAppPtr>());
        Assert.Equal(16 + 1 * 8, Marshal.SizeOf<CefSchemeRegistrarPtr>());
        Assert.Equal(40 + 1 * 8, Marshal.SizeOf<CefSchemeHandlerFactoryPtr>());
        Assert.Equal(40 + 2 * 8, Marshal.SizeOf<CefCallbackPtr>());
        Assert.Equal(40 + 7 * 8, Marshal.SizeOf<CefResourceHandlerPtr>());
        Assert.Equal(40 + 19 * 8, Marshal.SizeOf<CefClientPtr>());
        Assert.Equal(40 + 6 * 8, Marshal.SizeOf<CefLifeSpanHandlerPtr>());
        Assert.Equal(40 + 4 * 8, Marshal.SizeOf<CefLoadHandlerPtr>());
        Assert.Equal(40 + 21 * 8, Marshal.SizeOf<CefBrowserPtr>());
        Assert.Equal(40 + 69 * 8, Marshal.SizeOf<CefBrowserHostPtr>());
        Assert.Equal(40 + 26 * 8, Marshal.SizeOf<CefFramePtr>());
        Assert.Equal(40 + 22 * 8, Marshal.SizeOf<CefRequestPtr>());
        Assert.Equal(40 + 17 * 8, Marshal.SizeOf<CefResponsePtr>());
        Assert.Equal(40 + 7 * 8, Marshal.SizeOf<CefPostDataPtr>());
        Assert.Equal(40 + 8 * 8, Marshal.SizeOf<CefPostDataElementPtr>());
    }
}
