param(
    [Parameter(Mandatory = $true)]
    [string]$RepoPath,

    [int]$ProcessId,
    [string]$StatePath = ''
)

$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path -LiteralPath $RepoPath).Path
$delegatePath = Join-Path $repoRoot 'scripts\stop-resonite-headless.ps1'
$runtimeRoot = Join-Path $repoRoot 'runtime\windows\headless'

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

function Resolve-TrackedProcessId {
    param(
        [string]$ResolvedStatePath
    )

    if (-not (Test-Path -LiteralPath $ResolvedStatePath -PathType Leaf)) {
        throw "No tracked headless session state file exists at '$ResolvedStatePath'."
    }

    $state = Get-Content -LiteralPath $ResolvedStatePath -Raw | ConvertFrom-Json
    if ($null -eq $state.ProcessId) {
        throw "Tracked headless session state '$ResolvedStatePath' does not contain ProcessId."
    }

    return [int]$state.ProcessId
}

$resolvedStatePath = Resolve-StatePath -ConfiguredStatePath $StatePath -RuntimeRootPath $runtimeRoot
$resolvedProcessId = if ($PSBoundParameters.ContainsKey('ProcessId')) {
    $ProcessId
}
else {
    Resolve-TrackedProcessId -ResolvedStatePath $resolvedStatePath
}

$result = & $delegatePath -ProcessId $resolvedProcessId
if (Test-Path -LiteralPath $resolvedStatePath -PathType Leaf) {
    Remove-Item -LiteralPath $resolvedStatePath -Force
}

$result
exit $LASTEXITCODE
