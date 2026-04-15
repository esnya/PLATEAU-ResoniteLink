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
$delegatePath = Join-Path $repoRoot 'scripts\dump-resonite-root.ps1'

& $delegatePath `
    -RepoPath $repoRoot `
    -Endpoint $resolvedEndpoint `
    -OutputPath $resolvedOutputPath `
    -Depth $Depth `
    -IncludeComponentData:$IncludeComponentData
exit $LASTEXITCODE
