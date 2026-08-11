#if WINDOWS
using System.Runtime.CompilerServices;

namespace WebWindowUI.Tests.Platform.Support;

/// <summary>
/// 程序集加载即启动 STA 泵线程（不阻塞：loader lock 期间线程要等装配完成才能执行，
/// 就绪等待由首次 RunAsync 承担）。真 WebView2 测试经 StaThreadPump 跑在同一根 STA 线程上。
/// </summary>
internal static class TestBootstrap
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        _ = StaThreadPump.Instance;
    }
}
#endif
