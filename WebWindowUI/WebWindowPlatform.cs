namespace WebWindowUI;

/// <summary>
/// 平台入口：在编译期（构建时按当前操作系统设置 WINDOWS / LINUX / MACOS 编译符号）
/// 就确定使用哪个平台的实现，运行时不再做任何判断。
/// 新增平台只需在 Platforms 下添加实现并定义对应编译符号。
/// </summary>
public static class WebWindowPlatform
{
    public static IWebWindowPlatform Current { get; } = Create();

    private static IWebWindowPlatform Create()
    {
#if WINDOWS
        return new WebWindowUI.Windows.WindowsPlatform();
#elif LINUX
        return new WebWindowUI.Linux.LinuxPlatform();
#elif MACOS
        return new WebWindowUI.MacOS.MacOSPlatform();
#else
        throw new PlatformNotSupportedException(
            "当前构建没有选中的 WebWindow 平台实现。请在 csproj 里按操作系统定义 WINDOWS / LINUX / MACOS 编译符号。");
#endif
    }
}
