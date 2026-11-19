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

本地 Git 仓库已初始化为 main，尚未配置远端。没有独立测试框架、覆盖率或 CI 配置。本项目的自定义回归不是 dotnet test，不生成伪 Python requirements.txt；后续 Python 辅助程序遵守 AGENTS.md 的环境确认规则。

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
| publish | 先构建、双基线回归、自检，再核验并重建 dist |

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

自检包含 12 个数值检查，以及四组 PNG/SVG、CSV/TXT 的最小文件大小检查，共十个输出文件。自检图片为 900×900、PNG 144DPI，UI 导出为 1400×1400、默认 300DPI，二者参数不同。

四组命名为 xy-bitmap、xy-vector、uv-bitmap、uv-vector；u'v' 不支持位图，后两组实际都走矢量。xy-bitmap SVG 内嵌一个 PNG image，其他三份 SVG 为矢量色块并含 clipPath。

自检不验证 TIFF、完整 UI 流程、选中联动、像素差异或多显示器 DPI。它依赖 Windows / System.Drawing；免开主窗口不等于可以在 Linux 运行。渲染改动后按受影响的格式补充视觉和尺寸/DPI检查。

Exporter 本身不会自动关闭 HighlightSelection；UI 导出入口会克隆样式并关闭，其他调用者须自行设置。

## 发布与清理

发布前确认 `./dist/` 只存放发布产物。脚本校验 dist 的实际绝对路径位于项目根目录，并拒绝联接点/符号链接后才递归清理；该操作不会保留 dist 中的用户文件。

```powershell
$ErrorActionPreference = 'Stop'
$OutputEncoding = [Console]::InputEncoding = [Console]::OutputEncoding = New-Object System.Text.UTF8Encoding
$PSDefaultParameterValues['*:Encoding'] = 'UTF8'

.\build.ps1 -Target publish
# 自包含版：.\build.ps1 -Target publish -SelfContained
```

输出 `./dist/CIE1931xy.exe`；框架依赖版需要目标机器 .NET 8 Desktop Runtime，自包含版不需单独运行时。publish 的前置自检针对源码构建产物；发布后仍应单独验收实际发布文件。本轮没有重新发布，旧 dist 保留原状。

## Git 与忽略范围

2026-11-19 按用户要求初始化本地 main 仓库，建立源码、资源、测试与文档的初始记录；未绑定远端。本地提交用于保留开发历史，提交说明默认中文。

`./.gitignore` 排除工程下 bin/obj、根目录 dist/artifacts、TestResults、CodeGraph 数据与验证目录、日志/临时备份、个人 IDE 状态及 .env 配置；.env.example/sample/template 可以保留。共享 VS Code 配置、资源图片、doc/screens 历史截图和 tests/oracle JSON/文本保留在版本管理中，不按图片/CSV/TXT/SVG 扩展名整体忽略。

`./.gitattributes` 自动识别文本并规范仓库换行；源码、JSON 和文档检出为 LF，PowerShell/解决方案检出为 CRLF，位图/图标按二进制处理。tests/oracle 的 TXT 输入保留原始字节和 CRLF/LF/末尾换行，避免改变经典解析回归用例。不改变 UTF-8 内容或 BOM。

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
