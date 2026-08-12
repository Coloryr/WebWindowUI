// 标记程序集：后端模型库工程 ProjectReference/PackageReference 引用本包即声明自己是「后端模型库」。
// WebWindowUI.targets 据此识别角色（WebWindowUIIsBackend），触发模型→proto/descriptor/TS 生成。
namespace WebWindowUI;

/// <summary>
/// 后端角色标记：引用 WebWindowUI.Backend 即证明本工程是后端模型库。
/// </summary>
public static class WebWindowUIBackendMarker
{
}
