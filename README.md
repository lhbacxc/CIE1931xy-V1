# CIE1931xy 重建版

CIE1931xy 是 Windows 桌面色度分析工具，使用 C# / .NET 8 / WinForms 重建原版 1.6.0.2，并提供 2.0 多光谱工作台。它支持导入光谱、输入色坐标、绘制 CIE 1931 xy / CIE 1976 u'v' 色度图，以及导出图片和结果。

默认打开新版工作台；经典界面通过 `--classic` 或「视图 → 打开经典界面」进入。项目版本为 2.0.0，业务数据只保存在内存中，目前没有工程文件保存功能。

## 文档入口

- 使用软件：本文件。
- 接手开发：[项目入门指南](./doc/PROJECT_GUIDE.md)。
- 架构与计算边界：[架构说明](./doc/ARCHITECTURE.md)。
- 环境、命令与验证范围：[开发与验证指南](./doc/DEVELOPMENT.md)。
- 编码代理执行规则：[AGENTS.md](./AGENTS.md)。
- 历史资料：[修订摘录](./doc/CHANGELOG.md)、[合并计划与实施记录](./doc/MERGE_PLAN.md)、[逆向工程记录](./doc/REVERSE_ENGINEERING.md)。历史资料中的旧界面和验收数据不代表当前状态。

## 运行与构建

运行框架依赖版需要 Windows x64 和 **.NET 8 Desktop Runtime**；从源码构建使用 **.NET 10 SDK**，具体版本由 `./global.json` 约束，程序运行目标暂时仍为 .NET 8。

在项目根目录打开 PowerShell：

```powershell
$ErrorActionPreference = 'Stop'
$OutputEncoding = [Console]::InputEncoding = [Console]::OutputEncoding = New-Object System.Text.UTF8Encoding
$PSDefaultParameterValues['*:Encoding'] = 'UTF8'

.\dist\CIE1931xy.exe                          # 运行已有发布版（如该文件存在）
.\build.ps1                                 # 默认 all：构建 + 双基线 GoldenTests + SelfTest
.\build.ps1 -Target restore                 # 还原解决方案的编译依赖
.\build.ps1 -Target build                   # 只构建源码
.\build.ps1 -Target test                    # 构建测试程序并检查两套 oracle
.\build.ps1 -Target selftest                # 构建主程序并执行渲染/导出自检
.\build.ps1 -Target run                     # 构建并启动源码产物
.\build.ps1 -Target publish                 # 发布框架依赖单文件到 ./dist/
.\build.ps1 -Target publish -SelfContained  # 自包含发布，目标机无需另装运行时
```

脚本会优先寻找用户目录中的 SDK，找不到时可用 `-DotnetPath` 指定 SDK 的 `dotnet.exe`；它会另行寻找 .NET 8 运行时来执行测试和自检。`publish` 先跑回归和自检，再核验、删除并重建 `./dist/`，不要在该目录存放用户数据。`run` 直接启动刚构建的程序，不依赖旧发布版。

本机已补齐用户目录下的 .NET 10.0.404 SDK，并保留现有 .NET 8 Desktop Runtime。解决方案、编辑规范与 VS Code 构建/调试配置已补齐；实际源码构建和验证结果见 [开发指南](./doc/DEVELOPMENT.md)。

## 新版工作台

启动时没有示例数据，也没有「载入示例」按钮。左侧为三个页签：**光谱曲线 / 三刺激值 / 坐标点**，右侧为图形预览；颜色、描边、批量改名按钮位于页签上方。

### 导入与输入

| 入口 | 行为 |
| --- | --- |
| 导入光谱 / `Ctrl+O` | 多选 `.csv/.txt/.dat`，自动识别光谱或坐标文件 |
| 输入坐标 / `Ctrl+Shift+O` | 在多行输入框输入坐标文本 |
| 顶部「粘贴」 | 打开对话框，可编辑文本或读取剪贴板后解析 |
| `Ctrl+V` / `Ctrl+Shift+V` | 直接读取剪贴板文本并自动识别，和顶部对话框入口不同 |
| 拖放 / `--open` | 导入文件；启动参数一次指定一个文件 |

光谱支持逗号、分号、Tab、空格、竖线分隔及多条强度列；按波长排序，重复波长取平均，异常数据通过警告或错误反馈。单曲线文件以文件名命名，多曲线文件使用列标签。导入默认红色 `#D62728`、圆形、黑色描边。

坐标常见格式为 `x,y`、`名称,x,y`、`名称,x,y,强度`、`序号,名称,x,y`。解析器对数值列组合打分；检测到 u'/v' 表头时转换为 xy。额外数值列可能造成歧义，导入后应核对坐标。

文件读取支持 UTF-8 BOM、UTF-16 BOM、严格 UTF-8、GBK(936)，最后回退 Latin-1。项目源码和文档仍统一使用 UTF-8。

### 表格与选择

