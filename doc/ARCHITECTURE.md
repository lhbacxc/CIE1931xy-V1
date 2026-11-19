# 架构与计算边界

本文记录当前源码事实。源码路径相对 `./src/CIE1931xy.Rebuilt/`（项目根目录），测试和脚本路径另行标明。快速入口见 [项目入门指南](./PROJECT_GUIDE.md)，运行与验证见 [开发指南](./DEVELOPMENT.md)。

## 入口、状态与错误处理

`Program.Main` 在创建窗口前解析参数、注册 CodePagesEncodingProvider，再选择新版 PerMonitorV2 / Microsoft YaHei UI 或经典 DpiUnaware / Microsoft Sans Serif。`--selftest` 调用自检，不打开主窗口；默认运行 MainForm，`--classic` 运行 Form1。

主入口及 GoldenTests 控制台输出统一 UTF-8，供开发脚本重定向日志使用。编译使用 global.json 约束的 .NET 10 SDK，运行目标仍为 .NET 8；build.ps1 分别寻找 SDK 与匹配目标的运行时宿主，开发工具版本不会自动改变业务计算。

MainForm 是新版交互协调器，持有 `List<CieCurve>`、`List<CiePoint>` 和 `DiagramStyle`；数据不经过数据库或文件持久化。曲线包含原始波长/强度、名称、标记样式和结果缓存；坐标点包含 xy、样式和派生 CCT，U/V 为换算属性。导出文件名也只在当前进程内保存。

文件导入按文件捕获异常；解析器返回 `ParseResult.Curves/Points/Warnings/Errors`，成功项进入内存模型，警告/错误进入消息与状态反馈。导入后更新表格、必要时切到坐标点页，再请求重绘。经典链路使用 `ParseFailure` 和 `CieMath.Status`，由 Form1 映射为对话框。

## 两条计算链路

| 项目 | 经典界面 | 新版工作台 |
| --- | --- | --- |
| 输入 | `SpectralInput.TryParse`，保留原 VB 文本解析语义 | `SpectrumCsvParser.Parse` / `CoordinateParser.Parse` |
| 核心 | `CieMath.Calculate` | `Colorimetry.AnalyzeCurve` / `FillPointDerived` |
| 网格 | 选择 1nm / 5nm，并检查间距 | 波长排序后处理任意网格，CMF 线性插值 |
| XYZ | 按配色函数表逐点求和 | `SpectrumToXyz` 逐点求和；360–830nm 外不参与计算 |
| xy、u'v' | 计算后按 VB Int/Fix 语义取四位小数 | 保留计算值，展示与导出各自格式化 |
| 格式 | `VbText.FormatDouble` 使用 G15 和当前区域设置 | 解析/数据导出主要使用 InvariantCulture |
| 回归 | GoldenTests 的数值与文本精确比较 | SelfTest 的容差检查、解析和导出检查 |

配色函数事实源是 `Core/CieData.cs` 的 `Cie1931xyz`：360–830nm、1nm、471 行。经典双语文案也保存在该文件；改表格数据可能同时影响经典、新版、光谱边界和黑体计算。

新版默认公式为 `X=ΣS·x̄`、`Y=ΣS·ȳ`、`Z=ΣS·z̄`，没有乘波长间隔、没有乘标准光源、没有 Y=100 归一。`SpectrumToXyzTrapezoid` 实现梯形积分和可选 Y=100，但 AnalyzeCurve 默认不调用它，也没有 UI 切换入口；不能用旧合并计划中的“归一化默认开启”解释结果。

`xy` 来自 XYZ 总和归一；CIE1976 坐标为 `u'=4x/(-2x+12y+3)`、`v'=9y/(-2x+12y+3)`。独立坐标点没有原始光谱，其 CSV XYZ 在 Y=1 假设下反推，与光谱测量值含义不同。

`PlanckianLocus` 根据普朗克定律和 CMF 表计算黑体；新版 `Colorimetry.CorrelatedColorTemperature` 先查 1000–25000K 缓存轨迹的 CIE1960 uv 最近点，再在附近 ±150K 以约 10K 步长细化。缓存当前是 801 个采样点，旧注释“400 点”不准确。

当前 Δuv 返回最近点的欧氏距离，始终非负；`CctReliable` 条件为距离 ≤0.05。它不是带正负号的标准 Duv，不应据此判断点在黑体线哪一侧。经典 `PlanckianOverlay` 是独立叠加绘制入口，与新版 DiagramRenderer 的黑体图层分别维护。

## 解析与编码

