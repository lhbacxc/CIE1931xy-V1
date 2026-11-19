# C# 开发环境补齐方案

范围依据：用户已确认继续使用 C#，补齐本机开发环境与项目开发文件。本次不实现 TODO 中的新产品功能。

## 选择与边界

- 使用 .NET 10 LTS SDK 的官方 Windows x64 ZIP，校验 SHA512 后安装到当前用户目录，无需管理员权限或完整 Visual Studio。
- 保持主工程 net8.0-windows、GoldenTests net8.0；先验证现有代码，目标框架升级作为单独变更处理。
- 补齐 `./CIE1931xy.sln`、`./global.json`、`./.editorconfig` 及 `./.vscode/` 构建/调试/扩展推荐配置。
- `./build.ps1` 自动找到 SDK；构建和运行分离于发布，run 使用刚构建的程序；测试分别检查两套 oracle，默认 all 同时执行主程序自检。
- SDK 用于编译，现有机器级 .NET 8 Runtime / Desktop Runtime 用于运行 .NET 8 产物；脚本显式区分 SDK 与运行时宿主。
- 命令、日志和项目文件统一 UTF-8；发布清理使用已核验的项目 dist 路径，不跨目录删除。

## 实施与验收

1. 下载、校验并安装 SDK，验证其实际路径与版本；补齐用户 PATH，现有终端可通过脚本直接定位 SDK。
2. 添加解决方案和配置文件，固定 SDK 在 .NET 10 内滚动，不擅自切换到其他大版本。
3. 修复构建脚本的双 oracle、旧 dist 启动和 UTF-8 日志问题，增加 restore / selftest / clean 入口。
4. 从源码构建 Release 和用于编辑器调试的 Debug，分别完成双基线 GoldenTests 与 SelfTest。
5. 检查自检输出、SVG/XML、编码及开发配置；同步 `./AGENTS.md`、README 和开发指南。

开发环境补齐阶段不安装第三方 C# 包、不建立 Python 环境，当时未初始化 Git；后续本地 Git 初始化已按用户的新请求单独完成，见开发指南。Visual Studio / VS Code 为可选编辑器，配置文件不意味着已安装或已验证编辑器内交互调试。

## 完成状态

上述开发文件、SDK 安装、Release/Debug 构建、双基线回归、SelfTest、启动与日志检查已完成，实际证据见 [开发与验证指南](./DEVELOPMENT.md)。SDK 目录加入用户 PATH 后，旧进程或机器级 PATH 优先级仍可能影响直接 dotnet 命令；项目脚本自动定位用户 SDK，VS Code 任务使用系统 PowerShell，避免要求另装 pwsh。
