using WebWindowUI.Core;

namespace WebWindowUI;

/// <summary>
/// 平台引导（入口包）：构建期 targets 注入 PlatformBootstrap.g.cs 惰性登记加载委托，Main 首行 Init 触发（AOT 安全）。
/// </summary>
public static class WebWindowUIPlatform
{
    /// <summary>
    /// 注册平台实现（构建期由注入的 PlatformBootstrap.g.cs 调用，消费方勿手写；惰性登记，幂等）。
    /// </summary>
    /// <param name="platform">平台实现。</param>
    public static void RegisterPlatformLoader(IPlatform platform)
    {
        WebWindowPlatform.Register(platform);
    }

    public static void Init(string[] args)
    {
        WebWindowPlatform.Current.Init(args);
    }

    /// <summary>
    /// 运行当前平台的消息循环，直到所有窗口关闭后返回。
    /// </summary>
    public static void Run()
    {
        WebWindowPlatform.Current.RunMessageLoop();
    }
}
