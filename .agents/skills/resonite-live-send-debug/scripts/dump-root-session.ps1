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

$helperPath = Join-Path $PSScriptRoot 'windows-build-tools.ps1'
. $helperPath

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

$repoRoot = Resolve-RepoRoot -RepoPath $RepoPath
$headlessRuntimeRoot = Resolve-HeadlessRuntimeRoot -RepoRoot $repoRoot
$resolvedStatePath = Resolve-StatePath -ConfiguredStatePath $StatePath -RuntimeRootPath $headlessRuntimeRoot
$resolvedEndpoint = Resolve-Endpoint -ConfiguredEndpoint $Endpoint -ResolvedStatePath $resolvedStatePath
$resolvedOutputPath = Resolve-OutputPath -ConfiguredOutputPath $OutputPath -RepoRootPath $repoRoot -ResolvedLabel $Label

$dotnet = Resolve-DotNetCommandPath
$adminRuntimeRoot = Resolve-ResoniteRuntimeRoot -RepoRoot $repoRoot
$runtimeRoot = Join-Path $adminRuntimeRoot 'root-dumps'
$sessionToolProject = Join-Path (Split-Path -Parent $PSScriptRoot) 'tools\ResoniteSessionTool\ResoniteSessionTool.csproj'

$sessionToolBuild = Ensure-ResoniteSessionToolBuildOutput -DotNetPath $dotnet -ProjectPath $sessionToolProject -RepoRoot $repoRoot

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
$stdoutPath = Join-Path $adminRuntimeRoot 'resonite-session-tool-dump.stdout.log'
$stderrPath = Join-Path $adminRuntimeRoot 'resonite-session-tool-dump.stderr.log'
foreach ($path in @($stdoutPath, $stderrPath)) {
    if (Test-Path -LiteralPath $path) {
        Remove-Item -LiteralPath $path -Force
    }
}

$launchSpec = Get-BuiltDotNetToolLaunchSpec -DotNetPath $dotnet -ToolBuild $sessionToolBuild -Arguments $arguments
$process = Start-Process `
    -FilePath $launchSpec.FilePath `
    -ArgumentList $launchSpec.Arguments `
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
    throw "ResoniteSessionTool dump-root failed. ExitCode=$($process.ExitCode)`nSTDOUT:`n$stdoutText`nSTDERR:`n$stderrText"
}

if (-not (Test-Path -LiteralPath $resolvedOutputPath -PathType Leaf)) {
    throw "Root dump output was not created: $resolvedOutputPath`nSTDOUT:`n$stdoutText`nSTDERR:`n$stderrText"
}

[pscustomobject]@{
    Endpoint                    = $resolvedEndpoint
    OutputPath                  = (Resolve-Path -LiteralPath $resolvedOutputPath).Path
    Depth                       = $Depth
    IncludeComponentData        = $IncludeComponentData
    SessionToolDllPath          = $sessionToolBuild.DllPath
    SessionToolDllLastWriteTime = $sessionToolBuild.DllLastWriteTime
}
