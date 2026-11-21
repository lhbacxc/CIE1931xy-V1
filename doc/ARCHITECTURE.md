# 架构与计算边界

本文记录当前源码事实。源码路径相对 `./src/CIE1931xy.Rebuilt/`（项目根目录），测试和脚本路径另行标明。快速入口见 [项目入门指南](./PROJECT_GUIDE.md)，运行与验证见 [开发指南](./DEVELOPMENT.md)。

## 入口、状态与错误处理

`Program.Main` 在创建窗口前解析参数、注册 CodePagesEncodingProvider，再选择新版 PerMonitorV2 / Microsoft YaHei UI 或经典 DpiUnaware / Microsoft Sans Serif。`--selftest` 调用自检，不打开主窗口；默认运行 MainForm，`--classic` 运行 Form1。

主入口及 GoldenTests 控制台输出统一 UTF-8，供开发脚本重定向日志使用。编译使用 global.json 约束的 .NET 10 SDK，运行目标仍为 .NET 8；build.ps1 分别寻找 SDK 与匹配目标的运行时宿主，开发工具版本不会自动改变业务计算。

MainForm 是新版交互协调器，持有 `List<CieCurve>`、`List<CiePoint>`、`List<CieArrow>` 和 `DiagramStyle`；业务数据不经过数据库或文件持久化。曲线包含原始波长/强度、名称、标记样式和结果缓存；坐标点包含 xy、样式和派生 CCT，U/V 为换算属性。箭头保存参考数据对象与位置偏移，软件偏好及导出文件名单独通过 JSON 配置保存。

`Core/AppSettings.cs` 定义有版本的偏好白名单：图型、各显示开关、字体与大小、两种图型的轴标题/间距、导出 DPI 与文件名。`Core/SettingsStore.cs` 读写 UTF-8 JSON；配置路径为 AppContext.BaseDirectory 下的 CIE1931xy.settings.json，取实际程序目录而非工作目录。读取后补默认值、核验版本与数值范围；文件缺失正常使用默认，损坏或不可读时反馈状态，不在初始化时重写原文件。字体不可用时回退本机默认字体。

`MainForm.Settings.cs` 协调恢复及保存：BuildUi/ApplyStyleDefaults 后恢复样式和控件，期间抑制 UI 事件，再启用保存；字体、坐标轴等对话框只在应用后触发。400ms Timer 合并连续更改，关闭时补存尚未写完的修改，比较规范化快照避免无变更写入。完整 JSON 先写到同目录独立临时文件并 Flush，再替换目标；失败保留旧配置、清理自身临时文件并提示状态。配置不包含原始数据、单条曲线样式、隐藏/选中状态、窗口几何、固定坐标范围或 FontScale 等渲染缓存；经典入口及自检不读写。测试可用 MainForm(string settingsPath) 隔离路径，传 null 禁用保存。

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

新版手工布局在 `BuildUi` 开始暂停布局，全部控件加入后才恢复布局，保证 96dpi 设计尺寸统一缩放；不能在控件尚未加入时触发首次 AutoScale，否则高 DPI 下文字与控件边界会不同步。分割条最小宽度及初始位置、表格初始列宽显式从逻辑单位转成设备像素；表头最小宽度按文字测量，表头和数据行高度自动适应内容。

数据操作栏位于左侧表格上方，包含「导入光谱 / 输入数据」和「批量改名」两组；图形操作栏位于右侧预览上方，包含「颜色 / 描边」、「添加箭头 / 管理箭头」和「导出数据 / 导出图片」三组。`MakeToolbar` 统一两侧的按钮高度、内边距与背景；`AddToolbarGroup` 使用嵌套 FlowLayoutPanel，组内不换行、外层按整组换行。组内间距为 `MainForm.ButtonSpacing`（6 个逻辑像素），组间为其三倍，换行间距为 6；显示设置及对话框按钮仍统一使用 6 个逻辑像素。`UpdateTables` 同步无数据时的按钮禁用状态，图片导出保留空白底图能力。

