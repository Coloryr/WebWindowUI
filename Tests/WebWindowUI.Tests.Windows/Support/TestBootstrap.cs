using System.Runtime.CompilerServices;

namespace WebWindowUI.Tests.Windows.Support;

/// <summary>
/// 程序集加载即启动 STA 泵线程（不阻塞：loader lock 期间线程要等装配完成才能执行，就绪等待由首次
/// RunAsync 承担）。泵线程在自身创建隐藏消息窗口后注册平台，任何其它线程先加载平台都会把窗口建错线程。
/// </summary>
internal static class TestBootstrap
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        _ = StaThreadPump.Instance;
    }
}
