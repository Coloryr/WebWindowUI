using System;
using System.Collections.Generic;
using System.Text;

namespace WebWindowUI.Core.Platform;

/// <summary>
/// 弹出菜单（右键菜单）项：支持嵌套子菜单、分隔符、启用/禁用与选中状态。
/// 平台实现递归构建原生菜单，点击时按菜单项序号触发 <see cref="Click"/>。
/// </summary>
public class PopupMenu
{
    /// <summary>
    /// 子菜单项（非空即渲染成子菜单，忽略 <see cref="IsSeparator"/>）。
    /// </summary>
    public List<PopupMenu> Menus { get; set; } = [];

    /// <summary>
    /// 菜单文本（分隔符忽略）。
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// 是否为分隔符。
    /// </summary>
    public bool IsSeparator { get; set; }

    /// <summary>
    /// 是否可用（false 置灰不可点击）。
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// 是否选中（勾选标记；分隔符忽略）。
    /// </summary>
    public bool IsChecked { get; set; }

    /// <summary>
    /// 菜单项被点击时触发。
    /// </summary>
    public event Action? Click;

    /// <summary>
    /// 触发点击事件（平台菜单命令回调）。
    /// </summary>
    public void OnClick()
    {
        Click?.Invoke();
    }
}
