using System;
using System.Drawing;
using System.Windows.Forms;

namespace CIE1931xy;

internal static class Program
{
    /// <summary>启动时是否就叠加"代码实时计算"的黑体轨迹（仅经典界面 <c>--planckian</c>）。</summary>
    internal static bool StartWithPlanckian;

    /// <summary>启动时自动导入的文件（<c>--open 路径</c>），null 表示不导入。</summary>
    internal static string StartupFile;

    /// <summary>
    /// 程序入口。
    /// 命令行参数：
    ///   --classic       打开经典界面（原版 1.6.0.2 的布局，Form1）
    ///   --no-blackbody  经典界面使用"不带黑体辐射轨迹"的底图（等价原版 V.1.6.0.2a(2)）
    ///   --blackbody     经典界面使用"带黑体辐射轨迹"的底图（等价原版 V.1.6.0.2，缺省）
    ///   --planckian     经典界面启动即打开"代码实时计算"的黑体轨迹叠加层（Ctrl+P 可切换）
    ///   --selftest [目录] 无界面自检：算色度 + 导出全部格式，成功返回 0
    /// 默认打开新版主界面 <see cref="MainForm"/>（2.0.0）。
    /// </summary>
    [STAThread]
    private static void Main(string[] args)
    {
        // 无窗口自检也统一输出 UTF-8；没有控制台句柄时继续正常启动界面。
        try { Console.OutputEncoding = new System.Text.UTF8Encoding(false); }
        catch (System.IO.IOException) { }

        // DPI 感知模式必须在创建任何窗口之前决定，所以先解析参数。
        bool? variant = null;
        bool classic = false;
        string selfTestDir = null;
        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            if (string.Equals(arg, "--no-blackbody", StringComparison.OrdinalIgnoreCase))
            {
                variant = false;
            }
            else if (string.Equals(arg, "--blackbody", StringComparison.OrdinalIgnoreCase))
            {
                variant = true;
            }
            else if (string.Equals(arg, "--planckian", StringComparison.OrdinalIgnoreCase))
            {
                StartWithPlanckian = true;
            }
            else if (string.Equals(arg, "--classic", StringComparison.OrdinalIgnoreCase))
            {
                classic = true;
            }
            else if (string.Equals(arg, "--open", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                StartupFile = args[++i];
            }
            else if (string.Equals(arg, "--selftest", StringComparison.OrdinalIgnoreCase))
            {
                selfTestDir = i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal)
                    ? args[i + 1]
                    : System.IO.Path.Combine(System.IO.Path.GetTempPath(), "cie1931-selftest");
            }
        }

        // 注册代码页编码提供程序：.NET 8 默认只有 UTF-8/UTF-16/ASCII，
        // 不注册就取不到 GBK(936)，中文 CSV 会退化成 Latin-1 乱码。
        try { System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance); }
        catch (Exception) { }

        if (classic)
        {
            // 经典界面：与原版 1.6.0.2 一样按 96dpi 直接绘制（DpiUnaware），
            // 保证界面、底图、描点与原版逐像素一致；在高分屏上由系统整体放大。
            Application.SetHighDpiMode(HighDpiMode.DpiUnaware);
            Application.SetDefaultFont(new Font("Microsoft Sans Serif", 8.25F, FontStyle.Regular, GraphicsUnit.Point));
        }
        else
        {
            // 新版界面：按显示器 DPI 缩放（PerMonitorV2），高分屏上文字清晰、不糊。
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.SetDefaultFont(new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point));
        }

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        // 无界面自检：渲染 + 导出全部格式，返回失败项数量（0 = 通过）。
        if (selfTestDir != null)
        {
            int failures = Core.SelfTest.Run(selfTestDir, Console.Out);
            Console.Out.Flush();
            Environment.ExitCode = failures == 0 ? 0 : 1;
            return;
        }

        // 经典界面：没显式指定底图时，叠加层开着就用干净底图（原版 1.6.0.2a(2)），否则用带烘焙轨迹的那张。
        AppResources.WithBlackbody = variant ?? !StartWithPlanckian;

        if (classic)
        {
            Application.Run(new Form1());
        }
        else
        {
            Application.Run(new MainForm());
        }
    }
}
