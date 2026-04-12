param(
    [Parameter(Mandatory = $true)]
    [string]$RepoPath,

    [Parameter(Mandatory = $true)]
    [string]$ResoniteLinkPort,

    [Parameter(Mandatory = $true)]
    [string]$LocalSourcePath,

    [Parameter(Mandatory = $true)]
    [string]$Dataset,

    [Parameter(Mandatory = $true)]
    [string]$MeshCode,
    [ValidateSet('heightmap', 'mesh')]
    [string]$DemTerrainMode = 'heightmap',
    [string]$Connections = '8',
    [string]$LogPrefix = 'live-send',
    [switch]$NoWait
)

$ErrorActionPreference = 'Stop'

$delegatePath = Join-Path (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..\..')).Path 'scripts\run-live-send.ps1'

& $delegatePath `
    -RepoPath $RepoPath `
    -ResoniteLinkPort $ResoniteLinkPort `
    -LocalSourcePath $LocalSourcePath `
    -Dataset $Dataset `
    -MeshCode $MeshCode `
    -DemTerrainMode $DemTerrainMode `
    -Connections $Connections `
    -LogPrefix $LogPrefix `
    -NoWait:$NoWait
exit $LASTEXITCODE
