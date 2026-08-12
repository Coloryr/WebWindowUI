namespace WebWindowUI;

/// <summary>
/// 前端程序集宿主标记（由 targets 注入编译进每个前端工程 dll，Release）。前端 dll 是纯 Vue 产物、
/// 本身无 C# 类型，应用侧 <see cref="FrontendLoad"/> 用 <c>typeof</c> 引用本类型强制加载/链接它
/// （AOT 安全），使内嵌 wwwroot 能被 WebResourceResolver 扫描到。
/// </summary>
public static class FrontendHost
{
}