显示设置采用可伸缩的两列布局，标签和开关按文字调整尺寸；主设置面板按内容的 PreferredSize 和父容器空间测量高度，为数据页签预留空间。快捷键提示独立 Dock=Bottom，按宽度/字体/DPI 重测高度并分三行显示，不放进可滚动的设置项容器；小窗口只滚动设置项。坐标轴、字体和清空按钮共用按文字尺寸布局的 MakeBarButton，避免清空按钮的旧 202px 最小宽度迫使额外换行；测量用重入保护防止布局事件循环。统一输入数据、单项改名、批量改名对话框共用独立的 96dpi 初始化与自动布局按钮区；多行提示在宽度、字体或 DPI 变化后重新测量高度。经典窗口仍保持独立的 DpiUnaware 策略。

`InputData` 合并原来的坐标输入与粘贴窗口，数据类型为自动识别、色坐标或光谱；`Ctrl+Shift+O` 打开同一窗口并预选色坐标，`Ctrl+Shift+V` 打开默认输入窗口。`ParsePastedText` 按指定类型或自动判断调用解析器，并统一初始化名称、颜色、描边、形状，更新表格、切换相关页签与请求重绘；主窗口 `Ctrl+V` 读取剪贴板后复用该方法。经典解析与计算未改。

`UI/RoundedControls.cs` 为新版按钮、按钮式开关、下拉框与数值输入提供 4 个逻辑像素的小圆角，圆角和描边随 DPI 缩放。按钮/开关只替换绘制，沿用 WinForms 输入、自动尺寸、DialogResult 与可访问性；原生输入控件只补绘外框。共享绘制保留悬停、按下、禁用和键盘焦点提示。经典 Form1 不使用这些控件。

`Core/Rendering/PlotCanvas.cs` 定义 `IPlotCanvas` 和矩形/对齐等绘制协议；`GdiCanvas` 与 `SvgCanvas` 是两种输出。`DiagramRenderer` 接收样式和模型，完成布局、坐标映射、背景、轴、光谱边界、黑体、数据标记及图例，避免各导出格式自行计算点位置。

新版字体设置位于 `MainForm.FontSettings.cs`，MainForm 通过 partial 拆分该交互。设置窗口列出本机字体，分别编辑数字/字母字体和中文字体，以及坐标轴标题、刻度、波长、数据标签、图例和黑体温度的大小（8–64）；默认 Times New Roman / SimSun（宋体），大小依次为 26/22/20/20/20/17。应用后写入 DiagramStyle 并调度配置保存，取消不修改，恢复默认只重置窗口字段，须应用才生效。xy/u'v' 共用设置并跨进程保存，经典位图文字保持原状。

`IPlotCanvas.ChineseFontFamily` 由渲染器在开始绘制时设置。`PlotTextLayout` 按 Unicode Rune 分段，将汉字及中文标点与数字/字母/科学符号分开，组合附加符继承前一段字体；GDI 与 SVG 共用各段宽度和公共基线，测量与绘制使用相同字体。SVG 用 text/tspan 显式标注字体并保持混排空格，旋转围绕原对齐点，不嵌入字体文件。文字大小沿用像素基准并按画布缩放，不能当作 PNG/TIFF 的物理 pt；DPI 仍只影响图片元数据。

坐标轴标题、数据标签和图例按所选字体重新测量；过宽的数据标签和图例缩小到图内可用空间，数据标签位置也限制在绘图区。该适配不保证密集文字之间完全无重叠。`PlotTextLayout` 释放临时 Font，GdiCanvas 实现 IDisposable 并由 Exporter 释放缓存的 Pen/Brush；新增绘图调用同样须管理画布资源。

