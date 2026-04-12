param(
    [Parameter(Mandatory = $true)]
    [string]$RepoPath,

    [Parameter(Mandatory = $true)]
    [string]$Endpoint,

    [Parameter(Mandatory = $true)]
    [string]$Dataset,

    [switch]$ListOnly,
    [int]$VerificationTimeoutSeconds = 20,
    [int]$PollIntervalSeconds = 2
)

$ErrorActionPreference = 'Stop'

$delegatePath = Join-Path (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..\..')).Path 'scripts\cleanup-live-send.ps1'

& $delegatePath `
    -RepoPath $RepoPath `
    -Endpoint $Endpoint `
    -Dataset $Dataset `
    -ListOnly:$ListOnly `
    -KeepLogs `
    -VerificationTimeoutSeconds $VerificationTimeoutSeconds `
    -PollIntervalSeconds $PollIntervalSeconds
exit $LASTEXITCODE
