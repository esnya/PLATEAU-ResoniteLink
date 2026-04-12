param(
    [Parameter(Mandatory = $true)]
    [string]$RepoPath,

    [Parameter(Mandatory = $true)]
    [string]$HeadlessPath,

    [string]$ResoniteLinkPort,

    [string]$SessionName = 'PLATEAU Headless Test',
    [string]$SessionDescription = 'Disposable headless session for PLATEAU-ResoniteLink live tests.',
    [string]$LogPrefix = 'headless',
    [int]$StartupTimeoutSeconds = 120,
    [int]$DiscoveryTimeoutSeconds = 3,
    [string]$StatePath = ''
)

$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path -LiteralPath $RepoPath).Path
$delegatePath = Join-Path $repoRoot 'scripts\run-resonite-headless.ps1'
$discoverScript = Join-Path $PSScriptRoot 'discover-session.ps1'
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

$started = & $delegatePath `
    -RepoPath $repoRoot `
    -HeadlessPath $HeadlessPath `
    -ResoniteLinkPort $ResoniteLinkPort `
    -SessionName $SessionName `
    -SessionDescription $SessionDescription `
    -LogPrefix $LogPrefix `
    -StartupTimeoutSeconds $StartupTimeoutSeconds

$resolvedDiscoveryTimeoutSeconds = [Math]::Min([Math]::Max($DiscoveryTimeoutSeconds, 1), 30)
$announcements = @()
try {
    $announcements = @(& $discoverScript -TimeoutSeconds $resolvedDiscoveryTimeoutSeconds -MaxAnnouncements 10)
}
catch {
    $announcements = @()
}
$matchingAnnouncements = @(
    $announcements |
        Where-Object {
            $_.LinkPort -eq [int]$ResoniteLinkPort -and
            $_.SessionName -eq $SessionName
        }
)

$announcement = if ($matchingAnnouncements.Count -gt 0) {
    $matchingAnnouncements[0]
}
else {
    $stdoutLines = if (Test-Path -LiteralPath $started.StdoutLog) {
        Get-Content -LiteralPath $started.StdoutLog
    }
    else {
        @()
    }

    $sessionIdLine = $stdoutLines | Where-Object { $_ -match 'Unique Session ID:\s*(.+)$' } | Select-Object -Last 1
    $linkPortLine = $stdoutLines | Where-Object { $_ -match 'ResoniteLink Started on port:\s*([0-9]+)' } | Select-Object -Last 1
    $resolvedSessionId = if ($sessionIdLine) { ([regex]::Match($sessionIdLine, 'Unique Session ID:\s*(.+)$')).Groups[1].Value.Trim() } else { '' }
    $resolvedLinkPort = if ($linkPortLine) { [int]([regex]::Match($linkPortLine, 'ResoniteLink Started on port:\s*([0-9]+)')).Groups[1].Value } else { $started.ResoniteLinkPort }

    [pscustomobject]@{
        SessionName = $SessionName
        SessionId   = $resolvedSessionId
        LinkPort    = $resolvedLinkPort
        Discovery   = 'log-fallback'
    }
}

$resolvedStatePath = Resolve-StatePath -ConfiguredStatePath $StatePath -RuntimeRootPath $runtimeRoot
$stateDirectory = Split-Path -Parent $resolvedStatePath
if (-not [string]::IsNullOrWhiteSpace($stateDirectory)) {
    New-Item -ItemType Directory -Force -Path $stateDirectory | Out-Null
}

$result = [pscustomobject]@{
    ProcessId        = $started.ProcessId
    SessionName      = $announcement.SessionName
    SessionId        = $announcement.SessionId
    LinkPort         = $announcement.LinkPort
    Endpoint         = "ws://localhost:$($announcement.LinkPort)/"
    DiscoveryMode    = if ($announcement.PSObject.Properties.Match('Discovery').Count -gt 0) { $announcement.Discovery } else { 'udp' }
    ConfigPath       = $started.ConfigPath
    SessionRoot      = $started.SessionRoot
    StdoutLog        = $started.StdoutLog
    StderrLog        = $started.StderrLog
    DataFolder       = $started.DataFolder
    CacheFolder      = $started.CacheFolder
    LogsFolder       = $started.LogsFolder
    LauncherPath     = $started.LauncherPath
    WorkingDirectory = $started.WorkingDirectory
    WorldReadyLine   = $started.WorldReadyLine
    StatePath        = $resolvedStatePath
}

$result | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $resolvedStatePath -Encoding utf8
$result
