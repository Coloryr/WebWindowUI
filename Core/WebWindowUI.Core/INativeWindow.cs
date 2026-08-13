using System.Drawing;

namespace WebWindowUI.Core;

/// <summary>
/// 平台原生窗口抽象：封装窗口句柄生命周期，供各平台窗口壳复用。
/// </summary>
public interface INativeWindow
{
    /// <summary>
    /// 窗口销毁时触发。
    /// </summary>
    event Action? Destory;

    /// <summary>
    /// 窗口尺寸变化时触发。
    /// </summary>
    event Action? Resize;

    /// <summary>
    /// 平台窗口句柄。
    /// </summary>
    IntPtr WindowHandle { get; }

    /// <summary>
    /// 显示窗口。
    /// </summary>
    void Show();

    /// <summary>
    /// 隐藏窗口（不销毁）。
    /// </summary>
    void Hide();

    /// <summary>
    /// 关闭窗口。
    /// </summary>
    void Close();

    /// <summary>
    /// 激活/聚焦窗口。
    /// </summary>
    void Activate();

    /// <summary>
    /// 修改标题。
    /// </summary>
    /// <param name="title">新标题。</param>
    void SetTitle(string title);

    /// <summary>
    /// 设置图标。
    /// </summary>
    /// <param name="icon">窗口图标。</param>
    void SetIcon(WindowIcon icon);

    /// <summary>
    /// 获取窗口尺寸。
    /// </summary>
    /// <returns>窗口矩形。</returns>
    Rectangle GetSize();
}
