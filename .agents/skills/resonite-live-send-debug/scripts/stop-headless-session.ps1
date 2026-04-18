param(
    [Parameter(Mandatory = $true)]
    [string]$RepoPath,

    [int]$ProcessId,
    [string]$StatePath = ''
)

$ErrorActionPreference = 'Stop'

$helperPath = Join-Path $PSScriptRoot 'windows-build-tools.ps1'
. $helperPath

$repoRoot = Resolve-RepoRoot -RepoPath $RepoPath
$runtimeRoot = Resolve-HeadlessRuntimeRoot -RepoRoot $repoRoot

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
$usedTrackedState = -not $PSBoundParameters.ContainsKey('ProcessId')
$resolvedProcessId = if (-not $usedTrackedState) {
    $ProcessId
}
else {
    Resolve-TrackedProcessId -ResolvedStatePath $resolvedStatePath
}

$result = $null
$process = Get-Process -Id $resolvedProcessId -ErrorAction SilentlyContinue
if ($null -eq $process) {
    $result = [pscustomobject]@{
        ProcessId  = $resolvedProcessId
        WasRunning = $false
        HasExited  = $true
    }
}
else {
    Stop-Process -Id $resolvedProcessId -ErrorAction SilentlyContinue
    $deadline = (Get-Date).AddSeconds(20)

    while ((Get-Date) -lt $deadline) {
        if (-not (Get-Process -Id $resolvedProcessId -ErrorAction SilentlyContinue)) {
            $result = [pscustomobject]@{
                ProcessId  = $resolvedProcessId
                WasRunning = $true
                HasExited  = $true
            }
            break
        }

        Start-Sleep -Milliseconds 500
    }

    if ($null -eq $result) {
        Stop-Process -Id $resolvedProcessId -Force -ErrorAction SilentlyContinue
        Start-Sleep -Seconds 1

        if (Get-Process -Id $resolvedProcessId -ErrorAction SilentlyContinue) {
            throw "Headless process $resolvedProcessId is still running after targeted shutdown."
        }

        $result = [pscustomobject]@{
            ProcessId  = $resolvedProcessId
            WasRunning = $true
            HasExited  = $true
            Forced     = $true
        }
    }
}

if ($usedTrackedState -and (Test-Path -LiteralPath $resolvedStatePath -PathType Leaf)) {
    Remove-Item -LiteralPath $resolvedStatePath -Force
}

$result
exit $LASTEXITCODE
