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
    [string]$Connections = '8',
    [int]$ObserveSeconds = 30,
    [int]$DiscoveryTimeoutSeconds = 20,
    [string[]]$Modes = @('heightmap', 'mesh', 'heightmap'),
    [string]$HeadlessPath,
    [string]$HeadlessSessionName = 'PLATEAU Headless Test',
    [string]$HeadlessSessionDescription = 'Disposable headless session for PLATEAU-ResoniteLink live tests.',
    [string]$HeadlessLogPrefix = 'headless',
    [int]$HeadlessStartupTimeoutSeconds = 120,
    [string]$ExpectedSessionName,
    [string]$ExpectedSessionId
)

$ErrorActionPreference = 'Stop'

$skillRoot = Split-Path -Parent $PSScriptRoot
$discoverScript = Join-Path $PSScriptRoot 'discover-session.ps1'
$cleanupScript = Join-Path $PSScriptRoot 'cleanup-session.ps1'
$runScript = Join-Path $PSScriptRoot 'run-live-send.ps1'
$startHeadlessScript = Join-Path $PSScriptRoot 'start-headless-session.ps1'
$stopHeadlessScript = Join-Path $PSScriptRoot 'stop-headless-session.ps1'
$endpoint = "ws://localhost:$ResoniteLinkPort/"
$results = New-Object System.Collections.Generic.List[object]
$startedHeadless = $null

