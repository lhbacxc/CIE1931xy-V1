# 开发、构建与验证

命令均在项目根目录的 PowerShell 执行，文件路径相对项目根目录。架构见 [架构说明](./ARCHITECTURE.md)，首次接手见 [入门指南](./PROJECT_GUIDE.md)，本轮范围见 [开发环境补齐方案](./DEV_SETUP_PLAN.md)。

## 已确认的环境

- 主工程仍为 net8.0-windows / WinForms，GoldenTests 为 net8.0；本轮没有升级运行目标或更改计算算法。
- 编译 SDK：.NET 10.0.404，官方 Windows x64 ZIP 安装到当前用户 LocalAppData 下的 `./Microsoft/dotnet/`，安装前校验了 Microsoft 元数据中的 SHA512。
- `./global.json` 设置 version=10.0.404、rollForward=latestFeature、allowPrerelease=false；允许 .NET 10 同大版本内较新 feature band，不自动使用 .NET 11 或预览版。
- 本机机器级安装保留 .NETCore / WindowsDesktop Runtime 8.0.31；用户目录 SDK 带有 .NET 10 运行时。脚本分别寻找 SDK 和 .NET 8 运行时，避免拿仅有 .NET 10 的宿主运行 .NET 8 DLL。
- SDK 目录已加入用户 PATH，但旧进程仍继承旧环境，机器级 PATH 也可能优先命中仅有运行时的 dotnet。项目命令优先使用 build.ps1，它不依赖 PATH 的先后顺序。
- 本机无需另装完整 Visual Studio、C++ 工作负载或 Python；两个 csproj 没有第三方 PackageReference。首次还原会下载框架参考包，网络源为 NuGet。
- 按 [Microsoft 支持策略](https://dotnet.microsoft.com/platform/support/policy/dotnet-core)，.NET 8 已于 2026-11-10 结束支持。后续持续维护应单独评估 net10.0-windows 升级并跑兼容性检查；更新 SDK 本身不会改变程序目标。

查看实际 SDK 和机器级运行时：

```powershell
$ErrorActionPreference = 'Stop'
$OutputEncoding = [Console]::InputEncoding = [Console]::OutputEncoding = New-Object System.Text.UTF8Encoding
$PSDefaultParameterValues['*:Encoding'] = 'UTF8'

$sdkExe = Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet\dotnet.exe'
$runtimeExe = Join-Path $env:ProgramFiles 'dotnet\dotnet.exe'
& $sdkExe --info
& $runtimeExe --list-runtimes
```

## 开发文件

| 文件 | 职责 |
| --- | --- |
| `./CIE1931xy.sln` | 包含主程序与 GoldenTests，供 IDE 打开或整体 restore/clean |
| `./global.json` | SDK 版本选择与滚动规则 |
| `./.editorconfig` | UTF-8、缩进、行尾空白和末尾换行，不强制重排现有代码 |
| `./build.ps1` | 编译、双基线测试、自检、运行、发布与运行时选择 |
| `./.vscode/tasks.json` | Debug 构建、Release 全检查、双基线测试、自检任务 |
| `./.vscode/launch.json` | 新版/经典 Debug 调试入口，启动 apphost.exe |
| `./.vscode/extensions.json` | C#、C# Dev Kit、PowerShell 扩展推荐，不自动安装编辑器 |

本地 Git 仓库使用 main，origin 指向公开仓库 [lhbacxc/CIE1931xy-V1](https://github.com/lhbacxc/CIE1931xy-V1)。没有独立测试框架、覆盖率或 CI 配置。本项目的自定义回归不是 dotnet test，不生成伪 Python requirements.txt；后续 Python 辅助程序遵守 AGENTS.md 的环境确认规则。

## 日常命令

```powershell
$ErrorActionPreference = 'Stop'
$OutputEncoding = [Console]::InputEncoding = [Console]::OutputEncoding = New-Object System.Text.UTF8Encoding
$PSDefaultParameterValues['*:Encoding'] = 'UTF8'

.\build.ps1                                  # Release 构建 + 两套 oracle + SelfTest
.\build.ps1 -Target run                      # 构建后启动当前源码产物
.\build.ps1 -Target build -Configuration Debug
.\build.ps1 -Target test                     # 构建测试工程，逐套检查两个 oracle
.\build.ps1 -Target selftest                 # 构建主程序，再自检
.\build.ps1 -Target restore                  # 还原解决方案编译依赖
.\build.ps1 -Target clean                    # 清理对应 Configuration 的构建产物
```

| Target | 当前行为 |
| --- | --- |
| all（默认） | 主程序构建 + 测试工程构建/双基线回归 + 源码自检，不发布 |
| build | 构建主工程 |
| test | 构建 GoldenTests，再用 .NET 8 宿主分别运行两套 oracle |
| selftest | 构建主工程，用 .NET 8 Desktop 宿主执行 --selftest |
| run | 构建后启动对应 Configuration 下的 apphost.exe，不读取 dist |
| restore / clean | 对 CIE1931xy.sln 执行对应命令 |
| publish | 先构建、双基线回归、自检，再核验并更新 dist，保留配置和用户文件 |

默认 Configuration=Release、发布 Runtime=win-x64。`-DotnetPath` 可以覆盖 SDK 查找，候选安装仍必须满足 global.json；脚本自动把工作目录切到根目录，结束后恢复调用者位置。

构建产物为 `./src/CIE1931xy.Rebuilt/bin/Release/net8.0-windows/CIE1931xy.exe` 与 DLL；Debug 对应 Debug 目录，PDB 用于断点调试。经典窗口从同一程序加 `--classic` 打开。

## 编辑器调试

在 Visual Studio 中打开解决方案；需要设计器时选择“.NET 桌面开发”工作负载。当前 MainForm 主要手工创建控件，经典 Form1 有 Designer 文件，改经典布局时仍须遵守像素和 DPI 兼容约束。

在 VS Code 中打开项目目录，按扩展推荐安装 C# / C# Dev Kit。Ctrl+Shift+B 执行 Debug 构建，F5 可选择新版或经典入口；调试前自动执行同名构建任务。

任务使用系统自带 powershell.exe、-NoProfile 和单次进程的 ExecutionPolicy Bypass，不修改全局执行策略；build.ps1 使用 UTF-8 BOM，兼容 Windows PowerShell 5.1，同时也支持 pwsh。调试配置启动 exe，避免用用户目录的 .NET 10 宿主直接加载 net8.0 DLL。

本轮实际验证了配置的 PowerShell 命令和 Debug 产物，未安装/操作 VS Code 或 Visual Studio；编辑器内断点与设计器交互仍需在安装对应编辑器后确认。

## 经典 GoldenTests

测试工程通过 Compile Link 引入 CieData、CieMath、SpectralInput、VbText，固定 zh-CN 区域，比较 Data_Row、计算字段及七个格式化结果文本；数值精确相等，文本 ordinal 比较。

两套 oracle 各 9 个用例、11 个步骤。测试不创建 Form1、不验证记录表累计状态或像素；JSON 是历史 oracle，不因重建版结果变化而重新生成期望值。

旧脚本把两个 --oracle 传给同一次调用，后一个覆盖前一个。本轮改为逐套调用，`-Target test` 和默认 all 均覆盖两套；不需要手工绕过脚本补测。

只有运行时且已有测试 DLL 时，仍可直接核对已构建产物；注意此方法不重新编译：

```powershell
$ErrorActionPreference = 'Stop'
$OutputEncoding = [Console]::InputEncoding = [Console]::OutputEncoding = New-Object System.Text.UTF8Encoding
$PSDefaultParameterValues['*:Encoding'] = 'UTF8'

$runtimeExe = Join-Path $env:ProgramFiles 'dotnet\dotnet.exe'
foreach ($oracleFile in @('.\tests\oracle\oracle_results_with_blackbody.json', '.\tests\oracle\oracle_results_without_blackbody.json')) {
    & $runtimeExe .\tests\GoldenTests\bin\Release\net8.0\GoldenTests.dll --oracle $oracleFile
    if ($LASTEXITCODE -ne 0) { throw ('回归失败：' + $oracleFile) }
}
```

GoldenTests 控制台日志使用 UTF-8；`--report` 输出 UTF-8 Markdown，oracle 路径相对调用工作目录。发布报告前仍须确认没有敏感输入或不应公开的数据。

## 新版自检与输出

`-Target selftest` 构建后执行当前 DLL，默认写入 `./.codegraph/validation/selftest/`。可用 `-SelfTestDir` 指定其他目录，已存在的同名自检文件会被覆盖。子进程无窗口，脚本同时检查退出码和 SELFTEST OK，并保留 UTF-8 stdout/stderr 日志。

自检保留 12 个数值检查，以及四组 PNG/SVG、CSV/TXT 的最小文件大小检查，基础十个输出文件；ArrowSelfTest 增加 41 项箭头条件检查和 xy/u'v' × 直线/弯曲的四组 PNG/TIFF/SVG，共 22 个输出文件。基础自检图片为 900×900、PNG 144DPI，箭头 PNG/TIFF 为 900×900、300DPI；UI 导出为 1400×1400、默认 300DPI。

四组命名为 xy-bitmap、xy-vector、uv-bitmap、uv-vector；u'v' 不支持位图，后两组实际都走矢量。xy-bitmap SVG 内嵌一个 PNG image，其他三份 SVG 为矢量色块并含 clipPath。

箭头自检包含 TIFF 格式/尺寸/DPI、SVG 箭头结构与颜色、实际位图变化及绘图区边界、端点隐藏/恢复/删除、命中、平行偏移与导出高亮排除；仍不验证完整 UI 流程或多显示器 DPI。它依赖 Windows / System.Drawing；免开主窗口不等于可以在 Linux 运行。渲染改动后按受影响的格式补充视觉和尺寸/DPI检查。

Exporter 本身不会自动关闭 HighlightSelection；UI 导出入口会克隆样式并关闭，其他调用者须自行设置。

## 发布与清理

发布脚本校验 dist 的实际绝对路径位于项目根目录，并拒绝联接点/符号链接；不再递归清空目录，只让 dotnet publish 更新自身产物，保留自动生成的配置和其他用户文件。不要手动把整个 dist 作为缓存删除。

```powershell
$ErrorActionPreference = 'Stop'
$OutputEncoding = [Console]::InputEncoding = [Console]::OutputEncoding = New-Object System.Text.UTF8Encoding
$PSDefaultParameterValues['*:Encoding'] = 'UTF8'

.\build.ps1 -Target publish
# 自包含版：.\build.ps1 -Target publish -SelfContained
```

输出 `./dist/CIE1931xy.exe`；框架依赖版需要目标机器 .NET 8 Desktop Runtime，自包含版不需单独运行时。publish 的前置自检针对源码构建产物；发布后仍应单独验收实际发布文件，近期源码及发布验证见后文各轮记录。

## Git 与忽略范围

2026-11-19 初始化本地 main 仓库，建立源码、资源、测试与文档的初始记录；2026-11-21 按用户授权创建 GitHub 公开仓库 [lhbacxc/CIE1931xy-V1](https://github.com/lhbacxc/CIE1931xy-V1) 并绑定 origin。保留已有提交历史，提交说明默认中文。

`./.gitignore` 排除工程下 bin/obj、根目录 dist/artifacts、TestResults、CodeGraph 数据与验证目录、日志/临时备份、个人 IDE 状态、程序自动生成的 CIE1931xy.settings.json 及其临时文件、.env 配置；.env.example/sample/template 可以保留。共享 VS Code 配置、资源图片、doc/screens 历史截图和 tests/oracle JSON/文本保留在版本管理中，不按图片/CSV/TXT/SVG 扩展名整体忽略。

`./.gitattributes` 自动识别文本并规范仓库换行；源码、JSON 和文档检出为 LF，PowerShell/解决方案检出为 CRLF，位图/图标按二进制处理。tests/oracle 的 TXT 输入保留原始字节和 CRLF/LF/末尾换行，避免改变经典解析回归用例。不改变 UTF-8 内容或 BOM。

本地实验数据 `./测试光谱/` 与 `./test-spectrum/` 不随公开源码上传；发布包只包含本轮明确选择的程序、使用说明及 .NET 许可文件，不从 dist 整目录打包，避免带入个人配置或用户文件。

## GitHub Release 打包

公开 Release 提供框架依赖单 exe 与自包含便携 ZIP，校验值放在 SHA256SUMS.txt。先执行 `./build.ps1` 完成源码回归，再分别发布到被忽略的 `./artifacts/releases/<版本>/` 子目录，避免覆盖日常使用的 dist；命令中的版本号须与工程版本及 Git tag 一致。

以下是 v2.1.0 已验证的 PowerShell 命令：

```powershell
$ErrorActionPreference = 'Stop'
$OutputEncoding = [Console]::InputEncoding = [Console]::OutputEncoding = New-Object System.Text.UTF8Encoding
$PSDefaultParameterValues['*:Encoding'] = 'UTF8'

$releaseSdk = Join-Path $env:LOCALAPPDATA 'Microsoft/dotnet/dotnet.exe'
$releaseProject = './src/CIE1931xy.Rebuilt/CIE1931xy.Rebuilt.csproj'
$releaseArguments = @(
    'publish', $releaseProject, '-c', 'Release', '-r', 'win-x64',
    '-p:PublishSingleFile=true', '-p:IncludeNativeLibrariesForSelfExtract=true',
    '-p:DebugType=None', '-p:DebugSymbols=false', '--nologo'
)
& $releaseSdk @releaseArguments '-p:SelfContained=false' '-o' './artifacts/releases/v2.1.0/framework-dependent'
if ($LASTEXITCODE -ne 0) { throw '轻量版发布失败' }
& $releaseSdk @releaseArguments '-p:SelfContained=true' '-p:EnableCompressionInSingleFile=true' '-o' './artifacts/releases/v2.1.0/self-contained'
if ($LASTEXITCODE -ne 0) { throw '便携版发布失败' }
```

自包含 exe 启用内部压缩，首次启动会解包 native 组件；没有启用裁剪，避免 WinForms 动态加载兼容问题。便携 ZIP 内保留所用 .NET runtime 包的 LICENSE.TXT / THIRD-PARTY-NOTICES.TXT，使用带 DOTNET 后缀的文件名，区分原程序版权与运行时许可。

发布前验收实际 exe 的 `--selftest`。确认便携版无需额外运行时应仅给测试子进程设置空的 DOTNET_ROOT / DOTNET_ROOT_X64，并记录 COREHOST_TRACE，不能卸载或修改系统环境；本轮还启动了实际新版窗口，确认进入输入空闲。检查 ZIP 清单、解压后 exe 的 SHA256、上传后下载文件校验值；tag 指向对应源码提交。当前发布说明见 [v2.1.0](./RELEASE_v2.1.0.md)，首版记录见 [v2.0.0](./RELEASE_v2.0.0.md)。

### 2026-11-21 首次公开发布验收

- 保留既有本地 main 历史，新建公开 origin；本轮发布不实现仍在 TODO 中的箭头功能，不修改计算口径。
- 当前源码 Release 主程序及测试工程构建均为 0 警告、0 错误，两套 oracle 各 9/9，新版 12 个数值/解析检查和十个输出文件检查通过。
- 两种发布均使用标准 NuGet 还原与单文件发布参数，成功执行单文件分析器；无需沿用历史离线源或禁用分析器的绕过参数。两种实际 exe 自检通过。
- 仅对验证子进程隔离系统 .NET 根目录：轻量版返回缺少运行时错误，便携版自检及实际 MainForm 输入空闲检查通过。host trace 确认便携版采用内置 .NETCore / WindowsDesktop 8.0.34；未卸载系统运行时或修改系统环境变量。
- 轻量 exe 为 1,059,920 字节，与当前 dist 的 SHA256 一致；便携压缩 exe 为 72,521,399 字节，ZIP 为 66,980,720 字节。ZIP 仅含 exe、中文使用说明及两份 .NET 许可/声明，解压后 exe 的 SHA256 与发布原件一致，没有个人设置文件。
- 临时验收日志、trace 和 ZIP 解压核验位于被忽略的 `./.codegraph/validation/github-release/`；软件下载文件位于 `./artifacts/releases/v2.0.0/`，不纳入源码 Git。README、AGENTS、入门指南、开发指南和版本发布说明同步公开仓库与两种下载方式。
- GitHub [v2.0.0 Release](https://github.com/lhbacxc/CIE1931xy-V1/releases/tag/v2.0.0) 已正式公开发布，标记为 latest、非预发布；Git tag 指向发布提交 01ea497。源码 main 与 tag 均已推送。
- 上传后从 GitHub 下载两个程序包，SHA256 均与本地原件和 SHA256SUMS.txt 一致。本地 dist 已更新为此次轻量版并核对 SHA256，旧 exe 备份于本轮忽略的验证目录，原个人配置保留。

正常 git push 会同步所推送分支及其祖先提交，包含首次推送前的多次本地记录；未提交修改、忽略文件及未推送的其他分支不会自动同步。若需要合并历史为一次提交，应在推送前单独规划，不能擅自重写已有历史。

## CodeGraph 与长期事实

C# 索引覆盖全部源码和测试 Program，配置、PowerShell、Markdown 和资源另行读取。源码改动后执行 codegraph sync .，查看 codegraph status .；MCP projectPath 使用当前根目录。

调用图用于定位，不替代方法体与 Compile Link 配置。更新架构时改 ARCHITECTURE，用户行为改 README，环境与命令改本文；AGENTS.md 保留约束，TODO 保留用户待办。

## 2026-11-19 当前验收

| 检查 | 源码与实际运行证据 |
| --- | --- |
| SDK | 官方 ZIP SHA512 匹配；实际 SDK 路径与版本核对为用户安装、10.0.404 |
| Release | 主程序和测试工程从源码构建，0 警告、0 错误 |
| Debug | 系统 Windows PowerShell 的开发命令构建，0 警告、0 错误，exe/DLL/PDB 已生成 |
| 经典回归 | Release、Debug 均分别检查两套 oracle，每套 9/9、失败 0 |
| 新版自检 | 两种 Configuration 均为 12 个数值条件和十个输出文件通过，SELFTEST OK、退出码 0 |
| 启动 | Debug 新版/经典进程均启动并进入输入空闲，实际执行路径为 Debug 构建 exe；隐藏启动检查未覆盖可视化交互 |
| 输出 | 两种配置的 SVG 均可解析；位图/矢量 image 和 clipPath 数量正确；中文自检日志严格 UTF-8 解码通过 |
| 报告 | 新 GoldenTests 报告使用相对 oracle 路径，实际生成验证通过 |
| 编辑器 | 已写配置并验证其命令，未操作 IDE 内断点或设计器 |
| 范围 | 改动开发文件、脚本、日志编码及报告路径；计算公式/业务功能和程序版本未改，未重新发布 |

此前入门阶段曾因没有 SDK，只验证旧二进制；上述源码验收已替代那个构建阻塞结论。历史像素验收与 TIFF 验收仍未重跑，不应由本次编译或文件生成结果推断通过。

## 2026-11-19 新版文字裁切修复验收

- 原因：新版手工布局在全部控件加入之前执行了首次 DPI 缩放，后续固定尺寸按钮、设置标签及默认缩放模式的对话框没有与文字同步放大。修复方案与布局约束见架构说明的 DPI 章节。
- 修复范围：主工具栏、颜色/描边/批量改名栏、显示设置、快捷键提示、三个数据页签的表头和行高，以及输入坐标、单项改名、批量改名对话框。
- 本机实际 DPI=168（175%），主窗口与输入坐标对话框通过屏幕检查；临时布局检查程序进一步核对三个页签、按钮/标签文字边界、按钮重叠、表头宽度和设置区横向溢出。
- 在本机实际 DPI 及 100%/125%/150%/175%/200% 的边界与字体缩放模拟中，分别检查 1200×800、1000×640、1400×900 逻辑窗口尺寸、侧栏窄/宽布局及三个对话框；最终均为 `LAYOUT OK`、失败 0。模拟检查没有更改系统显示设置，不代表已经实际切换五种显示器 DPI 或验证了跨显示器拖动。
- 最终 Release 构建 0 警告、0 错误；两套经典 oracle 各 9/9，新版 12 个数值条件和十个导出文件检查通过，`SELFTEST OK`。本轮修复未修改经典计算与经典界面。
- 临时布局检查源及产物位于被忽略的 `./.codegraph/validation/layout-harness/`，不是新增的长期测试入口；日常构建、自检命令保持不变。
- 已更新并实际打开 `./dist/CIE1931xy.exe`，发布版自检与中文 UTF-8 日志检查通过，175% 下按钮、表头和显示设置通过屏幕验收。本轮单文件发布临时使用已缓存 NuGet 包目录作为源，并指定 `-p:EnableSingleFileAnalyzer=false`，绕过该分析器要求的未缓存运行时包下载；因此本轮没有执行单文件兼容性分析器，常规 publish 配置保留。
- 发布文件为 WinExe，本机无控制台直接运行其自检时 stdout 实测仍为 CP936；验收进程按该代码页解码后将日志保存为 UTF-8。通过 dotnet 宿主运行 DLL 的现有 build.ps1 自检日志仍为 UTF-8；不能只给直接运行的 WinExe 重定向流指定 UTF-8 解码并据此宣称日志正常。

## 2026-11-19 小圆角样式验收

新版按钮、按钮式开关、下拉与数值输入统一增加约 4 个逻辑像素的小圆角，沿用此前的尺寸、配色、DPI 布局和原生交互。控件职责见架构说明及入门指南的 `./src/CIE1931xy.Rebuilt/UI/` 入口。

Release 构建 0 警告、0 错误，两套经典 oracle 各 9/9，SelfTest 通过；本机 175% 与模拟 100%–200% 的已有布局检查均为 `LAYOUT OK`。屏幕验收确认小圆角、选中切换及原生下拉展开正常。沿用上节临时离线发布参数更新 `./dist/CIE1931xy.exe`，实际发布文件自检通过，十个输出文件齐全，复制后的 SHA256 与验收文件一致。

## 2026-11-19 按钮分组与统一输入验收

- 修改前的本地 Git 节点为 `9a09f78`，提交名「修复按钮文字与边框显示不全，统一控件为微圆角矩形」。本轮按已确认方案调整左右操作栏，并合并坐标输入与粘贴窗口；具体行为见 README，分组布局与共用输入链路见架构说明。
- Release 源码构建 0 警告、0 错误，两套经典 oracle 各 9/9；新版 12 个数值条件及十个导出文件检查通过，`SELFTEST OK`。
- 本机实际 DPI=168（175%）检查，以及模拟 100%/125%/150%/175%/200% 下三种窗口尺寸、窄/宽侧栏、三个页签与输入数据/改名对话框检查，均为 `LAYOUT OK`、失败 0。模拟检查未实际更改系统 DPI，不代表已验证跨显示器拖动。
- 在临时布局检查程序中实际打开输入窗口并提交数据，验证自动/指定类型的坐标与光谱输入、空输入不添加数据、空数据与导入后的按钮状态，以及空白底图导出按钮可用，均通过。屏幕检查确认新按钮顺序、分组间距、蓝色导图主按钮和输入窗口文字边界。
- 复用此前临时离线发布参数，向 `./.codegraph/validation/button-layout-publish/` 发布，实际单文件自检通过后更新 `./dist/CIE1931xy.exe`，复制前后 SHA256 一致。本轮同样禁用单文件兼容性分析器，常规工程与 publish 配置未改。
- 修改前的发布程序保存在被忽略的 `./.codegraph/validation/before-button-layout/`；临时布局/流程检查仍位于 `./.codegraph/validation/layout-harness/`，不作为新增的长期测试入口。

## 2026-11-19 网格可编辑性与无网格位图验收

- 原因：原版 PNG 底图的网格属于已有像素；位图背景渲染分支不会使用 `ShowGrid`。按钮事件与重绘有效，但此前未向用户说明该背景限制。
- xy 原版位图背景下网格按钮禁用、固定呈选中状态并显示提示；矢量 xy/u'v' 下可正常开关，背景和图型切换后保留矢量网格偏好。输入/计算链路与底图资源未改。
- 在临时布局检查程序中切换实际控件，确认按钮/提示状态、背景与图型切换及偏好恢复；网格开关的 xy/uv PNG 输出不同，SVG 分别增加 36/28 条网格线。实际导出无网格 PNG/TIFF，核验 1400×1400 px、300 DPI 及 TIFF 文件格式通过。
- 新提示触发了模拟 125% 下的纵向滚动，AutoSize 设置行曾按内部控件旧宽度撑出横向滚动条；为设置行同步最大宽度后，本机 175% 与模拟 100%–200%、三种窗口尺寸和窄/宽侧栏检查均为 `LAYOUT OK`、失败 0。
- Release 构建 0 警告、0 错误，两套经典 oracle 各 9/9，源码自检及实际单文件发布自检通过。沿用已有临时离线发布参数，更新 `./dist/CIE1931xy.exe` 并核验 SHA256 一致；原发布文件备份于被忽略的 `./.codegraph/validation/before-grid-toggle/`。
- 网格开关的临时 PNG/TIFF/SVG 输出位于被忽略的 `./.codegraph/validation/grid/`，检查复用临时布局程序，不新增长期测试命令；SelfTest 本身仍不包含 TIFF 检查。README 与使用说明已明确「矢量背景 → 关闭网格 → 导出 PNG/TIFF」路径。

## 2026-11-19 新版统一矢量绘图验收

- 新版 xy/u'v' 固定采用矢量绘图，移除背景选择、新版背景切换菜单及 Ctrl+B；网格和标注可独立开关，图型切换保留网格设置。设置区与使用说明明确预览方式不限制导出格式，导出对话框标明 PNG/TIFF 为位图、SVG 为矢量图。
- 主程序及测试工程 Release 构建 0 警告、0 错误，两套经典 oracle 各 9/9，源码自检通过。经典界面与底层位图兼容分支未修改，SelfTest 仍覆盖该分支。
- 临时流程检查确认默认背景为矢量、xy/u'v' 网格开关有效及图型切换后设置保留，PNG/TIFF/SVG 均可导出；两种图型的 SVG 均不含 image 元素，网格开关分别改变 36/28 条线。
- 本机实际 DPI=168（175%）以及模拟 100%/125%/150%/175%/200%、三种窗口尺寸、窄/宽侧栏、三个页签及对话框的布局检查通过，`LAYOUT OK`、失败 0。屏幕检查确认背景选择已移除，说明文字显示完整；模拟检查不代表已实测跨显示器拖动。
- 沿用已记录的临时离线发布参数，实际单文件自检通过后更新并打开 `./dist/CIE1931xy.exe`，复制前后 SHA256 一致。旧发布文件备份于被忽略的 `./.codegraph/validation/before-vector-preview/`；README、架构说明、入门指南与 AGENTS.md 已同步当前新版约束。

## 2026-11-19 SVG 拼接细缝与边界缺口修复验收

- 原因：独立色块的抗锯齿会形成二维拼接细线；从透明位图采样时，轮廓附近整块透明像素又导致阶梯缺口，后续矢量裁剪不能补回缺失的颜色。
- SVG 改为无像素掩码颜色场、连续横向 linearGradient 与细扫描带；内部关闭拼接边缘抗锯齿，外轮廓使用与描边共用的平滑裁剪路径。色标简化将 RGB 单通道采样误差限制在一个 8 位色阶，保留纯矢量输出。实现约束见架构说明。
- 主程序与测试程序 Release 构建 0 警告、0 错误；两套经典 oracle 各 9/9，源码及实际单文件自检通过。自检 PNG 字节大小与修复前一致，SVG 为渐变矢量且不含 image 元素；本轮没有改变计算公式或位图输出算法。
- xy/u'v' 的 PNG/TIFF/SVG 导出、网格开关与图型切换检查通过。使用已有本机 Edge 与 bundled sharp/librsvg 分别渲染两种图型的 SVG，原尺寸及四倍放大检查通过，未安装新依赖。
- 对 xy 四倍图在轮廓内侧抽检 561 个位置，修复前发现 48 个白色缺口，修复后两种渲染器均为 0；中部扫描线检查 1060 个位置，修复前 12 个二阶通道突变，修复后均为 0。该抽检不等同于对所有坐标、缩放与第三方查看器的完整证明。
- 临时样例、渲染对比与指标位于被忽略的 `./.codegraph/validation/svg-seams-qa/`，修复前 SVG 位于 `./.codegraph/validation/svg-seams-before/`；1400×1400 的无网格 SVG 样例位于 `./.codegraph/validation/grid/*-grid-off-export.svg`。xy/u'v' 样例约为 3.21/3.14 MB，体积比原来的粗色块方案增加。
- 沿用已记录的临时离线发布参数，实际单文件自检通过后更新 `./dist/CIE1931xy.exe`，复制前后 SHA256 一致；旧发布程序备份于被忽略的 `./.codegraph/validation/before-svg-seams/`。README、架构说明、入门指南与 AGENTS.md 已同步。

## 2026-11-19 x+y=1 参考线开关验收

- 新版新增「参考线」按钮及视图菜单入口，名称说明为 x+y=1（z=0），默认隐藏。按钮与菜单同步，xy 下启用、u'v' 下禁用并保留原选择；控制现有 `ShowClosingLine` 渲染字段，黑色光谱边界与紫线保留。
- Release 主程序与测试工程构建 0 警告、0 错误，两套经典 oracle 各 9/9，源码及实际单文件自检通过。
- 临时流程检查确认默认隐藏、按钮/菜单同步、图型切换状态及 PNG/TIFF/SVG 导出。参考线 SVG 元素计数开启为 1、关闭为 0，黑色光谱边界元素保留；u'v' 导出不含该 xy 参考线。
- 本机 175% 及模拟 100%–200%、三种窗口尺寸、窄/宽侧栏、三个页签和对话框布局检查均为 `LAYOUT OK`、失败 0；模拟检查不代表已实测跨显示器拖动。临时样例位于被忽略的 `./.codegraph/validation/reference-line/`。
- 沿用已记录的临时离线发布参数，发布文件自检通过后更新 `./dist/CIE1931xy.exe` 并核验 SHA256 一致；旧程序备份于被忽略的 `./.codegraph/validation/before-reference-line/`。README、架构说明与 TODO 已同步。

## 2026-11-19 坐标轴标题与间距验收

- 新增「坐标轴设置」按钮及视图菜单入口，支持编辑横、纵轴标题和间距（0–80，默认 16）；xy/u'v' 的标题分别保留，空名称使用图型默认值，间距共用。纵轴标题旋转 -90°，预览与 PNG/TIFF/SVG 导出使用同一布局。
- 刻度与标题先测量后布局，间距从刻度文字外缘起算，动态预留左侧/底部留白；长标题按可用空间缩小字号。布局后统一保存 LastTransform，避免矢量预览变换为空或沿用旧位置；数值范围与计算未修改。
- 临时对话框流程检查通过：应用、取消不保存、恢复默认、xy/u'v' 切换保留各自标题及三种图片格式导出。默认、中文、自定义间距及 80 字长标题的 SVG 经 Edge 实际渲染检查，旋转后的标题边界均在画布内，未与刻度重叠；结果保存于被忽略的 `./.codegraph/validation/axis-titles/bbox-metrics.json`。
- 本机 175% 与模拟 100%–200%、三种窗口尺寸、窄/宽侧栏、三个页签及坐标轴设置等对话框布局检查通过，`LAYOUT OK`、失败 0。模拟检查不代表已实测跨显示器拖动。
- Release 构建 0 警告、0 错误，两套经典 oracle 各 9/9；最终纵轴旋转版本的源码及实际单文件自检通过。沿用已记录的临时离线发布参数，更新 `./dist/CIE1931xy.exe` 并核验 SHA256 一致；旧程序备份于被忽略的 `./.codegraph/validation/before-axis-titles/`。
- README、架构说明、入门指南与 TODO 已同步。

## 2026-11-19 黑白画布背景验收

- 新增「黑色背景」按钮及视图菜单入口，默认白底、开启黑底；切换图型保留选择。画布背景、坐标轴、刻度、网格、参考线与图例配色同步，色域颜色场保留，波长/数据标签按所在底色选择黑字或白字。
- Release 构建 0 警告、0 错误，两套经典 oracle 各 9/9，源码及实际单文件自检通过。
- 临时流程检查确认按钮/菜单同步、xy/u'v' 黑底、PNG/TIFF 四角纯黑或纯白、SVG 的完整背景矩形及无内嵌 image；黑白 SVG 的渐变色标序列相同，恢复白底的预览与切换前完全相同。
- 本机 175% 与模拟 100%–200%、三种窗口尺寸、窄/宽侧栏、三个页签及对话框布局检查均通过，`LAYOUT OK`、失败 0。设置区增高以容纳开关。Edge 实际渲染检查了黑白 SVG 的坐标轴、刻度、波长标签、数据标签与图例。
- 临时样例位于被忽略的 `./.codegraph/validation/black-background/`。沿用已记录的临时离线发布参数，更新 `./dist/CIE1931xy.exe` 并核验 SHA256 一致，旧程序备份于被忽略的 `./.codegraph/validation/before-black-background/`。README、架构说明、入门指南与 TODO 已同步。

## 2026-11-20 光谱隐藏与快捷键验收

- 光谱右键菜单新增隐藏/显示并标注 Space；删除标注 Delete。表格支持多选与连续切换，隐藏后保留原选择、名称、样式与计算结果；图片排除隐藏项，CSV/TXT 保留全部数据。README 与架构说明同步行为及键盘处理约束。
- Release 构建 0 警告、0 错误，两套经典 oracle 各 9/9，新版数值与导出自检通过。临时隐藏窗口集成检查通过 28 项：真实多列光谱导入、通过窗口消息触发表格快捷键、混合选择、无选择、右键隐藏/恢复/删除、删除最后一条及计算行同步。
- xy/u'v' 下全部隐藏的 GDI 图像与无曲线图逐像素一致，SVG 文本完全一致，恢复后 SVG 改变；图例、标签和选中高亮一并排除。PNG/TIFF 从同一 GDI 渲染结果保存；CSV/TXT 隐藏前后内容一致（TXT 比较排除生成时间）。本机 175% 缩放的已有布局与输入流程检查通过，`LAYOUT OK`、失败 0；未实测跨显示器拖动。
- 修复两项实际验收问题：WinForms 不接受裸 Space 作为菜单 ShortcutKeys，改为显示表格快捷键提示；无控制台的单文件进程设置 Console.OutputEncoding 失败，改用 UTF-8 标准输出流回退。实际发布文件自检及重定向中文 UTF-8 日志检查通过。
- 沿用临时离线发布参数（缓存 NuGet 源、禁用单文件兼容性分析器），更新 `./dist/CIE1931xy.exe` 并核验 SHA256 一致。旧程序与临时检查文件保存在被忽略的 `./.codegraph/validation/curve-visibility/`；没有新增依赖、长期测试入口或修改经典计算。

## 2026-11-20 图中文字字体与大小验收

- 新增「字体与字号」按钮和视图菜单入口，默认数字/字母/科学符号 Times New Roman、汉字及中文标点 SimSun（宋体）；可选本机字体，并分别调整六类文字大小（8–64）。应用、取消、恢复默认及混排预览均通过；设置在 xy/u'v' 间共用，不跨进程保存。
- GDI 与 SVG 共用 PlotTextLayout 的 Unicode 分段、测量与基线；SVG 使用 text/tspan 标注各段字体，保留混排空格和纵轴旋转，不嵌入字体文件。改字号时重新布局，过宽的数据标签和图例限制到图内可用空间；该适配不保证密集文字之间完全无重叠。
- 最终 Release 构建 0 警告、0 错误，两套经典 oracle 各 9/9，源码自检及实际发布文件自检通过，发布日志为有效 UTF-8。临时字体/交互/导出检查共 56 项通过，覆盖六类大小、字体切换、中英文混排、组合附加符、扩展汉字、GDI/SVG 测量一致、字号进入 SVG、PNG/TIFF/SVG 及 1400×1400 / 300 DPI 核验。
- Edge 实际渲染检查 xy/u'v' 的默认字体、全部文字大小 64 和 60 字长标签；六组 SVG 的文字边界均位于画布内（允许 2px 渲染误差）。本机 175% 以及模拟 100%–200%、三种窗口尺寸、窄/宽侧栏、所有页签和字体窗口的初始/最小尺寸检查通过，`LAYOUT OK`、失败 0；未实测跨显示器拖动。
- 修复实际交互检查发现的空下拉列表初始化异常，以及流式布局把混排预览面板压成零高度的问题；临时 Font 和 GdiCanvas 的 Pen/Brush 均按生命周期释放。未新增依赖或修改经典界面/算法。
- 沿用缓存 NuGet 源和禁用单文件兼容性分析器的临时发布参数，更新 `./dist/CIE1931xy.exe`，复制前后 SHA256 一致；旧程序、窗口截图、输出及边界记录位于被忽略的 `./.codegraph/validation/fonts/`。README、架构说明、入门指南、AGENTS 和 TODO 已同步。

## 2026-11-20 程序同目录配置保存验收

- 新版偏好写入 AppContext.BaseDirectory 下的 CIE1931xy.settings.json，包含图型、显示开关、中英文字体与六类大小、两套轴标题/间距、导出 DPI 与文件名；导图按最后文件扩展名选择格式。400ms 合并变更，关闭时补存，加载期间抑制保存；业务数据、单项样式、隐藏/选择状态和渲染缓存不持久化。
- Release 构建 0 警告、0 错误，两套经典 oracle 各 9/9，源码及实际单文件自检通过，UTF-8 日志检查通过。配置专项检查共 39 项通过，包含不同工作目录、首次/无变更启动、取消编辑、损坏/null/未来版本/非法 UTF-8、部分字段、非法范围、不可用字体、文件被锁时保留旧配置并清理临时文件，以及解除锁定后重试。
- 通过两个独立进程修改并恢复实际 MainForm，验证背景配色、开关与禁用状态、字体/大小、两种轴标题、DPI 与文件名；立即关闭仍保存最新修改，重启前后 SVG 文本完全一致，恢复时原配置不被重写，数据仍为空。带配置的实际发布 exe 在不同工作目录启动并进入输入空闲，配置未被重写；自检不修改程序旁配置。
- build.ps1 保留原目录范围与联接点校验，移除整个 dist 的递归清空。提取并运行真实 Invoke-Publish 函数、调用 SDK 发布后，预置配置与用户 TXT 的 SHA256 均不变，`PUBLISH PRESERVATION OK`；未新增依赖。
- 沿用缓存 NuGet 源及禁用单文件兼容性分析器的临时发布参数，更新 `./dist/CIE1931xy.exe` 并核验 SHA256 一致。旧程序、配置样例、重启对照及发布检查保存在被忽略的 `./.codegraph/validation/settings/`；README、架构说明、入门指南、AGENTS、TODO 及忽略规则同步。

## 2026-11-20 图片导出成功提示验收

- MainForm 的 PNG/TIFF/SVG 保存成功后统一弹出「导出成功」信息框，显示完整保存位置，保留状态栏记录。取消保存直接返回，异常分支提示错误后立即返回；Exporter、自检与数据导出行为未改。
- Release 构建 0 警告、0 错误，两套经典 oracle 各 9/9，源码及实际发布文件自检通过。临时原生对话框集成检查 19 项通过：三种图片格式各出现一次成功提示并包含保存路径，提示前文件存在且有效，PNG/TIFF 为 1400×1400；取消无文件及成功提示，保存路径选定后模拟文件被锁，写入失败只提示错误并保留旧文件。
- 沿用缓存 NuGet 源、禁用单文件兼容性分析器的临时发布参数，更新 `./dist/CIE1931xy.exe` 并核验 SHA256 一致，配置保留。旧程序与临时导出检查位于被忽略的 `./.codegraph/validation/export-notification/`；README、架构说明和 TODO 已同步。

## 2026-11-20 左下显示设置提示裁切修复

- 原因是新增字体按钮后设置行换行，固定 374 逻辑像素的设置区不足，底部快捷键提示被滚动区裁切。现将提示独立固定到底部并分三行，根据宽度/字体/DPI 测量高度；主面板按实际内容与父容器空间调整，为数据页签保留空间，小窗口只滚动设置项。清空按钮改为与坐标轴/字体按钮相同的按文字尺寸布局。
- 本机 175% 的启动截图确认三行提示完整、三个按钮同排，默认窗口无设置区滚动条。临时布局检查覆盖启动状态、1000×640/1200×800/1400×900、窄/宽侧栏、三个页签及对话框；本机 175% 和模拟 100%–200% 均为 `LAYOUT OK`、失败 0，并检查提示不在滚动容器、不越界、不被设置项覆盖及所有快捷键文本保留。未实测跨显示器拖动。
- Release 构建 0 警告、0 错误，两套经典 oracle 各 9/9，源码及实际单文件自检通过。沿用缓存 NuGet 源和禁用单文件兼容性分析器的临时发布参数更新 `./dist/CIE1931xy.exe`，SHA256 一致，配置保留。旧程序、截图与自检文件位于被忽略的 `./.codegraph/validation/bottom-hint/`；README 与架构说明同步布局约束。

## 2026-11-21 运行环境与缺失提示核实

- 当前 dist 与上一轮验证发布文件 SHA256 相同，属于 win-x64 / WinExe / 框架依赖单文件。runtimeconfig 要求 Microsoft.NETCore.App 和 Microsoft.WindowsDesktop.App 8.0.0，默认使用可用的 .NET 8 补丁版本；本机已安装两项 8.0.31。App host 版本为 8.0.34，不能将它当作所需运行时最低版本或 SDK 版本。
- 仅对子进程将 DOTNET_ROOT_X64 / DOTNET_ROOT 指向空目录，并设置 DOTNET_DISABLE_GUI_ERRORS=1 避免实际弹窗，验证原 dist 在进入 C# 入口前退出，报「You must install .NET to run this application」、架构 x64、.NET location Not found，含下载入口及 0x80008083（hostfxr.dll 未找到）。未卸载或修改本机运行时；证据位于被忽略的 `./.codegraph/validation/runtime-requirements/`。
- [Microsoft 文档](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-environment-variables#dotnet_disable_gui_errors) 说明 GUI apphost 默认提供部分启动错误弹窗，设置该变量才只写 stderr。当前 Program.Main 无自定义中文运行时检查；实际 GUI 缺环境弹窗未在本轮显示。README 与 TODO 同步当前要求及提示来源，未改源码或重新发布。

## 2026-11-21 运行时目录选择追踪

- 对真实 dist 的自检子进程设置 COREHOST_TRACE / COREHOST_TRACEFILE 追踪，不改变系统环境。启动器读取 HKLM 的 x64 .NET InstallLocation，选中机器级 dotnet 根目录，加载其 host/fxr/8.0.31/hostfxr.dll，随后在同一根目录下选中 Microsoft.NETCore.App 和 Microsoft.WindowsDesktop.App 8.0.31；未使用用户目录 SDK 自带的 10.0.15。日志位于被忽略的 `./.codegraph/validation/runtime-search/host-trace.log`。
- 当前无 DOTNET_ROOT 系列环境覆盖。按 [Microsoft 的 .NET 7+ 规则](https://learn.microsoft.com/en-us/dotnet/core/compatibility/deployment/7.0/multilevel-lookup)，exe 在环境指定目录、注册目录或默认目录中选定第一处 .NET 安装，之后只在该根目录中查找所需框架；与通过特定 dotnet.exe 启动 DLL 的目录选择区别开。README 同步部署说明，未改源码或重新发布。

## 2026-11-21 点列旁箭头与整体拖动验收

- 从既有 main 创建 `codex/arrows` 分支，实现推荐范围并按用户示例修订位置：参考起终点确定方向，箭头初始放在点列旁边，鼠标拖动整体平移，保持方向/形状，不修改数据坐标。支持默认白色、自定义颜色、线宽、直线/弯曲、曲率与左右弯、细描边、多条箭头管理及编辑/删除；原始 TODO 的渐变色设想尚未实现。
- 当前源码 Release 构建 0 警告、0 错误，两套经典 oracle 各 9/9；原有 12 个数值/解析检查和基础十个输出文件检查通过。ArrowSelfTest 新增 41 项检查、四组 PNG/TIFF/SVG，验证点列旁的平行方向、整体偏移、命中、位图实际绘制与绘图区边界、端点隐藏/恢复/删除、SVG 结构/颜色/纯矢量、TIFF 格式/尺寸/DPI、导出高亮排除，全部通过。
- 临时实际窗口集成检查本机 175% 为 94 项通过，模拟 100%–200% 为 190 项通过，覆盖添加、编辑、取消、重复端点、两类数据源、多条箭头、直线/弯曲鼠标事件与捕获、编辑保留拖动位置、图型切换、管理删除、端点改名/隐藏/恢复/删除/清空，以及对话框按钮、数值字段、固定提示和管理窗口最小尺寸边界。此前主界面布局检查也覆盖本机 175% 与模拟 100%–200%、三种窗口尺寸及窄/宽侧栏，失败 0；未实测跨显示器拖动。
- 截图发现 AutoSize 表格布局把独立 NumericUpDown 缩成零宽，设置 100×26 最小逻辑尺寸修复，并增加数值字段边界检查。样式示意中将点列与箭头分开；使用提示独立固定在滚动区外并按宽度/字体测量，默认窗口增高，小窗口保留设置区滚动。
- 用已有 bundled sharp/librsvg 实际渲染示例和弯曲 SVG，检查 PNG/SVG 的箭头位置、方向、白色显示、曲线与箭头头部衔接；没有增加工程依赖。README 示例保存为 `./doc/screens/16-arrows-beside-points.png`，临时图、窗口截图、日志与检查程序在忽略的 `./.codegraph/validation/arrows/` 和 `arrows-harness/`。
- 本地测试版单文件 exe 位于 `./artifacts/arrows/CIE1931xy.exe`，仍需 .NET 8 Desktop Runtime x64；实际测试 exe 自检通过，生成 22 个文件。测试产物不进入 Git，main 与公开 v2.0.0 Release 不由本轮自动合并或替换。README、架构说明、入门指南、AGENTS、箭头设计和 TODO 同步当前分支行为与验证范围。

## 2026-11-21 v2.1.0 合并与发布准备

- 按用户后续授权同步 codex/arrows，再以保留分支历史的合并提交并入 main；工程、主窗口、关于和 TXT 报告版本统一为 2.1.0。源码运行目标仍为 .NET 8，没有新增工程依赖或修改计算口径。
- README 末尾改为「源码来源与致谢」，正式说明源码由 [chenhunquan](https://github.com/chenhunquan) 提供、本仓库在此基础上优化和扩展，同时保留历史原作者 nicklxg 的署名与记录。用户确认仅作来源声明，不调整自动生成的 GitHub Contributors 列表；不新增维护者权限或改写提交作者。
- 合并后的 Release 构建为 0 警告、0 错误，两套经典 oracle 各 9/9；基础数值/解析及 41 项箭头自检通过。轻量版与压缩自包含版均使用标准发布参数，两个实际 exe 的 FileVersion 为 2.1.0.0，ProductVersion 以 2.1.0 为基准并附构建时的 Git 修订后缀。
- 两个实际 exe 自检均通过，各输出 22 个文件；隔离系统 .NET 路径后，轻量版提示缺少运行时，便携版自检及 MainForm 启动输入空闲检查通过。没有改动系统运行时或环境变量。
- 轻量 exe 为 1,091,664 字节，便携 ZIP 为 66,994,657 字节，ZIP 仅含 exe、中文使用说明及 .NET 许可/第三方声明；解压 exe 的 SHA256 与原件一致。产物位于 `./artifacts/releases/v2.1.0/`，临时验证位于 `./.codegraph/validation/release-v2.1.0/`；发布说明见 [v2.1.0](./RELEASE_v2.1.0.md)。
- [GitHub v2.1.0 Release](https://github.com/lhbacxc/CIE1931xy-V1/releases/tag/v2.1.0) 已正式公开发布，设为 latest、非预发布，两个软件包和 SHA256SUMS.txt 已上传；上传后下载的 SHA256 与本地原件及校验文件一致。Git tag 对应发布提交 b16145b，codex/arrows 已确认是 main 的祖先。
- 本地 `./dist/CIE1931xy.exe` 同步为 2.1.0.0，SHA256 与轻量版原件一致；旧 exe 备份于本轮忽略的验证目录，原用户配置保留。
