namespace WebWindowUI.Core;

/// <summary>
/// 数据路由：把 DataScheme（默认 <c>appbin://</c>）下的一个子路径前缀映射为二进制资源。
/// 派生类实现 <see cref="ResolveBytes"/>；源生成器自动注册进 <see cref="DataRoutes"/>（[ModuleInitializer]，
/// AOT 安全），并同步产出前端 <c>src/models/dataRoutes.ts</c> 助手。
/// 路由应**直接继承本类**（TS 生成按「基类末段 == DataRoute」识别，间接派生不产 TS）。
/// </summary>
public interface IDataRoute
{
    /// <summary>
    /// 根据路径获取数据
    /// </summary>
    public Stream? ResolveBytes(string path);
}
