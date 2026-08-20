using WebWindowUI.Core;
using WebWindowUI.Tests.Linux.Support;
using Xunit;

namespace WebWindowUI.Tests.Linux;

/// <summary>
/// Linux 窗口状态面 E2E：CanMinimize/CanMaximize/CanResize/SetIcon 经真实 GTK3 平台验证
/// （不绑模型、不等桥接——窗口状态能力不依赖页面/桥）。无头模式窗口永不 show/realize，
/// CanMinimize/CanMaximize setter 只记跟踪字段、WM 功能位延迟到 realize 落地，此处验证
/// 公开 WebWindow API 的往返、UI 线程 marshal 与异常安全。
/// </summary>
[Collection("webkit")]
public class LinuxWindowStateTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    /// <summary>
    /// 新建窗口默认全可操作（可缩放/可最小化/可最大化/显示在任务栏）。
    /// </summary>
    [Fact]
    public async Task WindowState_Defaults_AllEnabled()
    {
        await WebKitTestHarness.RunWindowAsync("demo", "窗口状态", async win =>
        {
            Assert.True(win.Window.CanResize);
            Assert.True(win.Window.CanMinimize);
            Assert.True(win.Window.CanMaximize);
            Assert.True(win.Window.ShowInTaskbar);
        }, Timeout);
    }

    /// <summary>
    /// CanMinimize/CanMaximize 经公开 WebWindow API 往返（getter 读跟踪字段；setter marshal 主线程）。
    /// </summary>
    [Fact]
    public async Task CanMinimize_CanMaximize_RoundTrip()
    {
        await WebKitTestHarness.RunWindowAsync("demo", "窗口状态", async win =>
        {
            win.Window.CanMinimize = false;
            win.Window.CanMaximize = false;
            Assert.False(win.Window.CanMinimize);
            Assert.False(win.Window.CanMaximize);

            win.Window.CanMinimize = true;
            win.Window.CanMaximize = true;
            Assert.True(win.Window.CanMinimize);
            Assert.True(win.Window.CanMaximize);
        }, Timeout);
    }

    /// <summary>
    /// CanResize 往返不受同步 WM 功能位影响（set 走 gtk_window_set_resizable + 掩码重算）。
    /// </summary>
    [Fact]
    public async Task CanResize_RoundTrip()
    {
        await WebKitTestHarness.RunWindowAsync("demo", "窗口状态", async win =>
        {
            win.Window.CanResize = false;
            Assert.False(win.Window.CanResize);
            win.Window.CanResize = true;
            Assert.True(win.Window.CanResize);
        }, Timeout);
    }

    /// <summary>
    /// 真实图标（Sample wwwroot 的 app.ico）经 SetIcon 落地：不抛异常 + 图标流被真实消费。
    /// 流位置 > 0 即回归信号——旧 no-op 实现不读流、位置停留 0。
    /// </summary>
    [Fact]
    public async Task SetIcon_ValidIcon_NoThrow_ConsumesStream()
    {
        using Stream? iconStream = WebWindowResource.Resolve("icon/app.ico");
        Assert.NotNull(iconStream);
        var icon = WindowIcon.FromStream(iconStream);

        await WebKitTestHarness.RunWindowAsync("demo", "窗口状态", async win =>
        {
            win.Window.SetIcon(icon);
            // 二次设置同一实例：SetIcon 内部 Seek(0) 重置流位置，否则第二次解码空文件
            win.Window.SetIcon(icon);
            Assert.True(icon.Stream.Position > 0, "SetIcon 应真实消费图标流数据");
        }, Timeout);
    }

    /// <summary>
    /// SetIcon(null) 不操作、不抛异常。
    /// </summary>
    [Fact]
    public async Task SetIcon_Null_NoThrow()
    {
        await WebKitTestHarness.RunWindowAsync("demo", "窗口状态", async win =>
        {
            win.Window.SetIcon(null);
        }, Timeout);
    }

    /// <summary>
    /// 不可解码字节（gdk_pixbuf_new_from_file 失败）→ 静默跳过、不抛异常；流仍被读取。
    /// </summary>
    [Fact]
    public async Task SetIcon_InvalidBytes_NoThrow()
    {
        var icon = WindowIcon.FromStream(new MemoryStream(new byte[] { 1, 2, 3, 4, 5 }));

        await WebKitTestHarness.RunWindowAsync("demo", "窗口状态", async win =>
        {
            win.Window.SetIcon(icon);
            Assert.True(icon.Stream.Position > 0, "SetIcon 应读取图标流（临时文件已写入）");
        }, Timeout);
    }
}
