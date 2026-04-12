param(
    [Parameter(Mandatory = $true)]
    [string]$Endpoint,

    [string]$RepoPath = (Split-Path -Parent $PSScriptRoot),
    [string]$OutputPath = '',
    [int]$Depth = -1,
    [bool]$IncludeComponentData = $true
)

$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'windows-build-tools.ps1')

$dotnet = Resolve-DotNetCommandPath
$runtimeRoot = Join-Path $RepoPath 'runtime\windows\resonite\root-dumps'
$adminRuntimeRoot = Join-Path $RepoPath 'runtime\windows\resonite'
$adminProject = Join-Path $RepoPath 'scripts\ResoniteAdmin\ResoniteAdmin.csproj'

$adminBuild = Ensure-ResoniteAdminBuildOutput -DotNetPath $dotnet -ProjectPath $adminProject -RepoRoot $RepoPath

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    New-Item -ItemType Directory -Force -Path $runtimeRoot | Out-Null
    $timestamp = Get-Date -Format 'yyyyMMdd-HHmmss'
    $OutputPath = Join-Path $runtimeRoot ("root-dump-{0}.json" -f $timestamp)
}

$arguments = @(
    '--dump-root',
    $Endpoint,
    '--output',
    $OutputPath,
    '--depth',
    $Depth.ToString([System.Globalization.CultureInfo]::InvariantCulture)
)

if ($IncludeComponentData) {
    $arguments += '--include-component-data'
}
else {
    $arguments += '--exclude-component-data'
}

New-Item -ItemType Directory -Force -Path $adminRuntimeRoot | Out-Null
$stdoutPath = Join-Path $adminRuntimeRoot 'resonite-admin-dump.stdout.log'
$stderrPath = Join-Path $adminRuntimeRoot 'resonite-admin-dump.stderr.log'
foreach ($path in @($stdoutPath, $stderrPath)) {
    if (Test-Path -LiteralPath $path) {
        Remove-Item -LiteralPath $path -Force
    }
}

$launcherPath = if (-not [string]::IsNullOrWhiteSpace($adminBuild.ExePath)) { $adminBuild.ExePath } else { $dotnet }
$launcherArguments = if (-not [string]::IsNullOrWhiteSpace($adminBuild.ExePath)) { $arguments } else { @($adminBuild.DllPath) + $arguments }
$process = Start-Process `
    -FilePath $launcherPath `
    -ArgumentList $launcherArguments `
    -WorkingDirectory $RepoPath `
    -Wait `
    -PassThru `
    -RedirectStandardOutput $stdoutPath `
    -RedirectStandardError $stderrPath

$stdoutText = if (Test-Path -LiteralPath $stdoutPath) { Get-Content -LiteralPath $stdoutPath -Raw } else { '' }
$stderrText = if (Test-Path -LiteralPath $stderrPath) { Get-Content -LiteralPath $stderrPath -Raw } else { '' }
if (-not [string]::IsNullOrWhiteSpace($stdoutText)) {
    $stdoutText | Out-Host
}

if ($process.ExitCode -ne 0) {
    throw "ResoniteAdmin dump-root failed. ExitCode=$($process.ExitCode)`nSTDOUT:`n$stdoutText`nSTDERR:`n$stderrText"
}

if (-not (Test-Path -LiteralPath $OutputPath -PathType Leaf)) {
    throw "Root dump output was not created: $OutputPath`nSTDOUT:`n$stdoutText`nSTDERR:`n$stderrText"
}

[pscustomobject]@{
    Endpoint             = $Endpoint
    OutputPath           = (Resolve-Path -LiteralPath $OutputPath).Path
    Depth                = $Depth
    IncludeComponentData = $IncludeComponentData
    AdminDllPath         = $adminBuild.DllPath
    AdminDllLastWriteTime = $adminBuild.DllLastWriteTime
}
