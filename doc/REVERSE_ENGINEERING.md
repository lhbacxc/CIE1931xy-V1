# 逆向工程记录：CIE1931xy V.1.6.0.2 / V.1.6.0.2a(2)

> 历史证据记录：本文的原版识别、像素比对、工具链和合并结果为原任务快照。当前结构与使用方式见 [入门指南](./PROJECT_GUIDE.md) 和 [README](../README.md)，本次环境与验证范围见 [开发指南](./DEVELOPMENT.md)。
> 本项目保留源码、oracle JSON/输入和历史截图，未包含本文复现所需的外部反编译工作区、原始 oracle 采集器、drivetest.ps1 或完整原版参考文件。旧像素差异数据未在 2026-11-19 重新复验。

目标：把两个 Windows 小工具（`参考\CIE1931xy.V.1.6.0.2.exe`、`参考\CIE1931xy.V.1.6.0.2a(2).exe`）
逆到"能读、能编译、能改"的源码级，并确认重建版与原版行为一致。

结论先行：

1. 两个 exe 都是 **.NET Framework 2.0 的 VB.NET 托管程序**（纯 IL，无原生代码），可以直接反编译回近似原始源码。
2. 两者的**代码完全相同**，唯一差别是内嵌的**背景位图资源**（黑体轨迹是画在位图里的，不是程序画的）。
3. 重建版（`src\CIE1931xy.Rebuilt`）与原版在 **数值（132 项）**、**文本（77 项）**、**界面像素（图表区差异 0）** 上一致。

---

## 1. 目标识别（先确认它是什么，再决定怎么逆）

### 1.1 PE 头（两文件相同结构）

| 项 | 值 |
|---|---|
| DOS/PE 签名 | `MZ`，PE 头偏移 `0x3C` → `0x80` |
| Machine | `0x014C` → x86 32 位 |
| Optional header | `0x10B`（PE32） |
| Subsystem | `2`（Windows GUI） |
| Sections | 4 个：`.text` / `.sdata` / `.rsrc` / `.reloc` |
| Import Directory | RVA `0x7E948`，大小 83 → **只导入 `mscoree.dll` 的 `_CorExeMain`** |
| **CLR Directory** | RVA `0x2008`，大小 72 → **有托管头** |
| Resource Directory | RVA `0x82000`，大小 27248（图标/版本信息） |
| Debug Directory | RVA `0x80000` |

`_CorExeMain` 是 .NET 程序的入口桩：操作系统把执行权交给 CLR，由 CLR 找托管入口点。
ASCII 扫描里没有 `KERNEL32` / `USER32` / `VCRUNTIME` / `PyInstaller` / `Delphi` 的痕迹，
说明**没有任何原生代码**，排除了 C/C++、Delphi、打包器（PyInstaller/Electron 等）的可能。

### 1.2 CLI 头与元数据

| 项 | `V.1.6.0.2.exe` | 说明 |
|---|---|---|
| CLI header 文件偏移 | `0x408` | |
| cb / RuntimeVersion | `0x48` / `v2.0.50727` | .NET Framework 2.0/3.5 运行时 |
| Flags | `1` = `COMIMAGE_FLAGS_ILONLY` | 纯 IL，无混合模式程序集 |
| EntryPointToken | `0x06000002` | MethodDef 表第 2 项 = `Main` |
| Metadata | RVA `0x7E90C`，24636 B | |
| Managed Resources | RVA `0x2050`，421931 B | 两张位图 + 图标 |

程序集信息（REA 静态检查）：

```
AssemblyName       CIE1931xy.V.1.6.0.2
Version            1.6.0.2
MVID               9f4db688-e931-45ee-8701-ce7e0d1804bc
References         mscorlib 2.0.0.0
                   Microsoft.VisualBasic 8.0.0.0
                   System.Windows.Forms 2.0.0.0
                   System 2.0.0.0
                   System.Drawing 2.0.0.0
TypeDef            11        MethodDef   134      Field  98
MemberRef          222       Property    53       ManifestResource 2
```

