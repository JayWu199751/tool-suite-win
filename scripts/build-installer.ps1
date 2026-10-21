param(
    [string]$Configuration = 'Release',
    [string]$Runtime = 'win-x64',
    [string]$Version = '1.0.0',
    [string]$Dotnet
)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
[Console]::OutputEncoding = [Text.Encoding]::UTF8
$repoRoot = Split-Path $PSScriptRoot -Parent

function Resolve-Dotnet {
    if ($Dotnet) { return (Resolve-Path $Dotnet).Path }
    if ($env:DOTNET_ROOT) {
        $candidate = Join-Path $env:DOTNET_ROOT 'dotnet.exe'
        if (Test-Path $candidate) { return $candidate }
    }
    $perUser = Join-Path $env:LOCALAPPDATA 'Microsoft/dotnet/dotnet.exe'
    if (Test-Path $perUser) { return $perUser }
    $command = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($command) { return $command.Source }
    throw '未找到 dotnet SDK。请通过 -Dotnet 指定 dotnet.exe。'
}

$sdk = Resolve-Dotnet
$publishRoot = Join-Path $repoRoot "artifacts/publish/$Runtime"
$payload = Join-Path $repoRoot "artifacts/publish/ToolSuite-$Runtime.zip"
$installerPublish = Join-Path $repoRoot "artifacts/installer/publish"
$installerOutput = Join-Path $repoRoot 'artifacts/installer'
$installerExe = Join-Path $installerOutput "ToolSuite-Setup-$Version.exe"

Push-Location $repoRoot
try {
    # 1. 自包含发布套件（携带 .NET 10 桌面运行时），全部产物在整合目录内。
    if (Test-Path $publishRoot) { [IO.Directory]::Delete($publishRoot, $true) }
    & $sdk publish "src/ToolSuite.App/ToolSuite.App.csproj" -c $Configuration -r $Runtime --self-contained true `
        -p:DebugType=None -p:DebugSymbols=false -o $publishRoot
    if ($LASTEXITCODE -ne 0) { throw '套件自包含发布失败。' }
    foreach ($required in @('ToolSuite.exe', 'coreclr.dll', 'PresentationFramework.dll', 'hostfxr.dll')) {
        if (!(Test-Path (Join-Path $publishRoot $required))) { throw "自包含发布缺少 $required。" }
    }

    # 2. 载荷 zip 供安装器内嵌。直接调用 ZipFile，避免 Compress-Archive 在控制台刷进度条。
    if (Test-Path $payload) { [IO.File]::Delete($payload) }
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [IO.Compression.ZipFile]::CreateFromDirectory($publishRoot, $payload,
        [IO.Compression.CompressionLevel]::Optimal, $false)

    # 3. 单文件自包含安装器，内嵌发布载荷。
    if (Test-Path $installerPublish) { [IO.Directory]::Delete($installerPublish, $true) }
    & $sdk publish "tools/ToolSuite.Installer/ToolSuite.Installer.csproj" -c $Configuration -r $Runtime --self-contained true `
        "-p:PayloadZip=$payload" -o $installerPublish
    if ($LASTEXITCODE -ne 0) { throw '安装器构建失败。' }
    $built = Join-Path $installerPublish 'ToolSuite.Setup.exe'
    if (!(Test-Path $built)) { throw '安装器产物不存在。' }
    New-Item -ItemType Directory -Path $installerOutput -Force | Out-Null
    Copy-Item $built $installerExe -Force
    if (!(Test-Path $installerExe)) { throw '安装器产物复制失败。' }

    $summary = @(
        "套件版本: $Version",
        "发布目录: $publishRoot",
        "载荷: $payload",
        "安装包: $installerExe",
        "安装包大小: $([math]::Round((Get-Item $installerExe).Length / 1MB, 1)) MB",
        "构建时间: $(Get-Date -Format o)"
    )
    $summary | Set-Content (Join-Path $installerOutput 'build-info.txt') -Encoding UTF8
    $summary | ForEach-Object { Write-Output $_ }
}
finally { Pop-Location }
