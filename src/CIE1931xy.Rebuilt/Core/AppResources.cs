using System;
using System.Drawing;
using System.IO;
using System.Reflection;

namespace CIE1931xy;

/// <summary>
/// 内嵌资源访问器（替代 VB.NET 工程自动生成的 My.Resources）。
/// 两个版本的程序唯一真正的差别就是这张背景位图：带黑体辐射轨迹 vs 不带。
/// </summary>
internal static class AppResources
{
    /// <summary>true = 使用带黑体辐射轨迹（Planckian locus）的底图，等价于原版 V.1.6.0.2；
    /// false = 使用不带轨迹的底图，等价于原版 V.1.6.0.2a(2)。由命令行 --no-blackbody 或运行时 Ctrl+B 切换。</summary>
    internal static bool WithBlackbody = true;

    private static Bitmap _diagramWithBlackbody;
    private static Bitmap _diagramWithoutBlackbody;

    /// <summary>色度图背景位图（684 x 732，与原版内嵌资源逐字节一致）。</summary>
    internal static Bitmap Diagram
    {
        get
        {
            if (WithBlackbody)
                return _diagramWithBlackbody ??= LoadBitmap("cie1931xy.with-blackbody.png");
            return _diagramWithoutBlackbody ??= LoadBitmap("cie1931xy.without-blackbody.png");
        }
    }

    /// <summary>按变体取底图（不改变全局 <see cref="WithBlackbody"/>），供渲染器使用。</summary>
    internal static Bitmap GetDiagramBitmap(bool withBlackbody)
    {
        if (withBlackbody)
            return _diagramWithBlackbody ??= LoadBitmap("cie1931xy.with-blackbody.png");
        return _diagramWithoutBlackbody ??= LoadBitmap("cie1931xy.without-blackbody.png");
    }

    /// <summary>程序图标（与原版 app.ico 一致）。</summary>
    internal static Icon Icon => LoadIcon("app.ico");

    private static Bitmap LoadBitmap(string logicalName)
    {
        using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(logicalName)
            ?? throw new InvalidOperationException("缺少内嵌资源: " + logicalName);
        using Bitmap raw = new Bitmap(stream);
        // 统一成 32bppArgb，避免不同底图格式（24bpp vs 32bpp）导致后续绘图差异。
        // 用 Clone 而不是 DrawImageUnscaled：后者会按“目标画布 DPI / 图片 DPI”放大
        // （本机 125% 缩放时是 1.25 倍）并裁掉右下角，底图会变成放大后的局部。
        return raw.Clone(new Rectangle(0, 0, raw.Width, raw.Height),
                         System.Drawing.Imaging.PixelFormat.Format32bppArgb);
    }

    private static Icon LoadIcon(string logicalName)
    {
        using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(logicalName)
            ?? throw new InvalidOperationException("缺少内嵌资源: " + logicalName);
        return new Icon(stream);
    }
}
