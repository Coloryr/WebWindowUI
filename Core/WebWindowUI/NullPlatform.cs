using WebWindowUI.Core;

namespace WebWindowUI;

/// <summary>
/// 未注册平台时的兜底实现（所有成员抛 NotImplementedException）。
/// </summary>
public class NullPlatform : IPlatform
{
    /// <summary>
    /// 构造兜底平台（抛未实现异常）。
    /// </summary>
    public NullPlatform()
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// 创建窗口后端（未实现）。
    /// </summary>
    /// <param name="options">窗口选项。</param>
    /// <returns>窗口后端。</returns>
    public IWindowBackend CreateWindow(WebWindowOptions options)
    {
        throw new NotImplementedException();
    }

    public void Init(string[] args)
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// 是否在 UI 线程（未实现）。
    /// </summary>
    /// <returns>是否在 UI 线程。</returns>
    public bool IsUiThread()
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// 平台对话框（未实现）。
    /// </summary>
    public IPlatformDialog Dialog => throw new NotImplementedException();

    /// <summary>
    /// 运行消息循环（未实现）。
    /// </summary>
    public void RunMessageLoop()
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// 在 UI 线程运行委托（未实现）。
    /// </summary>
    /// <param name="action">委托。</param>
    public void RunOnUiThread(Action action)
    {
        throw new NotImplementedException();
    }
}
