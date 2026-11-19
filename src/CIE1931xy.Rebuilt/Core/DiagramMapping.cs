using System;

namespace CIE1931xy.Core;

/// <summary>
/// 色品坐标 (x, y) 到画布像素坐标的映射，与原版 <c>btnDrawPoint_Click</c> 里的公式完全一致：
/// <code>
/// px = Round(O.X + x / pixelperxy)
/// py = Round(O.Y + Axes_Height - y / pixelperxy)
/// </code>
/// 原版中 <c>O</c> 从未被赋值（恒为 0,0）、<c>pixelperxy = 0.8 / 600</c>、<c>Axes_Height = 675</c>，
/// 即 1 个 x/y 单位 = 750 像素；x 轴 0→0.8 对应 0→600 像素，y 轴 0→0.9 对应 675→0 像素。
/// </summary>
internal readonly struct DiagramMapping
{
    internal DiagramMapping(double originX, double originY, double axesHeight, double pixelPerXy)
    {
        OriginX = originX;
        OriginY = originY;
        AxesHeight = axesHeight;
        PixelPerXy = pixelPerXy;
    }

    internal double OriginX { get; }

    internal double OriginY { get; }

    internal double AxesHeight { get; }

    internal double PixelPerXy { get; }

    /// <summary>x → 画布横坐标（像素）。</summary>
    internal int Px(double x) => (int)Math.Round(OriginX + x / PixelPerXy);

    /// <summary>y → 画布纵坐标（像素，向下为正）。</summary>
    internal int Py(double y) => (int)Math.Round(OriginY + AxesHeight - y / PixelPerXy);

    /// <summary>画布横坐标 → x（用于调试/反向换算）。</summary>
    internal double XOf(double px) => (px - OriginX) * PixelPerXy;

    /// <summary>画布纵坐标 → y。</summary>
    internal double YOf(double py) => (OriginY + AxesHeight - py) * PixelPerXy;
}
