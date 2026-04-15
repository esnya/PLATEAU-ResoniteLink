param(
    [Parameter(Mandatory = $true)]
    [string]$RepoPath,

    [string]$Endpoint = '',
    [string]$StatePath = '',
    [string]$OutputPath = '',
    [string]$Label = 'root',
    [int]$Depth = -1,
    [bool]$IncludeComponentData = $true
)

$ErrorActionPreference = 'Stop'

function Resolve-StatePath {
    param(
        [string]$ConfiguredStatePath,
        [string]$RuntimeRootPath
    )

    if (-not [string]::IsNullOrWhiteSpace($ConfiguredStatePath)) {
        return $ConfiguredStatePath
    }

    return (Join-Path $RuntimeRootPath 'active-session.json')
}

function Resolve-Endpoint {
    param(
        [string]$ConfiguredEndpoint,
        [string]$ResolvedStatePath
    )

    if (-not [string]::IsNullOrWhiteSpace($ConfiguredEndpoint)) {
        return $ConfiguredEndpoint
    }

    if (-not (Test-Path -LiteralPath $ResolvedStatePath -PathType Leaf)) {
        throw "No tracked headless session state file exists at '$ResolvedStatePath', and -Endpoint was not provided."
    }

    $state = Get-Content -LiteralPath $ResolvedStatePath -Raw | ConvertFrom-Json
    if ([string]::IsNullOrWhiteSpace($state.Endpoint)) {
        throw "Tracked headless session state '$ResolvedStatePath' does not contain Endpoint."
    }

    return [string]$state.Endpoint
}

function Resolve-OutputPath {
    param(
        [string]$ConfiguredOutputPath,
        [string]$RepoRootPath,
        [string]$ResolvedLabel
    )

    if (-not [string]::IsNullOrWhiteSpace($ConfiguredOutputPath)) {
        return $ConfiguredOutputPath
    }

    $timestamp = Get-Date -Format 'yyyyMMdd-HHmmss'
    $dumpRoot = Join-Path $RepoRootPath 'runtime\windows\resonite\root-dumps'
    New-Item -ItemType Directory -Force -Path $dumpRoot | Out-Null
    return (Join-Path $dumpRoot ("{0}-{1}.json" -f $ResolvedLabel, $timestamp))
}

$repoRoot = (Resolve-Path -LiteralPath $RepoPath).Path
$headlessRuntimeRoot = Join-Path $repoRoot 'runtime\windows\headless'
$resolvedStatePath = Resolve-StatePath -ConfiguredStatePath $StatePath -RuntimeRootPath $headlessRuntimeRoot
$resolvedEndpoint = Resolve-Endpoint -ConfiguredEndpoint $Endpoint -ResolvedStatePath $resolvedStatePath
$resolvedOutputPath = Resolve-OutputPath -ConfiguredOutputPath $OutputPath -RepoRootPath $repoRoot -ResolvedLabel $Label

. (Join-Path $PSScriptRoot 'windows-build-tools.ps1')

$dotnet = Resolve-DotNetCommandPath
$runtimeRoot = Join-Path $repoRoot 'runtime\windows\resonite\root-dumps'
$adminRuntimeRoot = Join-Path $repoRoot 'runtime\windows\resonite'
$adminProject = Join-Path (Split-Path -Parent $PSScriptRoot) 'tools\ResoniteAdmin\ResoniteAdmin.csproj'

$adminBuild = Ensure-ResoniteAdminBuildOutput -DotNetPath $dotnet -ProjectPath $adminProject -RepoRoot $repoRoot

$arguments = @(
    '--dump-root',
    $resolvedEndpoint,
    '--output',
    $resolvedOutputPath,
    '--depth',
    $Depth.ToString([System.Globalization.CultureInfo]::InvariantCulture)
)

if ($IncludeComponentData) {
    $arguments += '--include-component-data'
}
else {
    $arguments += '--exclude-component-data'
}

New-Item -ItemType Directory -Force -Path $runtimeRoot, $adminRuntimeRoot | Out-Null
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
    -WorkingDirectory $repoRoot `
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

if (-not (Test-Path -LiteralPath $resolvedOutputPath -PathType Leaf)) {
    throw "Root dump output was not created: $resolvedOutputPath`nSTDOUT:`n$stdoutText`nSTDERR:`n$stderrText"
}

[pscustomobject]@{
    Endpoint              = $resolvedEndpoint
    OutputPath            = (Resolve-Path -LiteralPath $resolvedOutputPath).Path
    Depth                 = $Depth
    IncludeComponentData  = $IncludeComponentData
    AdminDllPath          = $adminBuild.DllPath
    AdminDllLastWriteTime = $adminBuild.DllLastWriteTime
}