- 光谱曲线页显示名称、形状、颜色、描边；三刺激值页显示波长范围、点数、峰值波长和 X/Y/Z；坐标点页显示独立输入点的 xy、u'v'、CCT 和 Δuv。光谱曲线的完整派生结果可通过 CSV/TXT 查看。
- 双击名称列重命名；右键修改形状、颜色、标签或删除。支持 Ctrl/Shift 多选及 Delete 删除。
- 「颜色 / 描边」应用于选中数据，无选中项时应用于全部数据。批量改名针对曲线和坐标点，可使用 `{n}` 编号；查找留空表示统一改名。
- 点击画布中的点或空白处可选择或取消选择。选中高亮只用于预览，UI 图片导出时会关闭。

### 绘图与计算

| 设置 | 当前行为 |
| --- | --- |
| 图型 | xy / u'v'，`Ctrl+T` 切换 |
| 轴范围 | xy 固定 x=0–0.8、y=0–0.9；u'v' 两轴均 0–0.65，无自动/手动轴范围入口 |
| 背景 | xy 支持原版位图和矢量背景；u'v' 自动使用矢量背景，`Ctrl+B` 切换 |
| 黑体线 | 默认开启，`Ctrl+P` 切换；位图模式更换带/不带烘焙轨迹的底图，矢量模式实时计算 |
| 网格 | 默认开启；位图里的网格和波长文字已烘焙，不能单独隐藏 |
| 图例 / 波长标注 / 数据标签 | 默认关闭；矢量背景的波长标注可以切换，图例位于右下角 |
| 标题 | 默认不显示；xy 绘制 x+y=1 参考线，u'v' 不绘制该线 |

新版光谱计算默认逐点求和，对 CIE1931 配色函数线性插值，**不做 Y=100 归一化，也不执行经典算法的四位小数取整**。经典界面保留原版 1nm/5nm 解析、求和和取整语义；两条链路不能混为一谈。

CCT 在 CIE1960 uv 中寻找黑体轨迹最近点。界面字段 Δuv 当前为非负距离，超过 0.05 时标记为仅供参考；它不是带符号的标准 Duv。标准光源选择和积分口径切换尚未提供界面入口。

### 导出

| 格式 | 内容 |
| --- | --- |
| PNG / TIFF | UI 固定 1400×1400 px，DPI 默认 300，可选 72–1200；DPI 只改变元数据，不增加像素 |
| SVG | 文字、线条和标记为矢量；矢量背景使用裁剪色块、不含位图；位图背景内嵌原版 PNG |
| CSV | UTF-8 BOM，每条曲线和独立坐标点一行，含色度、峰值、CCT 等字段 |
| TXT | UTF-8 BOM，计算结果与公式说明；其中「1nm 采样」文字不能替代实际输入步长 |

不提供 PDF 导出。导出文件名在当前进程内记住；不会跨重启保存。坐标点 CSV 中的 XYZ 由 xy 在 Y=1 假设下反推，并非测得的三刺激值。

## 经典界面与命令行

```powershell
$ErrorActionPreference = 'Stop'
$OutputEncoding = [Console]::InputEncoding = [Console]::OutputEncoding = New-Object System.Text.UTF8Encoding
$PSDefaultParameterValues['*:Encoding'] = 'UTF8'

.\dist\CIE1931xy.exe --classic
.\dist\CIE1931xy.exe --classic --no-blackbody
.\dist\CIE1931xy.exe --classic --planckian
.\dist\CIE1931xy.exe --open .\tests\oracle\case_bb_5nm_380_780.txt
.\dist\CIE1931xy.exe --selftest .\.codegraph\validation\selftest
```

经典界面先输入「波长、强度」两列，选择 1nm 或 5nm 间隔，再 Calculate；Draw Point 描点，Clr Input / Clr Rec / Clr Point 清理对应内容，Exp Graph 导图，Chinese 切换语言，双击 Data Input 标签从 TXT 导入。

经典模式 `Ctrl+B` 更换底图，`Ctrl+P` 切换计算黑体叠加层。`--blackbody` / `--no-blackbody` 选择底图，`--planckian` 开启计算叠加层；旧 README 提到的 `--no-planckian` 没有实现。`--selftest` 不打开主窗口，省略目录时写入系统临时目录。

经典模式按 DpiUnaware 运行，新版按 PerMonitorV2 运行。从工作台打开经典界面优先启动独立进程，两者 DPI 策略分别维护。

## 原版关系与声明

原版 `CIE1931xy.V.1.6.0.2.exe` 与 `CIE1931xy.V.1.6.0.2a(2).exe` 的计算逻辑一致，主要差别是 684×732 底图是否烘焙黑体轨迹。历史 oracle 和像素比对记录见 [逆向工程记录](./doc/REVERSE_ENGINEERING.md)，本项目没有包含原始反射采集器、像素比对脚本或完整外部参考工程。

原程序作者为 nicklxg；原程序版权归原作者。本工程用于学习研究与个人使用。原版署名、逆向依据与历史工具链保留在逆向工程记录中。