新版 `MainForm.ApplyStyleDefaults` 固定 `UseBitmapBackground=false`，xy/u'v' 均使用可编辑的矢量绘图，不再提供位图/矢量背景类型选择、切换菜单或新版 Ctrl+B。设置区说明统一矢量绘图便于独立控制网格和标注，导出仍可选择 PNG/TIFF 位图或 SVG 矢量图。预览仍由 GDI 渲染为 Bitmap 供 PictureBox 显示，并非新增 SVG 浏览器控件；SVG 导出使用同一绘图协议输出矢量元素。

「黑色背景」是独立的画布颜色开关，不改变背景绘图类型。`DiagramStyle.SetBlackBackground` 同步纯白/纯黑画布、坐标轴、刻度文字、网格、参考线、轮廓与图例边框颜色，默认白底；图例面板使用当前背景色。波长与数据标签在黑底模式下按所在区域的底色选黑字或白字，黑体线及色域颜色场仍使用原有颜色。按钮与视图菜单同步，切换图型保留背景选择，预览与 PNG/TIFF/SVG 读取同一份样式。

底层位图兼容分支只在 xy 下生效，仍由 SelfTest 覆盖：原图 684×732，内部轴矩形左上 (59,12)、宽 600、高 675；映射固定为 x=0–0.8、y=0–0.9。图像整体缩放后必须同步缩放内部轴矩形，否则描点会整体偏移。经典 Form1 也使用该绘图原点，不要依据 DiagramMapping 中过时的“原点恒为零”注释修改。

矢量模式由 `PlotSpace` 做 xy/u'v' 互转、光谱边界转换及数据到像素映射。UI 通过 `FixedBounds` 固定 xy 为 0–0.8 / 0–0.9，u'v' 为两轴 0–0.65；底层类型保留 Bounds 能力不等于当前 UI 支持轴范围编辑。

矢量图层顺序：xy 的 x+y=1 参考线、网格、真彩背景、边框与坐标轴、光谱轨迹与波长标注、可选 sRGB 三角、黑体线、曲线标记/独立坐标点、图例。位图已有网格、轴和波长文字，不重复绘制这些图层；黑体开关直接选择两张底图。

新版「参考线」按钮绑定 `DiagramStyle.ShowClosingLine`，仅控制浅灰 x+y=1（z=0）直线，默认隐藏；不控制光谱轨迹、色域边界或紫线。按钮与视图菜单同步状态，xy 下启用、u'v' 下禁用并保留选择。预览与 PNG/TIFF/SVG 导出共用该样式；底层保留该字段原有默认值，MainForm 单独设置新版默认值。

新版网格按钮始终可用，`OnStyleChanged` 直接同步 `ShowGrid`，切换图型保留设置；原来针对位图背景的网格禁用逻辑与偏好缓存已移除。无网格 PNG/TIFF/SVG 仅需关闭网格后选择输出格式。

`MainForm.EditAxisTitles` 在共用 DPI 对话框中编辑当前图型的横、纵轴标题及间距。`DiagramStyle` 分别保存 xy/u'v' 的标题覆盖值，空值使用 `PlotSpace` 默认名称；两种图型共用 `AxisTitleGapX/Y`（默认 16，UI 范围 0–80），应用后更新，取消不修改。

`DiagramRenderer.MeasureAxisLayout` 通过 `IPlotCanvas.MeasureText` 测量刻度和标题，间距从刻度文字的外缘起算，并随 FontScale 缩放；据此预留左侧和底部边距。横轴标题居中，纵轴标题整体旋转 -90°，长标题按可用空间缩小字号，避免文字重叠或裁切。GDI 与 SVG 使用同一布局逻辑。布局改变只影响绘图区位置与尺寸，不改变数据范围；`LastTransform` 在位图与矢量分支完成布局后统一保存，画布命中应使用最新变换，不复用固定像素边界。

`ChromaBackground` 计算并缓存真彩位图，屏幕和 PNG/TIFF 使用该位图。SVG 单独从无掩码颜色场 `SampleColor` 采样，不读取已按像素裁掉边缘的透明位图，防止边界色块缺失；两种输出共用相同的 XYZ→sRGB 与最大通道归一化公式。

