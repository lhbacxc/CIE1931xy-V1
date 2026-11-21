<#
.SYNOPSIS
    CIE1931xy：还原、构建、双基线回归、自检、运行和发布。
.DESCRIPTION
    使用 global.json 中约束的 SDK。SDK 与运行时宿主分别定位，支持用户目录便携 SDK。
    all（默认）= 构建 + 两套 oracle + 主程序自检。run 启动构建目录中的程序。
#>
param(
    [ValidateSet('restore', 'build', 'test', 'selftest', 'publish', 'run', 'clean', 'all')]
    [string]$Target = 'all',
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [string]$Runtime = 'win-x64',
    [switch]$SelfContained,
    [string]$DotnetPath = '',
    [string]$SelfTestDir = '.codegraph\validation\selftest'
)

$ErrorActionPreference = 'Stop'
$OutputEncoding = [Console]::InputEncoding = [Console]::OutputEncoding = New-Object System.Text.UTF8Encoding
$PSDefaultParameterValues['*:Encoding'] = 'UTF8'
$projectRoot = [IO.Path]::GetFullPath($PSScriptRoot)
$mainProject = Join-Path $projectRoot 'src\CIE1931xy.Rebuilt\CIE1931xy.Rebuilt.csproj'
$testProject = Join-Path $projectRoot 'tests\GoldenTests\GoldenTests.csproj'
$solutionPath = Join-Path $projectRoot 'CIE1931xy.sln'
$mainOutputDir = Join-Path $projectRoot "src\CIE1931xy.Rebuilt\bin\$Configuration\net8.0-windows"
$testDll = Join-Path $projectRoot "tests\GoldenTests\bin\$Configuration\net8.0\GoldenTests.dll"
$distPath = Join-Path $projectRoot 'dist'

function Get-DotnetCandidates {
    if ($DotnetPath) { $DotnetPath }
    Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet\dotnet.exe'
    Join-Path $env:USERPROFILE '.dotnet\dotnet.exe'
    if ($env:DOTNET_ROOT) { Join-Path $env:DOTNET_ROOT 'dotnet.exe' }
    $pathCommand = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($pathCommand) { $pathCommand.Source }
    Join-Path $env:ProgramFiles 'dotnet\dotnet.exe'
}

function Resolve-Sdk {
    foreach ($candidate in @(Get-DotnetCandidates | Select-Object -Unique)) {
        if (-not (Test-Path -LiteralPath $candidate -PathType Leaf)) { continue }
        $sdkList = @(& $candidate --list-sdks 2>$null)
        if ($LASTEXITCODE -ne 0 -or $sdkList.Count -eq 0) { continue }
        $selectedVersion = & $candidate --version 2>$null
        if ($LASTEXITCODE -eq 0 -and $selectedVersion -match '^10\.') { return $candidate }
    }
    throw '找不到符合 global.json 的 .NET 10 SDK。请安装 Windows x64 .NET 10 SDK，或用 -DotnetPath 指定其 dotnet.exe。只有 Desktop Runtime 不能构建。'
}

function Resolve-Runtime([bool]$NeedDesktop = $false) {
    foreach ($candidate in @(Get-DotnetCandidates | Select-Object -Unique)) {
        if (-not (Test-Path -LiteralPath $candidate -PathType Leaf)) { continue }
        $runtimeList = @(& $candidate --list-runtimes 2>$null)
        if ($LASTEXITCODE -ne 0) { continue }
        $hasCore = @($runtimeList | Where-Object { $_ -match '^Microsoft\.NETCore\.App 8\.' }).Count -gt 0
        $hasDesktop = @($runtimeList | Where-Object { $_ -match '^Microsoft\.WindowsDesktop\.App 8\.' }).Count -gt 0
        if ($hasCore -and (-not $NeedDesktop -or $hasDesktop)) { return $candidate }
    }
    throw '当前工程目标为 .NET 8，缺少可用的 .NET 8 Runtime / Desktop Runtime。SDK 版本和程序运行目标是两回事。'
}

function Invoke-Sdk([string[]]$Arguments) {
    & $script:sdkExe @Arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet $($Arguments[0]) 失败（exit $LASTEXITCODE）" }
}

function Invoke-Build {
    Write-Host "==> 构建主程序 ($Configuration)" -ForegroundColor Cyan
    Invoke-Sdk @('build', $mainProject, '-c', $Configuration, '--nologo')
}

function Invoke-GoldenTests {
    Write-Host '==> 构建测试程序并逐套验证 oracle' -ForegroundColor Cyan
    Invoke-Sdk @('build', $testProject, '-c', $Configuration, '--nologo')
    $runtimeExe = Resolve-Runtime
    foreach ($oracleName in @('oracle_results_with_blackbody.json', 'oracle_results_without_blackbody.json')) {
        $oraclePath = Join-Path $projectRoot "tests\oracle\$oracleName"
        Write-Host "    基线：$oracleName"
        & $runtimeExe $testDll --oracle $oraclePath
        if ($LASTEXITCODE -ne 0) { throw "回归失败：$oracleName（exit $LASTEXITCODE）" }
    }
}