- `SpectrumCsvParser.ReadTextFile`：先识别 UTF-8/UTF-16 BOM，再严格 UTF-8、GBK(936)，最后 Latin-1。GBK 依赖入口注册代码页提供程序；默认 UTF-8 替换回退可能掩盖 GBK 解码错误。
- `SpectrumCsvParser.Parse`：检测分隔符，统计数值列，识别波长列与强度列，跳过说明行，排序、合并重复波长并返回警告。波长列识别区间和计算的 CMF 区间不是同一个概念。
- `CoordinateParser.Parse`：识别 u'/v' 表头，在数值列对中打分挑坐标，不可靠时回退最后两列并警告；多余强度或编号列可能产生歧义。
- 新版文件输入与剪贴板共用解析器；项目源码和文档统一 UTF-8，兼容外部 GBK 数据不改变项目编码规则。

## 渲染、DPI 与资源

`Core/Rendering/PlotCanvas.cs` 定义 `IPlotCanvas` 和矩形/对齐等绘制协议；`GdiCanvas` 与 `SvgCanvas` 是两种输出。`DiagramRenderer` 接收样式和模型，完成布局、坐标映射、背景、轴、光谱边界、黑体、数据标记及图例，避免各导出格式自行计算点位置。

位图模式只在 xy 下生效：原图 684×732，内部轴矩形左上 (59,12)、宽 600、高 675；映射固定为 x=0–0.8、y=0–0.9。图像整体缩放后必须同步缩放内部轴矩形，否则描点会整体偏移。经典 Form1 也使用该绘图原点，不要依据 DiagramMapping 中过时的“原点恒为零”注释修改。

矢量模式由 `PlotSpace` 做 xy/u'v' 互转、光谱边界转换及数据到像素映射。UI 通过 `FixedBounds` 固定 xy 为 0–0.8 / 0–0.9，u'v' 为两轴 0–0.65；底层类型保留 Bounds 能力不等于当前 UI 支持轴范围编辑。

矢量图层顺序：xy 的 x+y=1 参考线、网格、真彩背景、边框与坐标轴、光谱轨迹与波长标注、可选 sRGB 三角、黑体线、曲线标记/独立坐标点、图例。位图已有网格、轴和波长文字，不重复绘制这些图层；黑体开关直接选择两张底图。

`ChromaBackground` 计算并缓存真彩位图，屏幕和 PNG/TIFF 使用该位图；SVG 矢量模式采样为最多 160 列色块，按光谱多边形 clipPath 裁剪并重叠色块防缝。矢量色块是色彩近似，不代表每个像素都与预览相同。

`Core/AppResources.cs` 使用 ManifestResourceStream 加载两张底图与图标，资源逻辑名在 csproj 显式声明。Bitmap.Clone 避免按目标 DPI 再绘制导致的放大裁切；改文件名须同步 csproj 与加载名。

新版预览由 140ms WinForms Timer 合并重绘，在 UI 线程同步计算和渲染；没有后台 Task 渲染管线。`RenderNow` 使用控件尺寸（上限 2400×2400，不足时回退 1000×1000），调用 Exporter，再把位图克隆到 PictureBox.Zoom。旧预览 Bitmap 会 Dispose。

UI 图片导出前克隆 DiagramStyle 并关闭 HighlightSelection。Exporter 本身不会自动排除选中高亮，其他调用者需要自行设置。

## 导出与检查边界

| 输出 | 实现与限制 |
| --- | --- |
| PNG/TIFF | GDI 画布先按 96DPI 渲染，再设置目标 DPI 元数据；UI 固定 1400×1400，不按 DPI 放大像素 |
| SVG 矢量背景 | 色块、clipPath、text、线和标记；不含 image 元素 |
| SVG 位图背景 | SvgCanvas.DrawImage 内嵌 base64 PNG，其上的数据标记仍是矢量 |
| CSV | UTF-8 BOM；曲线 XYZ/xy/u'v'/峰值/CCT 等，独立点以 Y=1 反推 XYZ；有限显示精度 |
| TXT | UTF-8 BOM；公式和结果，旧“1nm 采样求和”文字没有表达任意输入网格 |

导出字体缩放为 `clamp((width+height)/2000,1,2.4)`；预览同样经过 Exporter，不能仅凭旧注释断言最大化时字号完全固定。

GoldenTests 通过 Compile Link 包含 CieData、CieMath、SpectralInput、VbText；不加载 Form1 或主工程，不能验证记录表累加、选择状态与像素。SelfTest 检查 12 个数值条件及十个输出文件的最小大小；两个 u'v' 背景参数组合都回退矢量，不代表有 u'v' 位图资源。

## 文档维护映射

- 用户可见行为更新 [README](../README.md)，架构、计算、渲染约束更新本文，环境、命令与验证结论更新 [开发指南](./DEVELOPMENT.md)。
- 原版调查及旧像素比对留在 [逆向工程记录](./REVERSE_ENGINEERING.md)，旧轮次与计划留在 [修订摘录](./CHANGELOG.md) 和 [合并计划](./MERGE_PLAN.md)。
- CodeGraph 用于定位调用和影响范围；生成关系仍需以实际方法体确认，例如事件委托、接口调用与源码链接不一定完整体现在图中。
