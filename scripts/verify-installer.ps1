param(
    [string]$Configuration = 'Release',
    [string]$Dotnet,
    [switch]$Smoke
)
$ErrorActionPreference = 'Stop'
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
    throw '未找到 dotnet SDK。'
}

# 真实安装包的静默安装/卸载冒烟：只使用整合目录下的临时安装目录，不写任何开机启动项。
# 安装器会写真实的 HKCU 卸载信息；若发现已有同名键则拒绝执行，避免覆盖用户现有安装登记。
function Invoke-Smoke([string]$setup) {
    $uninstallKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\ToolSuite'
    $runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
    if (Test-Path $uninstallKey) { throw "已存在 $uninstallKey，跳过冒烟以免覆盖真实安装登记。" }
    if ($null -ne (Get-ItemProperty -Path $runKey -Name 'ToolSuite.Suite' -ErrorAction SilentlyContinue)) {
        throw '已存在 ToolSuite.Suite 开机启动值，跳过冒烟以免改动真实启动项。'
    }
    $smokeRoot = Join-Path $repoRoot 'artifacts/installer-smoke'
    $install = Join-Path $smokeRoot 'ToolSuite'
    if (Test-Path $smokeRoot) { [IO.Directory]::Delete($smokeRoot, $true) }
    New-Item -ItemType Directory -Path $smokeRoot -Force | Out-Null

    $installProcess = Start-Process -FilePath $setup -ArgumentList @('/silent', "/dir=`"$install`"", '/no-start-menu') -Wait -PassThru
    if ($installProcess.ExitCode -ne 0) { throw "静默安装失败，退出码 $($installProcess.ExitCode)。" }
    foreach ($required in @('ToolSuite.exe', 'coreclr.dll', 'PresentationFramework.dll', 'ToolSuite.Setup.exe', 'version.txt')) {
        if (!(Test-Path (Join-Path $install $required))) { throw "静默安装缺少 $required。" }
    }
    if ((Get-Content (Join-Path $install 'version.txt') -Raw).Trim() -ne '1.0.0') { throw '静默安装版本文件不正确。' }
    $registered = Get-ItemProperty -Path $uninstallKey -ErrorAction Stop
    if ($registered.DisplayName -ne '工具套件' -or $registered.InstallLocation -ne $install) {
        throw '静默安装未写入正确的套件卸载信息。'
    }
    if ($null -ne (Get-ItemProperty -Path $runKey -Name 'ToolSuite.Suite' -ErrorAction SilentlyContinue)) {
        throw '安装不得写入开机启动项。'
    }
    Write-Output "冒烟：静默安装成功 $install"

    $uninstallProcess = Start-Process -FilePath $setup -ArgumentList @('/silent', '/uninstall', "/dir=`"$install`"") -Wait -PassThru
    if ($uninstallProcess.ExitCode -ne 0) { throw "静默卸载失败，退出码 $($uninstallProcess.ExitCode)。" }
    for ($i = 0; $i -lt 40 -and (Test-Path $install); $i++) { Start-Sleep -Milliseconds 250 }
    if (Test-Path $install) { throw '静默卸载后安装目录仍存在。' }
    if (Test-Path $uninstallKey) { throw '静默卸载后套件卸载信息仍存在。' }
    if ($null -ne (Get-ItemProperty -Path $runKey -Name 'ToolSuite.Suite' -ErrorAction SilentlyContinue)) {
        throw '卸载不得改动或留下开机启动项。'
    }
    if (Test-Path (Join-Path ([Environment]::GetFolderPath('Programs')) '工具套件.lnk')) {
        throw '未请求开始菜单快捷方式时不得创建快捷方式。'
    }
    if (Test-Path $smokeRoot) { [IO.Directory]::Delete($smokeRoot, $true) }
    Write-Output '通过：真实安装包静默安装/卸载冒烟（临时目录，未写开机启动项）'
}

$sdk = Resolve-Dotnet
$checkRoot = Join-Path $repoRoot 'artifacts/installer-checks/latest'
Push-Location $repoRoot
try {
    if (Test-Path $checkRoot) { [IO.Directory]::Delete($checkRoot, $true) }
    & $sdk build "tests/ToolSuite.InstallerChecks/ToolSuite.InstallerChecks.csproj" -c $Configuration
    if ($LASTEXITCODE -ne 0) { throw '安装器隔离检查构建失败。' }
    & $sdk "tests/ToolSuite.InstallerChecks/bin/$Configuration/net10.0-windows/ToolSuite.InstallerChecks.dll" $checkRoot
    if ($LASTEXITCODE -ne 0) { throw '安装器隔离检查失败。' }

    $publish = Join-Path $repoRoot 'artifacts/publish/win-x64'
    foreach ($required in @('ToolSuite.exe', 'coreclr.dll', 'PresentationFramework.dll', 'hostfxr.dll')) {
        if (!(Test-Path (Join-Path $publish $required))) { throw "自包含发布缺少 $required。" }
    }
    $runtimeConfig = Get-Content (Join-Path $publish 'ToolSuite.runtimeconfig.json') -Raw
    if ($runtimeConfig -notmatch 'includedFrameworks' -or $runtimeConfig -match '"framework"\s*:') {
        throw '发布产物仍依赖目标机预装运行时。'
    }
    $installer = Get-ChildItem (Join-Path $repoRoot 'artifacts/installer') -Filter 'ToolSuite-Setup-*.exe' |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ($null -eq $installer -or $installer.Length -lt 10MB) { throw '安装包产物缺失或过小。' }
    Write-Output "通过：自包含发布 $publish；安装包 $($installer.FullName)（$([math]::Round($installer.Length / 1MB, 1)) MB）；隔离检查产物 $checkRoot"
    if ($Smoke) { Invoke-Smoke $installer.FullName }
}
finally { Pop-Location }