**`Microsoft.VisualBasic` 的引用 = VB.NET 写的**（不是 C#），这解释了后面反编译代码里
`Strings.Len/Mid/Asc`、`Conversions.ToDouble`、`Conversion.Int/Fix`、`Utils.CopyArray`、
`ProjectData.SetProjectError` 这些只有 VB 才会生成的调用。

### 1.3 为什么没用 Ghidra/IDA

REA 的 native provider 对这两个文件全部拒绝，返回 `provider_unavailable`：

- `ghidra: managed_target_unsupported` —— "Windows Ghidra P0 接受原生 PE 程序与 DLL；托管或未分类 PE 不支持"
- `hopper: unsupported_host` —— Hopper 只有 macOS
- `ida: not_configured` —— 本机未装

这不是故障，而是正确的判断：**托管程序应该走托管逆向路线**（元数据 + IL 反编译），
硬啃 native 反汇编只会看到 CLR 的 jit stub。REA 提供的 `inspect-managed-artifact` /
`inspect-managed-members` / `compare-managed-members` 正好覆盖这条路。

---

## 2. 工具链

| 用途 | 工具 | 说明 |
|---|---|---|
| 托管程序集静态检查 | `npx -y rea-agents@6.5.0 inspect-managed-artifact <exe>` | PE/CLI 身份、程序集引用、表计数、资源布局（不执行代码） |
| 成员清单 | `rea inspect-managed-members` | 类型/方法/签名/`ldstr` 字符串字面量 |
| 版本对比 | `rea compare-managed-members <a> <b>` | 按精确类型/方法/签名比较 |
| IL → C# | **ILSpy `ilspycmd` 11.1.0.9782** | `ilspycmd -p -o <dir> <exe>`（工程模式输出，含 .csproj 与资源） |
| 编译重建工程 | **.NET SDK 8.0.426** | `dotnet build`（net8.0-windows） |
| 编译 oracle | `.NET Framework 4.0.30319 csc.exe` | 反射加载原版 exe 需要 .NET Framework；源码是 C# 5 兼容写法 |
| 界面驱动/截图 | 自写 `drivetest.ps1` | `EnumChildWindows` + `WM_SETTEXT` + `BM_CLICK` + `PrintWindow` + 逐像素 diff |
| 资源提取 | PowerShell 反射 + `ResourceReader` | 读出 `CIE1931xy.Resources.resources` 里的位图/图标 |

---

## 3. 反编译与源码重建

`ilspycmd -p -o decomp1602 CIE1931xy.V.1.6.0.2.exe` 输出（含资源）：

```
app.ico  CIE1931xy.Form1.resx  CIE1931xy.Resources.resx  CIE1931xy.V.1.6.0.2.csproj
CIE1931xy\Form1.cs (2881 行)  CIE1931xy.My\*.cs  CIE1931xy.My.Resources\Resources.cs
Properties\AssemblyInfo.cs
```

`Form1.cs` 的结构（行号为 1602 版本的源码）：

| 行 | 内容 |
|---|---|
| 1–915 | 字段声明 + `[AccessedThroughProperty]` 属性（VB 设计器生成的控件包装） |
| 916–1546 | **构造函数**：配色函数表 `cie1931xyz[471,4]`（923–1396）、色坐标表 `ccxy1931[81,3]`（1397–1489）、中英文界面字符串表、`O = New Point(59, 12)`（1484–1485）、`Canvas/Bgimage` 克隆底图 |
| 1547–1562 | `Dispose(bool)` |
| 1563–2014 | `InitializeComponent()`：40 个控件、两个对话框、DataGridView 五列 |
| 2015–2881 | 事件处理：描绘坐标、窗体加载、导出图片、清空、**计算（2236–2645）**、表格点击、帮助、中英切换、导入文件 |