SVG 背景采用 257 个横向颜色样本与 512–2048 条细扫描带，每条带用标准 SVG linearGradient 连续插值；RGB 各通道误差不超过一个 8 位色阶时省略中间色标，以控制体积。扫描带完全不透明，内部矩形用 crispEdges 防止拼接抗锯齿露出底层网格；边界由 geometricPrecision 的 clipPath path 单独裁剪。裁剪路径与光谱描边共用 `AppendSmoothPath` 的二次贝塞尔曲线，紫线用直线闭合，避免像素阶梯和轮廓不一致。纵向仍为细带近似，SVG 与逐像素预览不保证逐像素相同；不使用内嵌 PNG、SVG meshgradient 或额外渲染依赖。

`Core/AppResources.cs` 使用 ManifestResourceStream 加载两张底图与图标，资源逻辑名在 csproj 显式声明。Bitmap.Clone 避免按目标 DPI 再绘制导致的放大裁切；改文件名须同步 csproj 与加载名。

新版预览由 140ms WinForms Timer 合并重绘，在 UI 线程同步计算和渲染；没有后台 Task 渲染管线。`RenderNow` 使用控件尺寸（上限 2400×2400，不足时回退 1000×1000），调用 Exporter，再把位图克隆到 PictureBox.Zoom。旧预览 Bitmap 会 Dispose。

UI 图片导出前克隆 DiagramStyle 并关闭 HighlightSelection。Exporter 本身不会自动排除选中高亮，其他调用者需要自行设置。

光谱模型 `CieCurve.Visible` 默认 true，仅控制绘图可见性；`DiagramRenderer.DrawCurves` 和 `DrawLegend` 排除隐藏曲线，GDI 与 SVG 共用该判断。隐藏不移除模型，也不影响计算结果或 CSV/TXT 数据导出。光谱表格右键提供隐藏/显示与删除并标注快捷键；`Space` / `Delete` 由表格自身处理，支持多选，不占用全局 `Ctrl+H` 批量改名。可见性切换原地更新名称标记和文字颜色，保留选中行；行 Tag 保存曲线模型，隐藏和删除按模型引用操作。

WinForms 的菜单 `ShortcutKeys` 不接受裸 `Space`，隐藏/显示菜单用 `ShortcutKeyDisplayString` 标注表格快捷键，避免打开右键菜单时抛异常。`Program.Main` 在无控制台的 WinExe 进程中设置输出代码页失败时，回退为直接使用 UTF-8 的标准输出 StreamWriter，确保实际单文件自检的重定向中文日志可读。

## 箭头方向标注

`Core/ArrowModel.cs` 中的 ArrowEndpoint 持有 CieCurve 或 CiePoint 引用，CieArrow 保存参考端点、类型、颜色、线宽、曲率、弯曲方向、描边和相对偏移；不通过名称或表格行号关联。箭头在点列旁边表达起点 → 终点的变化方向，参考点确定初始方向和长度；默认白色、2.5 线宽、黑色细描边，初始沿参考方向的垂线偏移约绘图区短边的 7%。

`MainForm.Arrows.cs` 统一光谱计算点和独立坐标点候选项、管理窗口及删除清理。少于两个可用点时禁用添加，拒绝同一端点和重合坐标；改名保留关联，隐藏光谱时相关箭头暂时隐藏，恢复后重新显示。UpdateTables 根据对象成员关系清理已删除端点的箭头，ClearAll 同时清空箭头；箭头不写入偏好 JSON 或 CSV/TXT，经典入口不接入。

`UI/ArrowEditorDialog.cs` 按独立 96dpi 基准创建编辑窗口，支持直线/弯曲、颜色、线宽 1–12、曲率 0–100%、左右弯、描边开关与颜色；使用点列与箭头分开的样式示意。应用返回新模型，取消不改原对象，编辑保留已有位置。NumericUpDown 设最小尺寸，避免 AutoSize 表格布局缩成零宽；使用提示固定在滚动区外，按宽度和字体测量高度，小窗口只滚动设置和示意预览。

