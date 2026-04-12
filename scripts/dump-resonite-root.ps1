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
$adminProject = Join-Path $RepoPath 'scripts\ResoniteAdmin\ResoniteAdmin.csproj'
$adminDll = Join-Path $RepoPath 'artifacts\build\windows\bin\ResoniteAdmin\Release\net10.0\ResoniteAdmin.dll'

if (-not (Test-Path $adminDll)) {
    $buildOutput = & "$dotnet" build $adminProject -c Release 2>&1
    $buildOutput | Out-Host
    if (-not (Test-Path $adminDll)) {
        throw "ResoniteAdmin build output not found: $adminDll"
    }
}

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    New-Item -ItemType Directory -Force -Path $runtimeRoot | Out-Null
    $timestamp = Get-Date -Format 'yyyyMMdd-HHmmss'
    $OutputPath = Join-Path $runtimeRoot ("root-dump-{0}.json" -f $timestamp)
}

$arguments = @(
    $adminDll,
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

$commandOutput = & "$dotnet" @arguments 2>&1
$commandOutput | Out-Host
if ($LASTEXITCODE -ne 0) {
    $outputText = ($commandOutput | ForEach-Object { $_.ToString() }) -join [Environment]::NewLine
    throw "ResoniteAdmin dump-root failed. ExitCode=$LASTEXITCODE`n$outputText"
}

if (-not (Test-Path -LiteralPath $OutputPath -PathType Leaf)) {
    $outputText = ($commandOutput | ForEach-Object { $_.ToString() }) -join [Environment]::NewLine
    throw "Root dump output was not created: $OutputPath`n$outputText"
}

[pscustomobject]@{
    Endpoint             = $Endpoint
    OutputPath           = (Resolve-Path -LiteralPath $OutputPath).Path
    Depth                = $Depth
    IncludeComponentData = $IncludeComponentData
    AdminDllPath         = $adminDll
    AdminDllLastWriteTime = (Get-Item -LiteralPath $adminDll).LastWriteTime
}
