namespace WebWindowUI;

/// <summary>
/// 前端程序集宿主标记（随 WebWindowUI 包分发，由 targets 注入编译进每个前端工程 dll，Release）：
/// 为前端 dll 提供一个可静态引用的类型。前端 dll 是纯 Vue 工程产物、本身无任何 C# 类型，
/// 应用侧 <see cref="FrontendLoad"/> 的模块初始化器用 <c>typeof</c> 引用本类型来强制加载/链接它
/// （AOT 安全：编译期静态引用，无运行时按名加载），使 WebResourceResolver 的已加载程序集扫描能发现内嵌 wwwroot。
/// </summary>
public static class FrontendHost
{
}
