// 标记程序集：前端工程 ProjectReference/PackageReference 引用本包即声明自己是「前端工程」。
// WebWindowUI.targets 据此识别角色（WebWindowUIIsFrontend），激活 vite 构建目标（WebWindowUIBuildFrontend）。
namespace WebWindowUI;

/// <summary>前端角色标记：引用 WebWindowUI.Frontend 即证明本工程是前端（Vue + Vite）工程。</summary>
public static class WebWindowUIFrontendMarker
{
}