`MainForm.ArrowDragging.cs` 按 PictureBox.Zoom 把鼠标坐标转为当前图片像素，再将拖动位移保存为绘图区宽/高的 OffsetX / OffsetY；整条箭头平移，原点坐标不变，直线保持与参考方向平行。箭身与箭头三角形均可命中，数据标记的点击优先；鼠标捕获期间更新位置，按约 30ms 节流重绘，松开后完整重绘。根据几何边界限制常规箭头留在绘图区内；超出绘图区的部分最终由画布裁剪。

`Core/Rendering/ArrowGeometry.cs` 共用曲线子段、终点切线、箭头三角形、边界和命中几何；`ArrowRenderer.cs` 先从原端点换算图型坐标，再整体平移。IPlotCanvas 的 DrawBezier / PushClip / PopClip 分别由 GDI DrawBezier + GraphicsState 和 SVG path C + clipPath 实现。箭头绘制在数据标记之前，选中高亮只用于预览；Exporter 新增可选箭头列表参数，旧调用仍可省略，预览与 PNG/TIFF/SVG 使用同一位置与形状。方向标注不代表经过中间测量点的真实轨迹。

## 导出与检查边界

`MainForm.ExportImage` 在 PNG/TIFF/SVG 的保存调用成功返回后，统一弹出带完整保存位置的「导出成功」信息框，并保留原有状态栏记录。保存对话框取消时直接返回，异常分支显示错误后也立即返回，避免失败后继续显示成功；Exporter 和自检仍不包含交互提示。

| 输出 | 实现与限制 |
| --- | --- |
| PNG/TIFF | GDI 画布先按 96DPI 渲染，再设置目标 DPI 元数据；UI 固定 1400×1400，不按 DPI 放大像素 |
| SVG 矢量背景 | linearGradient、扫描带、平滑 clipPath、text、线和标记；不含 image 元素 |
| SVG 位图兼容分支 | 仅底层兼容/自检使用；SvgCanvas.DrawImage 内嵌 base64 PNG，新版 UI 不进入该分支 |
| CSV | UTF-8 BOM；曲线 XYZ/xy/u'v'/峰值/CCT 等，独立点以 Y=1 反推 XYZ；有限显示精度 |
| TXT | UTF-8 BOM；公式和结果，旧“1nm 采样求和”文字没有表达任意输入网格 |

导出字体缩放为 `clamp((width+height)/2000,1,2.4)`；预览同样经过 Exporter，不能仅凭旧注释断言最大化时字号完全固定。

GoldenTests 通过 Compile Link 包含 CieData、CieMath、SpectralInput、VbText；不加载 Form1 或主工程，不能验证记录表累加、选择状态与像素。SelfTest 保留 12 个数值条件及十个基础输出文件的最小大小，另由 ArrowSelfTest 检查 41 项箭头条件和四组 PNG/TIFF/SVG，共 22 个输出文件；覆盖位图实际绘制、绘图区边界、SVG 结构、TIFF 格式/尺寸/DPI、端点联动和平行偏移。两个 u'v' 背景参数组合仍回退矢量，不代表有 u'v' 位图资源；自检不包含完整界面自动化。

## 文档维护映射

- 用户可见行为更新 [README](../README.md)，架构、计算、渲染约束更新本文，环境、命令与验证结论更新 [开发指南](./DEVELOPMENT.md)。
- 原版调查及旧像素比对留在 [逆向工程记录](./REVERSE_ENGINEERING.md)，旧轮次与计划留在 [修订摘录](./CHANGELOG.md) 和 [合并计划](./MERGE_PLAN.md)。
- CodeGraph 用于定位调用和影响范围；生成关系仍需以实际方法体确认，例如事件委托、接口调用与源码链接不一定完整体现在图中。
