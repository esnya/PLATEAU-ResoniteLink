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

$helperPath = Join-Path $PSScriptRoot 'windows-build-tools.ps1'
. $helperPath

$repoRoot = Resolve-RepoRoot -RepoPath $RepoPath
$discoverScript = Join-Path $PSScriptRoot 'discover-session.ps1'
$runtimeRoot = Resolve-HeadlessRuntimeRoot -RepoRoot $repoRoot

function Resolve-HeadlessLauncher {
    param(
        [string]$ConfiguredHeadlessPath
    )

    $resolvedPath = (Resolve-Path -LiteralPath $ConfiguredHeadlessPath).Path
    if ((Split-Path -Leaf $resolvedPath) -ieq 'Resonite' -and (Test-Path -LiteralPath (Join-Path $resolvedPath 'Headless') -PathType Container)) {
        $resolvedPath = (Resolve-Path -LiteralPath (Join-Path $resolvedPath 'Headless')).Path
    }

    if (Test-Path -LiteralPath $resolvedPath -PathType Container) {
        foreach ($candidateName in @('Resonite.exe', 'Resonite.dll')) {
            $candidatePath = Join-Path $resolvedPath $candidateName
            if (Test-Path -LiteralPath $candidatePath -PathType Leaf) {
                return [pscustomobject]@{
                    LauncherPath       = $candidatePath
                    WorkingDirectory   = $resolvedPath
                    RequiresDotNetHost = $candidateName.EndsWith('.dll', [System.StringComparison]::OrdinalIgnoreCase)
                }
            }
        }

        throw "No Resonite launcher was found under '$resolvedPath'. Expected Resonite.exe or Resonite.dll."
    }

    $workingDirectory = Split-Path -Parent $resolvedPath
    return [pscustomobject]@{
        LauncherPath       = $resolvedPath
        WorkingDirectory   = $workingDirectory
        RequiresDotNetHost = $resolvedPath.EndsWith('.dll', [System.StringComparison]::OrdinalIgnoreCase)
    }
}

function Get-LogTail {
    param(
        [string]$Path,
        [int]$LineCount = 20
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        return ''
    }

    return ((Get-Content -LiteralPath $Path -Tail $LineCount) -join [Environment]::NewLine)
}

$resolvedStatePath = Resolve-StatePath -ConfiguredStatePath $StatePath -RuntimeRootPath $runtimeRoot
$launcher = Resolve-HeadlessLauncher -ConfiguredHeadlessPath $HeadlessPath
$dotNetCommandPath = Resolve-DotNetCommandPath
$sessionRoot = Join-Path $runtimeRoot $LogPrefix
$stdoutLog = Join-Path $runtimeRoot ("{0}.stdout.log" -f $LogPrefix)
$stderrLog = Join-Path $runtimeRoot ("{0}.stderr.log" -f $LogPrefix)
$configPath = Join-Path $sessionRoot 'Config.json'
$headlessDataRoot = Join-Path $sessionRoot 'Data'
$headlessCacheRoot = Join-Path $sessionRoot 'Cache'
$headlessLogsRoot = Join-Path $sessionRoot 'Logs'
$parsedResoniteLinkPort = $null

if (-not [string]::IsNullOrWhiteSpace($ResoniteLinkPort) -and (-not [int]::TryParse($ResoniteLinkPort, [ref]$parsedResoniteLinkPort) -or $parsedResoniteLinkPort -lt 1 -or $parsedResoniteLinkPort -gt 65535)) {
    throw "The value '$ResoniteLinkPort' is not a valid TCP port."
}

New-Item -ItemType Directory -Force -Path $runtimeRoot, $sessionRoot, $headlessDataRoot, $headlessCacheRoot, $headlessLogsRoot | Out-Null

foreach ($path in @($stdoutLog, $stderrLog, $configPath)) {
    if (Test-Path -LiteralPath $path) {
        Remove-Item -LiteralPath $path -Force
    }
}

$startWorld = [ordered]@{
    sessionName = $SessionName
    description = $SessionDescription
    accessLevel = 'Anyone'
    hideFromPublicListing = $true
    loadWorldPresetName = 'Grid'
    enableResoniteLink = $true
    saveOnExit = $false
    autoSleep = $true
}

if ($null -ne $parsedResoniteLinkPort) {
    $startWorld.forceResoniteLinkPort = $parsedResoniteLinkPort
}

$config = [ordered]@{
    comment = 'Disposable headless session for PLATEAU-ResoniteLink live tests.'
    dataFolder = $headlessDataRoot
    cacheFolder = $headlessCacheRoot
    logsFolder = $headlessLogsRoot
    startWorlds = @($startWorld)
}

$config | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $configPath -Encoding utf8

$processArguments = if ($launcher.RequiresDotNetHost) {
    @($launcher.LauncherPath, '-HeadlessConfig', $configPath)
}
else {
    @('-HeadlessConfig', $configPath)
}

