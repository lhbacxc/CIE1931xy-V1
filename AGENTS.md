# CIE1931xy 项目指令

## 开始任务

- 先读本文件及 [项目入门指南](./doc/PROJECT_GUIDE.md)；按任务补读 [架构说明](./doc/ARCHITECTURE.md) 和 [开发与验证指南](./doc/DEVELOPMENT.md)。
- 面向用户的行为以 [README](./README.md) 和当前源码为准；[合并计划](./doc/MERGE_PLAN.md)、[修订摘录](./doc/CHANGELOG.md)、[逆向记录](./doc/REVERSE_ENGINEERING.md) 包含历史状态，不能直接当作当前需求。
- 项目已启用 CodeGraph。先用索引定位符号、调用关系与影响范围，再按新鲜度提示补查；MCP 调用的 `projectPath` 使用当前项目根目录。
- 索引状态用 `codegraph status .` 检查，源码改动后用 `codegraph sync .` 同步；不要手改 `./.codegraph/` 数据库。索引覆盖 C#，配置、脚本、资源和文档另行读取。

## 编码、语言与写入限制

- 所有新增或修改的文本统一 UTF-8，不改变现有中文文本编码；文件读写明确指定编码。
- 回答、技术文档和变更记录默认中文；专有名词、专业术语、代码与命令可保留英文。
- 项目文档中的路径使用相对路径。单次文件写入工具调用不超过 300 行且不超过 12,000 字符；超过时分次执行。
- 规则冲突按编码、语言、文档组织、其他规则的顺序处理。

## PowerShell

- 所有 CLI、脚本和环境交互使用 PowerShell，优先 pwsh；不要混用 Bash 语法，处理数据优先对象管道。
- 脚本启动时设置以下错误处理与编码（有 `param` 的脚本在参数声明后立即设置）：

```powershell
$ErrorActionPreference = 'Stop'
$OutputEncoding = [Console]::InputEncoding = [Console]::OutputEncoding = New-Object System.Text.UTF8Encoding
$PSDefaultParameterValues['*:Encoding'] = 'UTF8'
```

- 文件读写加 `-Encoding UTF8`；外部命令失败须检查 `$LASTEXITCODE`，仅设置 ErrorActionPreference 不能捕获所有外部命令失败。
- 递归删除前解析并确认目标位于预期目录，使用 `Remove-Item -LiteralPath` 等 PowerShell 原生命令。

## 技术栈与源码边界

- 主工程 `./src/CIE1931xy.Rebuilt/CIE1931xy.Rebuilt.csproj`：C# / net8.0-windows / WinForms；版本 2.0.0，无第三方 PackageReference。
- 测试工程 `./tests/GoldenTests/GoldenTests.csproj`：net8.0 控制台程序，通过 Compile Link 复用四个经典计算文件，不引用主工程。
- 主入口 `./src/CIE1931xy.Rebuilt/Program.cs`；默认 `MainForm`，`--classic` 使用 `Form1`，`--selftest` 调用 `Core/SelfTest.cs`。
- 经典链路 `Form1 → SpectralInput → CieMath → VbText` 保留 VB 语义、1nm/5nm 和四位小数取整；改动须检查 oracle。
- 新版链路 `MainForm → SpectrumCsvParser/CoordinateParser → Colorimetry → Model → DiagramRenderer/Exporter` 默认逐点求和、未归一、未做经典四位取整；不要把两条链路混同。
- 新版工作台的 xy/u'v' 统一使用矢量绘图，PNG/TIFF/SVG 仅为导出格式选项，不恢复位图背景选择或新版 Ctrl+B。渲染通过 `IPlotCanvas` 共用 GDI 与 SVG 输出；新增绘制能力须核对两种实现。新版 SVG 使用渐变扫描带与平滑轮廓裁剪、不内嵌 PNG；背景不能复用像素透明掩码，修复后须核对放大显示的接缝与边界缺口。底层位图兼容分支及经典底图保留。
- 新版图中文字默认数字/字母 Times New Roman、中文 SimSun（宋体）；混排分段、测量与基线共用 `./src/CIE1931xy.Rebuilt/Core/Rendering/PlotTextLayout.cs`。字体/大小交互位于 `./src/CIE1931xy.Rebuilt/MainForm.FontSettings.cs`；改字体绘制须同时核对 GDI 与 SVG、旋转标题及文字边界，释放临时 Font 与 GdiCanvas 资源。经典位图文字不受该设置影响。
- 新版偏好保存为实际 exe 同目录的 `./CIE1931xy.settings.json`，以 AppContext.BaseDirectory 定位；恢复后才启用保存，不能在初始化时覆盖用户配置。存储白名单、UTF-8、数值校验及同目录完整写入后替换由 Core/AppSettings.cs、Core/SettingsStore.cs 与 MainForm.Settings.cs 维护；不保存业务数据或渲染缓存。经典入口及自检不读写该配置。
- 位图固定 684×732，轴矩形为 (59,12) 起、600×675；对应 xy 范围 0–0.8 / 0–0.9。坐标映射不可使用数据包络替代该范围。
- 新版 PerMonitorV2 与经典 DpiUnaware 分别维护；不能直接把经典窗口改为新版缩放方式并宣称像素兼容。
- 新版手工布局必须在全部控件加入后才恢复布局并执行首次 DPI 缩放；按钮、标签、设置行和对话框要按文字与可用空间布局。具体约束见架构说明的 DPI 章节。
- 通常使用 PascalCase 类型/方法、下划线私有字段；现有文件级和块级 namespace 并存，沿用所改文件风格。Nullable 与 ImplicitUsings 均关闭。

