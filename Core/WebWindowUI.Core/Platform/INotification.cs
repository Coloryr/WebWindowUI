using System;
using System.Collections.Generic;
using System.Text;

namespace WebWindowUI.Core.Platform;

/// <summary>
/// 系统通知样式（Windows 气泡/通知中心、Linux 通知服务、macOS 通知）。
/// </summary>
public enum NotificationType
{
    /// <summary>
    /// 信息
    /// </summary>
    Info,
    /// <summary>
    /// 警告
    /// </summary>
    Warning,
    /// <summary>
    /// 错误
    /// </summary>
    Error,
}

/// <summary>
/// 系统通知抽象：独立于托盘图标的原生通知（标题/内容/样式），点击经 <see cref="Clicked"/> 回传。
/// </summary>
public interface INotification
{
    /// <summary>
    /// 显示一条系统通知（重复调用刷新已显示的通知）。
    /// </summary>
    /// <param name="title">标题。</param>
    /// <param name="text">内容。</param>
    /// <param name="type">通知样式。</param>
    void Show(string title, string text, NotificationType type = NotificationType.Info);

    /// <summary>
    /// 关闭当前通知（未显示时无操作）。
    /// </summary>
    void Close();

    /// <summary>
    /// 通知被点击时触发。
    /// </summary>
    event Action? Clicked;
}
