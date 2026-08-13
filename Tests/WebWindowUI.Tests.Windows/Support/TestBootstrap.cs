using System.Runtime.CompilerServices;

namespace WebWindowUI.Tests.Windows.Support;

/// <summary>
/// 程序集加载即启动 STA 泵线程（不阻塞：loader lock 期间线程要等装配完成才能执行，
/// 就绪等待由首次 RunAsync / 泵初始化承担）。真 WebView2 测试经 StaThreadPump 跑在同一根
/// STA 线程上。只启动泵线程——泵线程在自身创建隐藏消息窗口后注册平台（见 StaThreadPump），
/// 任何其它线程先加载平台程序集都会把隐藏消息窗口建在那个线程上，async 延续永不派发。
/// </summary>
internal static class TestBootstrap
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        _ = StaThreadPump.Instance;
    }
}
