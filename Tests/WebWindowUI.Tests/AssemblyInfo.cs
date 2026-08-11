using Xunit;

// 单例 UiThreadId / 消息窗口是线程绑定的（首次创建 WebWindow 的线程拥有它们）。
// 所有测试必须串行；真 WebView2 测试经 StaThreadPump 跑在同一根 STA 线程上。
[assembly: CollectionBehavior(DisableTestParallelization = true)]
