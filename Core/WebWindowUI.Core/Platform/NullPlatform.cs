namespace WebWindowUI.Core.Platform;

/// <summary>
/// 未注册平台时的兜底实现（构造抛 PlatformNotSupportedException——测试泵的
/// <c>EnsurePlatformRegistered</c> 靠该异常兜底注册真实平台）。
/// </summary>
public class NullPlatform : IPlatform
{
    /// <summary>
    /// 构造兜底平台（未注册时抛异常，提示先调 WebWindowUIPlatform.Init 或注册平台）。
    /// </summary>
    public NullPlatform()
    {
        throw new PlatformNotSupportedException("未注册平台，请先调用 WebWindowUIPlatform.Init 或 WebWindowPlatform.Register。");
    }

    /// <summary>
    /// 创建窗口（未实现）。
    /// </summary>
    /// <param name="options">窗口选项。</param>
    /// <returns>平台窗口。</returns>
    public WebWindow CreateWindow(WebWindowOptions options)
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
    /// 平台剪贴板（未实现）。
    /// </summary>
    public IClipboard Clipboard => throw new NotImplementedException();

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