function Invoke-SelfTest([string]$ApplicationDll = (Join-Path $mainOutputDir 'CIE1931xy.dll')) {
    Write-Host '==> 主程序数值、解析、渲染和导出自检' -ForegroundColor Cyan
    $runtimeExe = Resolve-Runtime -NeedDesktop $true
    $outDir = if ([IO.Path]::IsPathRooted($SelfTestDir)) {
        [IO.Path]::GetFullPath($SelfTestDir)
    } else {
        [IO.Path]::GetFullPath((Join-Path $projectRoot $SelfTestDir))
    }
    New-Item -ItemType Directory -Path $outDir -Force | Out-Null
    $startInfo = New-Object System.Diagnostics.ProcessStartInfo
    $startInfo.FileName = $runtimeExe
    $startInfo.Arguments = '"' + $ApplicationDll + '" --selftest "' + $outDir + '"'
    $startInfo.WorkingDirectory = $projectRoot
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.StandardOutputEncoding = New-Object System.Text.UTF8Encoding
    $startInfo.StandardErrorEncoding = New-Object System.Text.UTF8Encoding
    $process = New-Object System.Diagnostics.Process
    $process.StartInfo = $startInfo
    try {
        if (-not $process.Start()) { throw '无法启动主程序自检' }
        $outputTask = $process.StandardOutput.ReadToEndAsync()
        $errorTask = $process.StandardError.ReadToEndAsync()
        $process.WaitForExit()
        $outputText = $outputTask.GetAwaiter().GetResult()
        $errorText = $errorTask.GetAwaiter().GetResult()
        Set-Content -LiteralPath (Join-Path $outDir 'selftest.stdout.log') -Value $outputText -Encoding UTF8 -NoNewline
        Set-Content -LiteralPath (Join-Path $outDir 'selftest.stderr.log') -Value $errorText -Encoding UTF8 -NoNewline
        if ($outputText) { Write-Host $outputText.TrimEnd() }
        if ($errorText) { Write-Host $errorText.TrimEnd() -ForegroundColor Yellow }
        if ($process.ExitCode -ne 0 -or $outputText -notmatch 'SELFTEST OK') {
            throw "主程序自检失败（exit $($process.ExitCode)），请检查输出目录中的日志。"
        }
    } finally {
        $process.Dispose()
    }
}

function Invoke-Publish {
    # 验证项目根目录下的真实 dist 目录，拒绝符号链接或联接点。
    $resolvedRoot = (Resolve-Path -LiteralPath $projectRoot).ProviderPath
    $expectedDist = [IO.Path]::GetFullPath((Join-Path $resolvedRoot 'dist'))
    $resolvedDist = [IO.Path]::GetFullPath($distPath)
    if ($resolvedDist -ne $expectedDist -or (Split-Path -Parent $resolvedDist) -ne $resolvedRoot) {
        throw '发布目标不在项目 dist 目录中，已停止。'
    }
    if (Test-Path -LiteralPath $resolvedDist) {
        $distItem = Get-Item -LiteralPath $resolvedDist -Force
        if (-not $distItem.PSIsContainer -or ($distItem.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw 'dist 不是普通目录，已停止发布。'
        }
        if ((Resolve-Path -LiteralPath $resolvedDist).ProviderPath -ne $expectedDist) {
            throw 'dist 的实际路径不符合发布范围，已停止发布。'
        }
        # 保留配置和用户文件；dotnet publish 只更新自身生成的发布产物。
    }
    Write-Host "==> 发布 ($Runtime, SelfContained=$SelfContained)" -ForegroundColor Cyan
    $publishArguments = @(
        'publish', $mainProject, '-c', $Configuration, '-r', $Runtime, '-o', $resolvedDist,
        "-p:SelfContained=$($SelfContained.IsPresent.ToString().ToLower())",
        '-p:PublishSingleFile=true', '-p:IncludeNativeLibrariesForSelfExtract=true',
        '-p:DebugType=None', '-p:DebugSymbols=false', '--nologo'
    )
    Invoke-Sdk $publishArguments
    Write-Host ('发布文件：' + (Join-Path $resolvedDist 'CIE1931xy.exe'))
}

function Invoke-Run {
    $appExe = Join-Path $mainOutputDir 'CIE1931xy.exe'
    if (-not (Test-Path -LiteralPath $appExe)) { throw '找不到刚构建的主程序。' }
    Write-Host "==> 启动构建版：$appExe" -ForegroundColor Cyan
    Start-Process -FilePath $appExe -WorkingDirectory $projectRoot | Out-Null
}

Push-Location -LiteralPath $projectRoot
try {
    $script:sdkExe = Resolve-Sdk
    Write-Host "SDK：$script:sdkExe" -ForegroundColor DarkGray
    & $script:sdkExe --version
    if ($LASTEXITCODE -ne 0) { throw 'SDK 版本核验失败' }
    switch ($Target) {
        'restore' { Invoke-Sdk @('restore', $solutionPath, '--nologo') }
        'build' { Invoke-Build }
        'test' { Invoke-GoldenTests }
        'selftest' { Invoke-Build; Invoke-SelfTest }
        'publish' { Invoke-Build; Invoke-GoldenTests; Invoke-SelfTest; Invoke-Publish }
        'run' { Invoke-Build; Invoke-Run }
        'clean' { Invoke-Sdk @('clean', $solutionPath, '-c', $Configuration, '--nologo') }
        'all' { Invoke-Build; Invoke-GoldenTests; Invoke-SelfTest }
    }
    Write-Host '完成。' -ForegroundColor Green
} finally {
    Pop-Location
}