## 构建与验证

- 从项目根目录执行 `./build.ps1`，默认 all = 主程序构建 + 双基线 GoldenTests + SelfTest；restore/build/test/selftest/run/clean/publish 入口见开发指南。
- `./global.json` 约束 .NET 10 SDK（基准 10.0.404、latestFeature、禁止预览版）；主工程仍为 .NET 8 目标。脚本分别定位 SDK 和 .NET 8 运行时宿主，不能把 SDK 版本当作程序运行版本。
- `-Target test` 构建测试工程，并分别运行两套 oracle；正常结果是每套 9/9。SDK 在用户目录时优先通过脚本执行测试，避免使用缺少 .NET 8 的 SDK 宿主直接运行 DLL。
- `-Target run` 构建后启动对应 Configuration 下的主程序，不读取 dist；VS Code 配置也调试 Debug apphost.exe。
- publish 先构建、回归、自检，再核验并更新 `./dist/` 的发布产物，不清空目录，保留配置与其他用户文件。工程下的 bin/obj 是产物，dist 中同时可能包含用户配置；不能把整个目录当作可删除产物。
- GoldenTests 只校验经典解析/计算字段及格式化文本，不校验窗体、表格状态或像素；新版解析/渲染用 `--selftest` 补充。
- 自检含 12 个数值检查及 PNG/SVG/CSV/TXT 文件检查，没有 TIFF 或界面自动化检查；u'v' 的 bitmap 组合仍回退矢量背景。
- 解决方案 `./CIE1931xy.sln` 包含主程序和 GoldenTests；编辑规范见 `./.editorconfig`，编辑器配置见 `./.vscode/`。没有独立测试框架、覆盖率或 CI，不能把自定义回归命令写成 dotnet test。
- 验证现有二进制必须标明，不能作为源码构建成功的证据；源码验证结果记录在开发指南。

## Python 与依赖

- 本项目没有 Python 工程或已确认的 Conda 环境；常规构建/测试不需要 Python，不为本 C# 项目生成伪 requirements.txt。
- 如任务需要新增 Python 辅助程序，先询问用户当前 Conda 环境名；使用目标 Python 绝对路径，禁止 `conda activate`。
- 安装新 Python 包前必须向用户确认，在目标环境安装后于根目录生成或更新 requirements.txt。
- Python 文件读写显式使用 `encoding='utf-8'`，入口 main() 开始处设置 `sys.stdout.reconfigure(encoding='utf-8')`。

## 长期文档维护

- 结束前只检查本次涉及的架构、目录职责、环境/依赖、命令、约定与可复用排障事实；有长期变化时自主增量更新对应文档。
- 详细事实放到 `./doc/ARCHITECTURE.md` 或 `./doc/DEVELOPMENT.md`，用户行为同步 README；本文件只保留执行约束和入口。
- 保留用户规则、人工内容和有效约定；不写入猜测、敏感数据、临时日志或未经验证的命令。无长期变化不写文档。
- 本地 Git 仓库使用 main；提交说明默认中文。origin 为公开仓库 [lhbacxc/CIE1931xy-V1](https://github.com/lhbacxc/CIE1931xy-V1)，发布流程见开发指南；后续推送与 Release 发布仍遵循用户授权，不推断分支或合并约定。
- 遵循 `./.gitignore` 和 `./.gitattributes`：构建/发布、CodeGraph、日志和个人编辑器状态不纳入 Git；源码、资源、doc/screens、tests/oracle 及共享 VS Code 配置应保留。
- tests/oracle 的 TXT 回归输入含换行边界用例，Git 禁止对其转换换行；不要批量格式化这些输入或重建 oracle 答案。
- `./TODO.md` 保留用户原始待办；不得未经实现或验证就勾为完成，历史计划中被后续修订覆盖的条目也不能直接恢复。