$filePath = if ($launcher.RequiresDotNetHost) { $dotNetCommandPath } else { $launcher.LauncherPath }
$process = Start-Process `
    -FilePath $filePath `
    -WorkingDirectory $launcher.WorkingDirectory `
    -ArgumentList $processArguments `
    -PassThru `
    -RedirectStandardOutput $stdoutLog `
    -RedirectStandardError $stderrLog

$deadline = (Get-Date).AddSeconds($StartupTimeoutSeconds)
$worldReadyLine = $null

while ((Get-Date) -lt $deadline) {
    $process.Refresh()
    if ($process.HasExited) {
        $stdoutTail = Get-LogTail -Path $stdoutLog
        $stderrTail = Get-LogTail -Path $stderrLog
        throw "Headless process $($process.Id) exited before readiness. ExitCode=$($process.ExitCode)`nSTDOUT:`n$stdoutTail`nSTDERR:`n$stderrTail"
    }

    if (Test-Path -LiteralPath $stdoutLog) {
        $match = Select-String -LiteralPath $stdoutLog -Pattern 'World running' | Select-Object -Last 1
        if ($null -ne $match) {
            $worldReadyLine = $match.Line
            break
        }
    }

    Start-Sleep -Milliseconds 500
}

if ($null -eq $worldReadyLine) {
    if (Get-Process -Id $process.Id -ErrorAction SilentlyContinue) {
        Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
    }

    $stdoutTail = Get-LogTail -Path $stdoutLog
    $stderrTail = Get-LogTail -Path $stderrLog
    throw "Headless process $($process.Id) did not report 'World Running' within ${StartupTimeoutSeconds}s.`nSTDOUT:`n$stdoutTail`nSTDERR:`n$stderrTail"
}

$resolvedResoniteLinkPort = $parsedResoniteLinkPort
if (Test-Path -LiteralPath $stdoutLog) {
    $linkPortMatch = Select-String -LiteralPath $stdoutLog -Pattern 'ResoniteLink Started on port:\s*([0-9]+)' | Select-Object -Last 1
    if ($null -ne $linkPortMatch) {
        $resolvedResoniteLinkPort = [int]$linkPortMatch.Matches[0].Groups[1].Value
    }
}

if ($null -eq $resolvedResoniteLinkPort) {
    if (Get-Process -Id $process.Id -ErrorAction SilentlyContinue) {
        Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
    }

    $stdoutTail = Get-LogTail -Path $stdoutLog
    $stderrTail = Get-LogTail -Path $stderrLog
    throw "Headless process $($process.Id) became ready but did not report a ResoniteLink port.`nSTDOUT:`n$stdoutTail`nSTDERR:`n$stderrTail"
}

if (($null -ne $parsedResoniteLinkPort) -and ($resolvedResoniteLinkPort -ne $parsedResoniteLinkPort)) {
    if (Get-Process -Id $process.Id -ErrorAction SilentlyContinue) {
        Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
    }

    $stdoutTail = Get-LogTail -Path $stdoutLog
    $stderrTail = Get-LogTail -Path $stderrLog
    throw "Headless process $($process.Id) reported ResoniteLink port $resolvedResoniteLinkPort, which does not match requested port $parsedResoniteLinkPort.`nSTDOUT:`n$stdoutTail`nSTDERR:`n$stderrTail"
}

$started = [pscustomobject]@{
    ProcessId        = $process.Id
    ResoniteLinkPort = $resolvedResoniteLinkPort
    SessionName      = $SessionName
    SessionRoot      = $sessionRoot
    ConfigPath       = $configPath
    StdoutLog        = $stdoutLog
    StderrLog        = $stderrLog
    DataFolder       = $headlessDataRoot
    CacheFolder      = $headlessCacheRoot
    LogsFolder       = $headlessLogsRoot
    LauncherPath     = $launcher.LauncherPath
    WorkingDirectory = $launcher.WorkingDirectory
    WorldReadyLine   = $worldReadyLine
}

$resolvedDiscoveryTimeoutSeconds = [Math]::Min([Math]::Max($DiscoveryTimeoutSeconds, 1), 30)
$announcements = @()
try {
    $announcements = @(& $discoverScript -TimeoutSeconds $resolvedDiscoveryTimeoutSeconds -MaxAnnouncements 10)
}
catch {
    $announcements = @()
}
$expectedLinkPort = [int]$started.ResoniteLinkPort
$matchingAnnouncements = @(
    $announcements |
        Where-Object {
            $_.LinkPort -eq $expectedLinkPort -and
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
    $resolvedLinkPort = if ($linkPortLine) { [int]([regex]::Match($linkPortLine, 'ResoniteLink Started on port:\s*([0-9]+)')).Groups[1].Value } else { $expectedLinkPort }

    [pscustomobject]@{
        SessionName = $SessionName
        SessionId   = $resolvedSessionId
        LinkPort    = $resolvedLinkPort
        Discovery   = 'log-fallback'
    }
}

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
