using System;
using System.Collections.Generic;
using System.Text;

namespace WebWindowUI.Core.Platform;

public enum SystemDecorations
{
    None,
    Border,
    Full
}

public enum WindowState
{
    Normal,
    Minimize,
    Maximize,
    Full,
    FullBorderLess
}

public struct Point2I
{ 
    public int X;
    public int Y;
}

public record Screen
{
    public int Index { get; }
    public Point2I Size { get; }

    /// <summary>
    /// 构造显示器信息。
    /// </summary>
    /// <param name="index">显示器序号（枚举序）。</param>
    /// <param name="size">显示器分辨率。</param>
    public Screen(int index, Point2I size)
    {
        Index = index;
        Size = size;
    }
}

public enum PointDataType
{ 
    LeftDown,
    RightDown,
    LeftUp,
    RightUp,
    MinDown,
    MinUp,
}

public record PointData
{
    public PointDataType Type { get; }
    public Point2I Pos { get; }
}