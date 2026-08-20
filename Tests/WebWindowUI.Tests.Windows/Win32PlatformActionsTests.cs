using System.Runtime.InteropServices;
using WebWindowUI.Natives.Windows;
using WebWindowUI.Tests.Windows.Support;
using Xunit;

namespace WebWindowUI.Tests.Windows;

/// <summary>
/// Win32 平台动作（对话框/托盘）封送正确性测试：曾因 OPENFILENAME.lStructSize 误用 NOTIFYICONDATA 尺寸
/// （976）致 GetOpenFileNameW/GetSaveFileNameW 直接按取消返回（打开/保存文件无效）；NotifyIconDataMarshaller
/// 漏拷 uCallbackMessage 致托盘消息（右键菜单/点击）不路由（托盘菜单不显示）。这里锁结构尺寸 + 封送字段。
/// </summary>
public class Win32PlatformActionsTests
{
    /// <summary>
    /// OPENFILENAME.lStructSize 必须等于原生 OPENFILENAMEW 布局尺寸（x64=168，含 lpEditInfo/lpstrPrompt 占位）。
    /// 曾误设 NOTIFYICONDATA 尺寸（976），GetOpenFileNameW/GetSaveFileNameW 校验 lStructSize 失败直接返回。
    /// </summary>
    [Fact]
    public void OpenFileName_StructSize_MatchesNativeLayout()
    {
        var ofn = new OPENFILENAME();
        int expected = Marshal.SizeOf<OpenFileNameMarshaller.Native>();
        Assert.Equal(expected, ofn.lStructSize);
        Assert.True(ofn.lStructSize is >= 88 and <= 200, $"lStructSize={ofn.lStructSize} 不像是 OPENFILENAMEW 尺寸");
    }

    /// <summary>
    /// NOTIFYICONDATAW 原生尺寸必须为 976（V3）：uTimeout/uVersion 是 4 字节联合（不是两个独立 uint，
    /// 分开会让 szInfoTitle 起整体右移 4 字节、尺寸虚增到 984）。cbSize 对不上 Windows 校验即拒绝显示。
    /// </summary>
    [Fact]
    public void NotifyIconData_NativeSize_IsV3Size()
    {
        Assert.Equal(976, NOTIFYICONDATA.Size);
    }

    /// <summary>
    /// NotifyIconDataMarshaller 封送必须带 uCallbackMessage：漏拷则原生 uCallbackMessage=0，
    /// Shell_NotifyIcon 不向窗口路由 WM_TRAYICON（托盘右键菜单/单击/双击全收不到）。
    /// </summary>
    [Fact]
    public void NotifyIconData_Unmanaged_KeepsCallbackMessage()
    {
        var nid = new NOTIFYICONDATA
        {
            hWnd = IntPtr.Zero,
            uID = 0x1001,
            uFlags = Win32.NIF_MESSAGE | Win32.NIF_TIP,
            uCallbackMessage = Win32.WM_TRAYICON,
            szTip = "test",
        };
        var native = NotifyIconDataMarshaller.ConvertToUnmanaged(nid);
        Assert.Equal(Win32.WM_TRAYICON, native.uCallbackMessage);
    }

    /// <summary>
    /// 真实托盘链路：NIM_ADD / NIM_MODIFY(NIF_STATE 隐藏) / NIM_MODIFY(NIF_INFO 气泡) / NIM_DELETE
    /// 全部返回 true（原生接受封送结构，图标可见/可隐藏/可弹气泡）。在 STA 泵线程建消息窗口作托盘宿主。
    /// </summary>
    [Fact]
    public async Task TrayIcon_AddHideBalloonDelete_Succeeds()
    {
        await StaThreadPump.Instance.RunAsync(async () =>
        {
            IntPtr hwnd = Win32.CreateWindowExW(
                0, "Static", "", 0,
                0, 0, 0, 0,
                (IntPtr)Win32.HWND_MESSAGE, IntPtr.Zero, Win32.GetModuleHandleW(null), IntPtr.Zero);
            Assert.NotEqual(IntPtr.Zero, hwnd);
            try
            {
                var nid = new NOTIFYICONDATA
                {
                    hWnd = hwnd,
                    uID = 0x1001,
                    uFlags = Win32.NIF_MESSAGE | Win32.NIF_TIP,
                    uCallbackMessage = Win32.WM_TRAYICON,
                    szTip = "test",
                };
                Assert.True(Win32.Shell_NotifyIcon(Win32.NIM_ADD, in nid), "NIM_ADD 应成功（封送结构被原生接受）");

                nid.uVersion = Win32.NOTIFYICON_VERSION_4;
                Win32.Shell_NotifyIcon(Win32.NIM_SETVERSION, in nid);

                var hide = new NOTIFYICONDATA
                {
                    hWnd = hwnd,
                    uID = 0x1001,
                    uFlags = Win32.NIF_STATE,
                    dwState = Win32.NIS_HIDDEN,
                    dwStateMask = Win32.NIS_HIDDEN,
                };
                Assert.True(Win32.Shell_NotifyIcon(Win32.NIM_MODIFY, in hide), "NIM_MODIFY 隐藏应成功");

                var balloon = new NOTIFYICONDATA
                {
                    hWnd = hwnd,
                    uID = 0x1001,
                    uFlags = Win32.NIF_INFO,
                    szInfo = "气泡测试",
                    szInfoTitle = "标题",
                    dwInfoFlags = Win32.NIIF_INFO,
                };
                Assert.True(Win32.Shell_NotifyIcon(Win32.NIM_MODIFY, in balloon), "NIM_MODIFY 气泡应成功");
            }
            finally
            {
                var del = new NOTIFYICONDATA { hWnd = hwnd, uID = 0x1001 };
                Win32.Shell_NotifyIcon(Win32.NIM_DELETE, in del);
                Win32.DestroyWindow(hwnd);
            }
        });
    }
}