try {
    if (-not [string]::IsNullOrWhiteSpace($HeadlessPath)) {
        $startedHeadless = & $startHeadlessScript `
            -RepoPath $RepoPath `
            -HeadlessPath $HeadlessPath `
            -ResoniteLinkPort $ResoniteLinkPort `
            -SessionName $HeadlessSessionName `
            -SessionDescription $HeadlessSessionDescription `
            -LogPrefix $HeadlessLogPrefix `
            -StartupTimeoutSeconds $HeadlessStartupTimeoutSeconds

        if (-not $ExpectedSessionId) {
            $ExpectedSessionId = $startedHeadless.SessionId
        }

        if (-not $ExpectedSessionName) {
            $ExpectedSessionName = $startedHeadless.SessionName
        }
    }

    for ($index = 0; $index -lt $Modes.Length; $index++) {
        $mode = $Modes[$index]
        $logPrefix = "send.$MeshCode.$mode.$($index + 1)"

        $announcements = @(& $discoverScript -TimeoutSeconds $DiscoveryTimeoutSeconds -MaxAnnouncements 5)
        $matchingAnnouncements = @(
            $announcements |
                Where-Object { $_.LinkPort -eq [int]$ResoniteLinkPort }
        )

        if ($matchingAnnouncements.Count -eq 0) {
            throw "No ResoniteLink announcement matched requested port $ResoniteLinkPort."
        }

        $announcement = $null
        if ($ExpectedSessionId) {
            $announcement = @(
                $matchingAnnouncements |
                    Where-Object { $_.SessionId -eq $ExpectedSessionId } |
                    Select-Object -First 1
            )[0]
            if ($null -eq $announcement) {
                throw "No discovered session id matched expected '$ExpectedSessionId' on port $ResoniteLinkPort."
            }
        }
        elseif ($ExpectedSessionName) {
            $announcement = @(
                $matchingAnnouncements |
                    Where-Object { $_.SessionName -eq $ExpectedSessionName } |
                    Select-Object -First 1
            )[0]
            if ($null -eq $announcement) {
                throw "No discovered session name matched expected '$ExpectedSessionName' on port $ResoniteLinkPort."
            }
        }
        else {
            $uniqueSessions = @(
                $matchingAnnouncements |
                    Group-Object SessionId, SessionName, LinkPort
            )
            if ($uniqueSessions.Count -ne 1) {
                throw "Multiple ResoniteLink sessions were discovered on port $ResoniteLinkPort. Pass -ExpectedSessionId or -ExpectedSessionName explicitly."
            }

            $announcement = $matchingAnnouncements[0]
        }

        if ($announcement.LinkPort -ne [int]$ResoniteLinkPort) {
            throw "Discovered listener port $($announcement.LinkPort) does not match requested port $ResoniteLinkPort."
        }
        if ($ExpectedSessionName -and $announcement.SessionName -ne $ExpectedSessionName) {
            throw "Discovered session name '$($announcement.SessionName)' does not match expected '$ExpectedSessionName'."
        }
        if ($ExpectedSessionId -and $announcement.SessionId -ne $ExpectedSessionId) {
            throw "Discovered session id '$($announcement.SessionId)' does not match expected '$ExpectedSessionId'."
        }

        & $cleanupScript -RepoPath $RepoPath -Endpoint $endpoint -Dataset $Dataset

        $run = & $runScript `
            -RepoPath $RepoPath `
            -ResoniteLinkPort $ResoniteLinkPort `
            -LocalSourcePath $LocalSourcePath `
            -Dataset $Dataset `
            -MeshCode $MeshCode `
            -DemTerrainMode $mode `
            -Connections $Connections `
            -LogPrefix $logPrefix `
            -NoWait

        Start-Sleep -Seconds $ObserveSeconds

        $observationStartedAt = Get-Date
        $process = Get-Process -Id $run.ProcessId -ErrorAction SilentlyContinue
        $stdoutTail = @()
        $stderrTail = @()
        $stdoutFull = @()

        if (Test-Path $run.StdoutLog) {
            $stdoutFull = Get-Content $run.StdoutLog
            $stdoutTail = $stdoutFull | Select-Object -Last 20
        }

        if (Test-Path $run.StderrLog) {
            $stderrTail = Get-Content $run.StderrLog -Tail 20
        }

        $observationEndedAt = Get-Date
        $wasInterrupted = $false

        if ($null -ne $process) {
            $wasInterrupted = $true
            Stop-Process -Id $run.ProcessId -Force -ErrorAction SilentlyContinue
            Start-Sleep -Seconds 1
            if (Get-Process -Id $run.ProcessId -ErrorAction SilentlyContinue) {
                throw "Targeted PID $($run.ProcessId) is still alive after Stop-Process."
            }
        }

        & $cleanupScript -RepoPath $RepoPath -Endpoint $endpoint -Dataset $Dataset

        $results.Add([pscustomobject]@{
            Mode            = $mode
            LogPrefix       = $logPrefix
            ProcessId       = $run.ProcessId
            WasStillRunning = ($null -ne $process)
            SessionName     = $announcement.SessionName
            SessionId       = $announcement.SessionId
            LinkPort        = $announcement.LinkPort
            StdoutLog       = $run.StdoutLog
            StderrLog       = $run.StderrLog
            CliDllPath      = $run.CliDllPath
            CliDllLastWriteTime = $run.CliDllLastWriteTime
            HeadlessProcessId = if ($null -ne $startedHeadless) { $startedHeadless.ProcessId } else { $null }
            HeadlessStdoutLog = if ($null -ne $startedHeadless) { $startedHeadless.StdoutLog } else { $null }
            HeadlessStderrLog = if ($null -ne $startedHeadless) { $startedHeadless.StderrLog } else { $null }
            HeadlessConfigPath = if ($null -ne $startedHeadless) { $startedHeadless.ConfigPath } else { $null }
            ObservationStartedAt = $observationStartedAt
            ObservationEndedAt   = $observationEndedAt
            WasInterrupted  = $wasInterrupted
            StructuralValidity = if ($wasInterrupted) { 'provisional-root-only-cleanup' } else { 'provisional-natural-exit-root-only-cleanup' }
            LastImportLine  = ($stdoutFull | Where-Object { $_ -match '\[import\]' } | Select-Object -Last 1)
            LastLiveLine    = ($stdoutFull | Where-Object { $_ -match '\[live\]' } | Select-Object -Last 1)
            StdoutTail      = ($stdoutTail -join [Environment]::NewLine)
            StderrTail      = ($stderrTail -join [Environment]::NewLine)
        })
    }
}
finally {
    if ($null -ne $startedHeadless) {
        & $stopHeadlessScript -RepoPath $RepoPath -ProcessId $startedHeadless.ProcessId | Out-Null
    }
}

$results
