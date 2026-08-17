namespace WebWindowUI.Core.Platform;

/// <summary>
/// 平台消息循环抽象：平台窗口层经它投递 UI 线程任务并运行消息泵。
/// </summary>
public interface IMessageLoop
{
    /// <summary>
    /// 初始化消息循环（隐藏消息窗口等基础设施）。
    /// </summary>
    void InitMessageLoop();

    /// <summary>
    /// 运行消息循环，直到循环退出后返回。
    /// </summary>
    void MessageLoop();

    /// <summary>
    /// 当前线程是否 UI 线程。
    /// </summary>
    /// <returns>是否在 UI 线程。</returns>
    bool IsUiThread();

    /// <summary>
    /// 把委托投递到 UI 线程执行。
    /// </summary>
    /// <param name="action">要在 UI 线程执行的委托。</param>
    void RunOnUiThread(Action action);
}
