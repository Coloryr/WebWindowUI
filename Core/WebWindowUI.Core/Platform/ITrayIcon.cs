using System;
using System.Collections.Generic;
using System.Text;

namespace WebWindowUI.Core.Platform;

/// <summary>
/// 托盘图标样式（气泡通知用）。
/// </summary>
public enum TrayIconType
{
    /// <summary>
    /// 无图标
    /// </summary>
    None = 0,
    /// <summary>
    /// 信息图标
    /// </summary>
    Info = 1,
    /// <summary>
    /// 警告图标
    /// </summary>
    Warning = 2,
    /// <summary>
    /// 错误图标
    /// </summary>
    Error = 3,
}

/// <summary>
/// 托盘点击按钮类型。
/// </summary>
public enum TrayClickType
{
    /// <summary>
    /// 左键
    /// </summary>
    Left,
    /// <summary>
    /// 右键
    /// </summary>
    Right,
    /// <summary>
    /// 中键
    /// </summary>
    Middle,
}

/// <summary>
/// 托盘点击事件参数（按钮类型 + 点击屏幕坐标）。
/// </summary>
public record TrayClickEvent
{
    /// <summary>
    /// 点击类型（左/右/中键）。
    /// </summary>
    public TrayClickType Type { get; }

    /// <summary>
    /// 点击时的屏幕坐标。
    /// </summary>
    public Point2I Position { get; }

    /// <summary>
    /// 构造点击事件。
    /// </summary>
    /// <param name="type">点击按钮类型。</param>
    /// <param name="position">点击屏幕坐标。</param>
    public TrayClickEvent(TrayClickType type, Point2I position)
    {
        Type = type;
        Position = position;
    }
}

/// <summary>
/// 系统托盘图标抽象：图标/提示/右键菜单/气泡通知/可见性，点击经事件回传（事件携带按钮类型与坐标）。
/// </summary>
public interface ITrayIcon
{
    /// <summary>
    /// 设置托盘图标
    /// </summary>
    /// <param name="icon">图标数据（文件或流）。</param>
    void SetIcon(WindowIcon icon);

    /// <summary>
    /// 设置托盘提示文本
    /// </summary>
    /// <param name="tip">提示文本（最多 127 字符）。</param>
    void SetTip(string tip);

    /// <summary>
    /// 设置右键菜单
    /// </summary>
    /// <param name="menu">菜单树（支持嵌套与分隔符）。</param>
    void SetMenu(PopupMenu menu);

    /// <summary>
    /// 打开弹窗（手动弹出右键菜单）
    /// </summary>
    void ShowMenu();

    /// <summary>
    /// 显示气泡通知
    /// </summary>
    /// <param name="title">标题（最多 63 字符）。</param>
    /// <param name="text">内容（最多 255 字符）。</param>
    /// <param name="type">通知样式。</param>
    void ShowBalloon(string title, string text, TrayIconType type = TrayIconType.Info);

    /// <summary>
    /// 显示或隐藏托盘图标
    /// </summary>
    /// <param name="visible">是否可见。</param>
    void SetVisible(bool visible);

    /// <summary>
    /// 移除托盘图标（窗口销毁时自动调用，重复调用安全）。
    /// </summary>
    void Delete();

    /// <summary>
    /// 单击（含左/右/中键，经 <see cref="TrayClickEvent.Type"/> 区分）。
    /// </summary>
    event Action<TrayClickEvent>? Click;

    /// <summary>
    /// 双击（左键双击）。
    /// </summary>
    event Action<TrayClickEvent>? DoubleClick;
}