界面字符串全部放在构造函数的表里（`LabelText[5,4]`、`ButtonText[10,4]`、`MessageTitleText[6,4]`、
`MessageInfoText`、`MessageExclamText`、`MessageErrorText`、`HelpText[4]`），
列 2 = 英文、列 3 = 中文，运行时按 `LangFlag`（2=英文，3=中文，**启动默认英文**）拷到列 1 再用。

重建时这些表逐字保留在 `Core\CieData.cs`（连"Designed by nicklxg 2009 / E-mail: nicklxg@163.com /
Version: 1.6.0.2"的帮助文本都在）。

### 3.1 资源

`CIE1931xy.Resources.resources` 里有两个托管资源：

| 资源名 | 类型 | 内容 |
|---|---|---|
| `CIE1931xy` | `Bitmap` | 684×732 色品图底图（版本间不同，见下） |
| `CIE1931xy1` | `Icon` | 32×32 图标 |

提取方式（PowerShell 5.1）：

```powershell
Add-Type -AssemblyName System.Drawing, System.Windows.Forms
$asm = [Reflection.Assembly]::LoadFile($exe)
$s = $asm.GetManifestResourceStream('CIE1931xy.Resources.resources')
$r = New-Object System.Resources.ResourceReader($s)
foreach ($e in $r) { "$($e.Key) = $($e.Value.GetType().Name)" }
```

---

## 4. 两个版本到底差在哪（关键结论）

### 4.1 成员级对比先给了个"假警报"

`rea compare-managed-members` 的输出：unchanged 48 / changed 32 / **added 148 / removed 152**。
看上去像"功能变化很大"，但把 `Form1.cs` 两份源码 diff 之后（`form1.diff`，806 行）发现：

- `V.1.6.0.2.exe` 是 **Debug 编译**：构造函数里有 `__ENCList`（编辑并继续用的 WeakReference 列表）、
  `(disposing && components != null) ? true : false` 这种 Debug 代码生成、`while` 循环 + `num2/num3` 临时变量；
- `V.1.6.0.2a(2).exe` 是 **Release 编译**（且用的 VB 编译器版本更新）：`for` 循环、`if (disposing && components != null)`、
  `InitializeComponent` 里控件初始化顺序略有不同。

**没有任何方法缺失、没有分支被删、没有公式改变。** 差异全部是编译器产物。

### 4.2 真正的差异：背景位图

把两个 exe 的 `CIE1931xy` 位图都导出来对比：

| | `V.1.6.0.2` | `V.1.6.0.2a(2)` |
|---|---|---|
| 尺寸 | 684×732 | 684×732 |
| 像素格式 | `Format24bppRgb` | `Format32bppArgb`（采样点全部不透明） |
| 内容 | 色品图 + 网格 + 波长数字 + **黑体轨迹 `T_c(K)`**、1500/2000/2500/3000/4000/6000/10000 K 等温线刻度、`∞` 分支、右上角小插图 | 同样的色品图/网格/波长数字，**没有黑体轨迹** |
| 差异像素 | 234 570 / 500 688（46.8%） | |

见 `doc\screens\00-artwork-diff.png`（左=带黑体，右=无黑体）。

由此得到两个结论：

1. "黑体辐射线"在原版里 **不是程序画的，是位图的一部分**，所以在原版上不可能"关掉这一层"或"改色温范围"。
2. 若想让这个功能真正可改，必须在代码里重新实现一遍 —— 即重建版新增的 `PlanckianLocus` + `PlanckianOverlay`
   （按普朗克定律 `M(λ,T)=C1/(λ⁵(e^{C2/λT}−1))` 对 CIE 表积分求 xy，再画轨迹和等温线）。

> 量化验证：把我们算出的轨迹与**原版位图里烘焙的那条曲线**逐列比较（238 个采样），
> 中位偏差 0.000342 xy（≈0.26 px），p90 0.0007，最大 0.0018 —— 说明公式与色温范围都还原对了。
> 位图那条曲线实际覆盖约 1138–14342 K，超过 15000 K 后偏离物理（终点 (0.227,0.231) vs 真值 (0.2399,0.2340)），
> 所以重建版的绘制范围定在 1000–25000 K。

