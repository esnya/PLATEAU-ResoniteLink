param(
    [Parameter(Mandatory = $true)]
    [string]$RepoPath,

    [string]$HeadlessPath = '',
    [string]$StatePath = '',
    [string]$Dataset = 'plateau-20202-matsumoto-shi-2020',
    [string]$BaseMeshCode = '54372778',
    [string]$AppendMeshCode = '54372788',
    [string]$ResoniteLinkPort = '19001',
    [string]$LocalSourcePath = '',
    [string]$Connections = '1',
    [string]$HeadlessSessionName = 'Matsumoto Base Append Heightmap 19001',
    [string]$HeadlessSessionDescription = 'Disposable headless session for Matsumoto base/append heightmap validation on port 19001.',
    [string]$HeadlessLogPrefix = 'matsumoto-base-append-heightmap-19001',
    [int]$DiscoveryTimeoutSeconds = 20,
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'

$discoverScript = Join-Path $PSScriptRoot 'discover-session.ps1'
$startHeadlessScript = Join-Path $PSScriptRoot 'start-headless-session.ps1'
$stopHeadlessScript = Join-Path $PSScriptRoot 'stop-headless-session.ps1'
$cleanupScript = Join-Path $PSScriptRoot 'cleanup-session.ps1'
$runLiveSendScript = Join-Path $PSScriptRoot 'run-live-send.ps1'
$dumpRootScript = Join-Path $PSScriptRoot 'dump-root-session.ps1'

$repoRoot = (Resolve-Path -LiteralPath $RepoPath).Path

function Resolve-TrackedStatePath {
    param(
        [string]$ConfiguredStatePath,
        [string]$ResolvedRepoRoot
    )

    if (-not [string]::IsNullOrWhiteSpace($ConfiguredStatePath)) {
        return $ConfiguredStatePath
    }

    return (Join-Path $ResolvedRepoRoot 'runtime\windows\headless\active-session.json')
}

function Resolve-DefaultLocalSourcePath {
    param(
        [string]$ResolvedRepoRoot,
        [string]$DatasetName
    )

    $candidate = Join-Path $ResolvedRepoRoot ("local\{0}\source-archive.zip" -f $DatasetName)
    if (-not (Test-Path -LiteralPath $candidate -PathType Leaf)) {
        throw "Default Matsumoto local source archive was not found: $candidate"
    }

    return $candidate
}

function Resolve-EndpointFromState {
    param(
        [string]$ResolvedStatePath
    )

    if (-not (Test-Path -LiteralPath $ResolvedStatePath -PathType Leaf)) {
        return $null
    }

    $state = Get-Content -LiteralPath $ResolvedStatePath -Raw | ConvertFrom-Json
    if ([string]::IsNullOrWhiteSpace($state.Endpoint)) {
        return $null
    }

    return [string]$state.Endpoint
}

function Resolve-Announcement {
    param(
        [string]$DiscoverScriptPath,
        [string]$RequestedPort,
        [int]$TimeoutSeconds,
        [string]$ExpectedSessionId,
        [string]$ExpectedSessionName
    )

    $announcements = @(& $DiscoverScriptPath -TimeoutSeconds $TimeoutSeconds -MaxAnnouncements 10)
    $matchingAnnouncements = @(
        $announcements |
            Where-Object { $_.LinkPort -eq [int]$RequestedPort }
    )

    if ($matchingAnnouncements.Count -eq 0) {
        throw "No ResoniteLink announcement matched requested port $RequestedPort."
    }

    if (-not [string]::IsNullOrWhiteSpace($ExpectedSessionId)) {
        $announcement = @(
            $matchingAnnouncements |
                Where-Object { $_.SessionId -eq $ExpectedSessionId } |
                Select-Object -First 1
        )[0]
        if ($null -eq $announcement) {
            throw "No discovered session id matched expected '$ExpectedSessionId' on port $RequestedPort."
        }

        return $announcement
    }

    if (-not [string]::IsNullOrWhiteSpace($ExpectedSessionName)) {
        $announcement = @(
            $matchingAnnouncements |
                Where-Object { $_.SessionName -eq $ExpectedSessionName } |
                Select-Object -First 1
        )[0]
        if ($null -eq $announcement) {
            throw "No discovered session name matched expected '$ExpectedSessionName' on port $RequestedPort."
        }

        return $announcement
    }

    $uniqueSessions = @(
        $matchingAnnouncements |
            Group-Object SessionId, SessionName, LinkPort
    )
    if ($uniqueSessions.Count -ne 1) {
        throw "Multiple ResoniteLink sessions were discovered on port $RequestedPort. Pass -HeadlessPath or track a session state explicitly."
    }

    return $matchingAnnouncements[0]
}

$resolvedStatePath = Resolve-TrackedStatePath -ConfiguredStatePath $StatePath -ResolvedRepoRoot $repoRoot
$resolvedLocalSourcePath = if ([string]::IsNullOrWhiteSpace($LocalSourcePath)) {
    Resolve-DefaultLocalSourcePath -ResolvedRepoRoot $repoRoot -DatasetName $Dataset
}
else {
    (Resolve-Path -LiteralPath $LocalSourcePath).Path
}
$startedHeadless = $null

try {
    if (-not [string]::IsNullOrWhiteSpace($HeadlessPath)) {
        $startedHeadless = & $startHeadlessScript `
            -RepoPath $repoRoot `
            -HeadlessPath $HeadlessPath `
            -ResoniteLinkPort $ResoniteLinkPort `
            -SessionName $HeadlessSessionName `
            -SessionDescription $HeadlessSessionDescription `
            -LogPrefix $HeadlessLogPrefix `
            -StatePath $resolvedStatePath
    }

    $trackedEndpoint = Resolve-EndpointFromState -ResolvedStatePath $resolvedStatePath
    $trackedState = if (Test-Path -LiteralPath $resolvedStatePath -PathType Leaf) {
        Get-Content -LiteralPath $resolvedStatePath -Raw | ConvertFrom-Json
    }
    else {
        $null
    }
    $announcement = Resolve-Announcement `
        -DiscoverScriptPath $discoverScript `
        -RequestedPort $ResoniteLinkPort `
        -TimeoutSeconds $DiscoveryTimeoutSeconds `
        -ExpectedSessionId $(if ($trackedState) { [string]$trackedState.SessionId } else { '' }) `
        -ExpectedSessionName $(if ($trackedState) { [string]$trackedState.SessionName } else { '' })

    $endpoint = if (-not [string]::IsNullOrWhiteSpace($trackedEndpoint)) {
        $trackedEndpoint
    }
    else {
        "ws://localhost:$ResoniteLinkPort/"
    }

    & $cleanupScript -RepoPath $repoRoot -Endpoint $endpoint -Dataset $Dataset

    $baselineDump = & $dumpRootScript `
        -RepoPath $repoRoot `
        -Endpoint $endpoint `
        -Label 'matsumoto-baseappend-baseline'

    $baseRun = & $runLiveSendScript `
        -RepoPath $repoRoot `
        -ResoniteLinkPort $ResoniteLinkPort `
        -LocalSourcePath $resolvedLocalSourcePath `
        -Dataset $Dataset `
        -MeshCode $BaseMeshCode `
        -DemTerrainMode 'heightmap' `
        -Connections $Connections `
        -LogPrefix 'matsumoto-base-heightmap-19001' `
        -SkipBuild:$SkipBuild

    if ($baseRun.ExitCode -ne 0) {
        throw "Base live send failed. ExitCode=$($baseRun.ExitCode) StdoutLog=$($baseRun.StdoutLog) StderrLog=$($baseRun.StderrLog)"
    }

    $afterBaseDump = & $dumpRootScript `
        -RepoPath $repoRoot `
        -Endpoint $endpoint `
        -Label 'matsumoto-base-heightmap-after-send'

    $appendRun = & $runLiveSendScript `
        -RepoPath $repoRoot `
        -ResoniteLinkPort $ResoniteLinkPort `
        -LocalSourcePath $resolvedLocalSourcePath `
        -Dataset $Dataset `
        -MeshCode $AppendMeshCode `
        -DemTerrainMode 'heightmap' `
        -Connections $Connections `
        -LogPrefix 'matsumoto-append-heightmap-19001' `
        -SkipBuild:$SkipBuild

    if ($appendRun.ExitCode -ne 0) {
        throw "Append live send failed. ExitCode=$($appendRun.ExitCode) StdoutLog=$($appendRun.StdoutLog) StderrLog=$($appendRun.StderrLog)"
    }

    $afterAppendDump = & $dumpRootScript `
        -RepoPath $repoRoot `
        -Endpoint $endpoint `
        -Label 'matsumoto-append-heightmap-after-send'

    [pscustomobject]@{
        Dataset = $Dataset
        LocalSourcePath = $resolvedLocalSourcePath
        Endpoint = $endpoint
        SessionName = $announcement.SessionName
        SessionId = $announcement.SessionId
        LinkPort = $announcement.LinkPort
        Mode = 'heightmap'
        BaseMeshCode = $BaseMeshCode
        AppendMeshCode = $AppendMeshCode
        BaselineDumpPath = $baselineDump.OutputPath
        AfterBaseDumpPath = $afterBaseDump.OutputPath
        AfterAppendDumpPath = $afterAppendDump.OutputPath
        BaseStdoutLog = $baseRun.StdoutLog
        BaseStderrLog = $baseRun.StderrLog
        AppendStdoutLog = $appendRun.StdoutLog
        AppendStderrLog = $appendRun.StderrLog
        HeadlessProcessId = if ($null -ne $startedHeadless) { $startedHeadless.ProcessId } else { $null }
        HeadlessConfigPath = if ($null -ne $startedHeadless) { $startedHeadless.ConfigPath } else { $null }
        StructuralValidity = 'checked-base-then-append-without-intermediate-cleanup'
    }
}
finally {
    if ($null -ne $startedHeadless) {
        & $stopHeadlessScript -RepoPath $repoRoot -ProcessId $startedHeadless.ProcessId | Out-Null
    }
}
