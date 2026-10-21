param([string]$Configuration = 'Release')
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$suiteRoot = $repoRoot
if (Test-Path (Join-Path $repoRoot '.git') -PathType Leaf) {
    $gitMetadata = Get-Content (Join-Path $repoRoot '.git') -Raw
    if ($gitMetadata -match 'gitdir:\s*(.+)[\\/]\.git[\\/]worktrees[\\/]') { $suiteRoot = $Matches[1] }
}
$env:DOTNET_CLI_HOME = Join-Path $suiteRoot '.dotnet'
$env:NUGET_PACKAGES = Join-Path $suiteRoot '.packages'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$sdk = if ($env:DOTNET_ROOT -and (Test-Path (Join-Path $env:DOTNET_ROOT 'dotnet.exe'))) { Join-Path $env:DOTNET_ROOT 'dotnet.exe' } else { (Get-Command dotnet).Source }
$testRoot = Join-Path $repoRoot ('artifacts/host-check-' + [Guid]::NewGuid().ToString('N'))
Push-Location $repoRoot
try {
    & $sdk run --project tests/ToolSuite.HostChecks -c $Configuration -- (Join-Path $testRoot 'core')
    if ($LASTEXITCODE -ne 0) { throw '宿主控制入口检查失败。' }
    & $sdk build src/ToolSuite.App -c $Configuration
    if ($LASTEXITCODE -ne 0) { throw 'WPF 宿主构建失败。' }
    & $sdk "src/ToolSuite.App/bin/$Configuration/net10.0-windows/ToolSuite.dll" --data-root (Join-Path $testRoot 'wpf') --verify-host
    if ($LASTEXITCODE -ne 0 -or !(Test-Path (Join-Path $testRoot 'wpf/verification-passed.txt'))) { throw "真实宿主检查失败，查看 $testRoot。" }
    Write-Output "通过，隔离检查产物：$testRoot"
} finally { Pop-Location }