---

## 5. 计算逻辑还原（`btnCalc_Click` 的算法链）

1. **预处理**：`TSV_*` 归零；`\t`→空格、`"  "`→`" "`、`,`→空格；剥掉末尾所有 `"\r\n"` 对。
2. **空输入**：弹 Info1（"Please input the Spectra Power Distribution Data…"）。
3. **跳过表头**：首字符不是数字时，找第一个 `\n` 且其后是数字，从那里截断。
4. **字符白名单**：`0-9` `.` 空格 `\t` `\n` `\r` `E` `e`；否则弹"非法字符 + ASCII 码"对话框。
5. **逐行解析**：以空格分列、`"\r\n"` 分行，存入 `pdata[n,2]`（波长、强度）。
6. **找峰值**：`Data_Row = 行数−1`；峰值波长 = 第一个"强度最大"的行（`Math.Round` 取整）。
7. **匹配积分区间**：先按 5nm 或 1nm 检查相邻间隔是否一致（不一致弹 Error1）；
   再在 360–830 nm 的配色函数表里找到起始偏移 `Offset`，然后按间距把 `pdata` 与 `cie1931xyz` 对齐累加，
   得 `TSV_X / TSV_Y / TSV_Z`。
   （数据从 360 nm 起 / 从更高波长起，两种情形的循环边界不同，见 `CieMath.FindOffset*`。）
8. **色坐标**：`x=X/(X+Y+Z)`、`y=Y/(X+Y+Z)`。
9. **UCS1976**：`u'=4X/(X+15Y+3Z)`、`v'=9Y/(X+15Y+3Z)`。
10. **取整（原版的小技巧）**：
    ```vb
    If Conversion.Int(v * 100000) Mod 10 >= 5 Then v = Conversion.Fix(v*10000 + 1)/10000
    Else v = Conversion.Fix(v*10000)/10000
    ```
    即"看第 5 位小数是否 ≥5，再截断到 4 位" —— 用 `Int`(向下取整) + `Fix`(向零截断) 实现四舍五入。
11. **RGB**（MacAdam 由 xy 推 RGB 的经验公式，见 `CieMath.Calculate`）。
12. **记录**：结果写进文本框、`DataGridViewRec` 行、以及六个 `str*` 数组（供点击某行回显）。

### 5.1 VB 语义陷阱（重建时必须一一对应）

| 原版 VB | 重建版实现 | 注意 |
|---|---|---|
| `Strings.Len/Mid/Asc` | `VbText.Len/Mid/Asc` | 1-based；`Asc("")` 抛异常；`Asc` 返回的是 UTF-16 码元 |
| `Conversions.ToDouble` | `VbText.ToDouble` | 当前区域格式；失败抛 `FormatException` |
| `Conversion.Int` | `Math.Floor` | 向下取整（负数也向下） |
| `Conversion.Fix` | `Math.Truncate` | 向零截断 |
| `Operators.CompareString(a,b,false)` | `string.CompareOrdinal` | 二进制比较，不受区域影响 |
| `Utils.CopyArray` | `VbText.Grow` | 复制并保留旧值 |
| 数字转字符串 | `VbText.FormatDouble` = `G15` | **.NET 2.0 按 15 位有效数字显示，.NET 8 默认最短往返**，不改就和原版文本框显示不一样 |

原版还有几个"坑"，重建时**保留语义但不再崩**：

- 剥尾部 CRLF 的循环用陈旧的 `num` 去 `Mid`，遇到单独 `\n` 会抛异常；
- `Conversions.ToDouble` 没有 try/catch，非法数字会让程序弹未捕获异常框；
- 行解析里"既没空格也没 CRLF"的分支会 `continue` 死循环。

---

## 6. 验证体系（怎么证明"一致"）

### 6.1 反射 oracle：直接问原版程序要答案

