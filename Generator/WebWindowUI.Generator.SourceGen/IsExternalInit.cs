// netstandard2.0 缺 record 所需的 init 访问器支持（record 的 Equals/GetHashCode 等成员声明 init 访问器），
// 编译器要求 System.Runtime.CompilerServices.IsExternalInit 存在。internal 补齐，与本程序集外的任何
// 定义无冲突；文件范围命名空间限制 → 单独文件用块命名空间声明（不能与 file-scoped namespace 共存于同一文件）。
namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit
    {
    }
}
