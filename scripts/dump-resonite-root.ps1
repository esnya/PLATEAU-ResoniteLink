param(
    [Parameter(Mandatory = $true)]
    [string]$Endpoint,

    [string]$RepoPath = (Split-Path -Parent $PSScriptRoot),
    [string]$OutputPath = '',
    [int]$Depth = -1,
    [bool]$IncludeComponentData = $true
)

$ErrorActionPreference = 'Stop'

function Get-DotNetCommandPath {
    $candidates = @()

    if (-not [string]::IsNullOrWhiteSpace($env:DOTNET_EXE)) {
        $candidates += $env:DOTNET_EXE
    }

    if (-not [string]::IsNullOrWhiteSpace($env:DOTNET_HOST_PATH)) {
        $candidates += $env:DOTNET_HOST_PATH
    }

    if (-not [string]::IsNullOrWhiteSpace($env:DOTNET_ROOT)) {
        $candidates += (Join-Path $env:DOTNET_ROOT 'dotnet.exe')
    }

    $command = Get-Command -Name dotnet.exe -CommandType Application -ErrorAction SilentlyContinue
    if ($command) {
        return $command.Source
    }

    $command = Get-Command -Name dotnet -CommandType Application -ErrorAction SilentlyContinue
    if ($command) {
        return $command.Source
    }

    if (-not [string]::IsNullOrWhiteSpace($env:ProgramFiles)) {
        $candidates += (Join-Path $env:ProgramFiles 'dotnet\dotnet.exe')
    }

    foreach ($candidate in $candidates) {
        if (-not [string]::IsNullOrWhiteSpace($candidate) -and (Test-Path -LiteralPath $candidate)) {
            return (Get-Item -LiteralPath $candidate).FullName
        }
    }

    throw 'Unable to locate dotnet.exe. Set DOTNET_EXE, DOTNET_HOST_PATH, or DOTNET_ROOT, or ensure dotnet is available on PATH.'
}

$dotnet = Get-DotNetCommandPath
$runtimeRoot = Join-Path $RepoPath 'runtime\windows\resonite\root-dumps'
$adminRuntimeRoot = Join-Path $RepoPath 'runtime\windows\resonite'
$adminProject = Join-Path $RepoPath 'scripts\ResoniteAdmin\ResoniteAdmin.csproj'
$adminDll = Join-Path $RepoPath 'artifacts\build\windows\bin\ResoniteAdmin\Release\net10.0\ResoniteAdmin.dll'
$adminExe = Join-Path $RepoPath 'artifacts\build\windows\bin\ResoniteAdmin\Release\net10.0\ResoniteAdmin.exe'

if (Test-Path -LiteralPath $adminDll) {
    Remove-Item -LiteralPath $adminDll -Force
}

$buildOutput = & "$dotnet" build $adminProject -c Release 2>&1
$buildOutput | Out-Host
if (-not (Test-Path $adminDll)) {
    throw "ResoniteAdmin build output not found: $adminDll"
}

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

$launcherPath = if (Test-Path -LiteralPath $adminExe -PathType Leaf) { $adminExe } else { $dotnet }
$launcherArguments = if ($launcherPath -eq $adminExe) { $arguments } else { @($adminDll) + $arguments }

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
    AdminDllPath         = $adminDll
    AdminDllLastWriteTime = (Get-Item -LiteralPath $adminDll).LastWriteTime
}