`tests` 之外的 `oracle.exe`（.NET Framework 4.0 编译）用 `Assembly.LoadFile` 加载**原版 exe**，
`Activator` 出 `CIE1931xy.Form1`，调用 `Form1_Load`、把输入塞进私有字段 `_txtBoxDataInput`、
设置 `_RadioBtn1nm/_RadioBtn5nm`、反射调用私有 `btnCalc_Click`，然后把所有私有字段、
7 个结果文本框的字符串、`DataGridViewRec` 的每个单元格导出成 JSON。

结果：9 个用例 / 11 个步骤（纯单色、黑体谱、5nm/1nm、带表头、尾随换行、连续多次计算…），
两个版本各跑一遍，**两版本之间 132 项数值、77 项文本、全部单元格完全相同**（这本身就是"两版功能一致"的铁证）。

重建版的 `tests\GoldenTests` 用同一批输入跑自己的 `CieMath`，与 oracle JSON 逐字段比对
（数值用 `==` 精确比较，文本用 `G15` 比较）→ **9/9 PASS**，每次 `build.ps1` 自动执行。

### 6.2 像素级：Win32 消息驱动 + 截图比对

`drivetest.ps1` 无视窗焦点地驱动程序：

1. `EnumChildWindows` 找输入框与两个按钮；
2. `SendMessage(WM_SETTEXT)` 写入 `555 1.0`，`SendMessage(WM_GETTEXT)` 回读（**跨进程必须用 SendMessage，
   用 `GetWindowText` 读别人家的子控件会得到空串**）；
3. `PostMessage(BM_CLICK)` 触发"计算"和"描绘坐标"；
4. `PrintWindow` 抓图表区与整窗，统计 `LightPink` 描点质心、逐像素 diff。

| 指标 | 原版 1.6.0.2 | 重建版 |
|---|---|---|
| 输入框回读 | `555 1.0` | `555 1.0` |
| 结果 | `0.3374 / 0.6588 / 0.5120501 / 1 / 0.005749999 / 0.1319 / 0.5795` | 完全相同 |
| 图表区尺寸 | 682×730 | 682×730 |
| 描点质心 / 粉色像素数 | (313.09, 194.09) / 44 | (313.09, 194.09) / 44 |
| 图表区差异像素 | — | **0 / 497 860（0.000%）** |
| 整窗差异像素 | — | 19 047 / 759 808（2.507%，字体抗锯齿） |

### 6.3 踩过的两个坑（对后续维护有用）

- **绘图原点**：反编译代码里 `O = New Point(59, 12)`（构造函数），描点公式是
  `px = O.X + x/pixelperxy`、`py = O.Y + Axes_Height − y/pixelperxy`（`Axes_Height=675`、`pixelperxy=0.8/600`）。
  我第一版漏抄了这一行，点就整体偏了 59/12 px；从位图上量出来是 (60, 12)——正好差 1 px，
  双向印证了这个常量。
- **DPI/字体自动缩放**：.NET 8 用 `SetDefaultFont(Microsoft Sans Serif 8.25)` 时实测字高 13 而不是设计器假设的 12，
  `AutoScaleMode.Font` 会触发**只纵向 1.083 倍**的缩放（图表区 793 高、窗体 804 高）。
  修法：`Application.SetHighDpiMode(DpiUnaware)` + `AutoScaleMode.None`，布局就回到原版的 1:1。

---

## 7. 复现步骤

```powershell
# 1) 识别（只需 rea-agents，不需要 ghidra/ida）
npx -y rea-agents@6.5.0 inspect-managed-artifact '参考\CIE1931xy.V.1.6.0.2.exe'

# 2) 反编译
dotnet tool install --global ilspycmd            # 或下载 nupkg 解压
ilspycmd -p -o rea-work\decomp1602 '参考\CIE1931xy.V.1.6.0.2.exe'

# 3) 对比两版
npx -y rea-agents@6.5.0 compare-managed-members '参考\CIE1931xy.V.1.6.0.2.exe' '参考\CIE1931xy.V.1.6.0.2a(2).exe'

# 4) 构建 + 回归
.\build.ps1

# 5) 界面像素比对（需要先跑过原版，得一张基线图）
powershell -File drivetest.ps1 -Exe '参考\CIE1931xy.V.1.6.0.2.exe' -Lang en -OutPng orig.png
powershell -File drivetest.ps1 -Exe 'dist\CIE1931xy.exe' -Lang en -OutPng rebuild.png -ComparePng orig.png
```

---

## 8. 已知局限

- `ccxy1931[81,3]`（380–780 nm 每 5 nm 的色坐标）在原版里赋值但**从未使用**，重建版照抄保留（存在 `CieData`），
  不参与任何计算。
- 原版 `DataGridViewRec` 实际有 11 行（第 11 行是 DataGridView 默认的"新增行"），重建版行为一致。
- 图表区像素完全一致，但文字（按钮/标签/帮助）在整窗比对里有 ~2.5% 像素差异，
  原因是 .NET 2.0 GDI 与 .NET 8 GDI+ 的字形光栅化不同，无法（也不值得）逐像素复刻。
- 原版没有任何原生代码/加壳/混淆，所以"逆向"到这里已经到底：没有隐藏功能、没有联网、没有校验。

---

## 9. 合并记录（v2.0.0，2026-10-23）

逆向结论落地之后，按用户要求在**不改动 CIEdraw 项目**的前提下，把 CIEdraw 的能力合并进重建版：

- **保留的逆向成果**：`Form1` + `Form1.Designer.cs` + `Core/CieData.cs` + `Core/CieMath.cs` + `Core/SpectralInput.cs`
  一行未改（9 个 oracle 用例仍 9/9 PASS，UI 像素验收仍 0 差异）；用 `--classic` 或菜单「视图 → 打开经典界面」进入。
- **新增（按 CIEdraw 语义重写为 C#）**：
  | 能力 | 新文件 | 来源 |
  |---|---|---|
  | 光谱文件解析（分隔符/波长列识别/表头/重复波长/异常值） | `Core\SpectrumCsvParser.cs` | `光谱数据画CIE.py` 的 `parse_spectral_text` L691–856、`validate_spectral_curve` L859–931 |
  | 坐标文件解析（名称列、u'v' 自动换算） | `Core\CoordinateParser.cs` | `parse_coordinate_text` L934–1019 |
  | 色度学计算（CMF 插值、xy/u'v'/uv1960 互转、CCT、真彩映射） | `Core\Colorimetry.cs` | `spectrum_to_xyz` L1078、`xy_to_uv` L1113、`cct_from_xy` L1228、`cie_background_image` L1311 |
  | 矢量/位图双画布（GDI+ 与 SVG 同一套绘制接口） | `Core\Rendering\PlotCanvas.cs`、`GdiCanvas.cs`、`SvgCanvas.cs`、`PlotSpace.cs`、`DiagramRenderer.cs`、`ChromaBackground.cs` | 新写（CIEdraw 用 matplotlib，无法复用） |
  | 导出 PNG/TIFF(DPI)/SVG/CSV(BOM)/TXT | `Core\Export\Exporter.cs` | 新写 |
  | 新工作台界面（4 页签表格 + 显示设置 + 画布 + 菜单/状态栏） | `MainForm.cs` | 新写（布局参考 CIEdraw，样式重做） |
- **口径选择**：默认沿用**原版 1.6.0.2 的逐点求和 + 4 位小数取整**（保证与原程序逐位一致）；
  `Colorimetry.SpectrumToXyzTrapezoid(...)` 保留 CIEdraw 的"梯形积分 + Y=100 归一"作为可选口径，需要时在 `Colorimetry` 里切换。
- **验证**：`--selftest`（12 项数值断言 + 4 种图型/背景组合的 PNG/SVG + CSV/TXT）与 `build.ps1 -Target test`（9 个 oracle 用例）全绿。
