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
    [switch]$SkipBuild,
    [double]$MemoryLimitGiB = 16.0,
    [int]$PollSeconds = 10
)

$ErrorActionPreference = 'Stop'

function Get-LogTail {
    param(
        [string]$Path
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        return @()
    }

    return @(Get-Content -LiteralPath $Path -Tail 80)
}

function Get-LastTimestampedLine {
    param(
        [string[]]$Lines,
        [string]$Category
    )

    $pattern = "^\[[^\]]+\] \[$Category\]\["
    for ($index = $Lines.Count - 1; $index -ge 0; $index--) {
        if ($Lines[$index] -match $pattern) {
            return $Lines[$index]
        }
    }

    return $null
}

function Get-FailureReason {
    param(
        [string[]]$StdoutLines,
        [string[]]$StderrLines
    )

    $failurePatterns = @(
        "status code '502'",
        'Import failed:',
        'Unhandled exception',
        'connect failed',
        'Exception:'
    )

    foreach ($line in @($StderrLines + $StdoutLines)) {
        foreach ($pattern in $failurePatterns) {
            if ($line -like "*$pattern*") {
                return $line
            }
        }
    }

    return $null
}

$runnerPath = Join-Path $PSScriptRoot 'run-live-send.ps1'
$launch = & $runnerPath `
    -RepoPath $RepoPath `
    -ResoniteLinkPort $ResoniteLinkPort `
    -LocalSourcePath $LocalSourcePath `
    -Dataset $Dataset `
    -MeshCode $MeshCode `
    -DemTerrainMode $DemTerrainMode `
    -Connections $Connections `
    -LogPrefix $LogPrefix `
    -SkipBuild:$SkipBuild `
    -NoWait

$processId = [int]$launch.ProcessId
$stdoutLog = [string]$launch.StdoutLog
$stderrLog = [string]$launch.StderrLog
$memoryLimitBytes = [int64]($MemoryLimitGiB * 1GB)

$status = 'unknown'
$reason = $null
$exitCode = $null

while ($true) {
    $stdoutLines = Get-LogTail -Path $stdoutLog
    $stderrLines = Get-LogTail -Path $stderrLog
    $process = Get-Process -Id $processId -ErrorAction SilentlyContinue

    if ($stderrLines.Count -gt 0) {
        if ($process) {
            Stop-Process -Id $processId -Force -ErrorAction SilentlyContinue
            $process = $null
        }

        $status = 'failed'
        $reason = $stderrLines[-1]
        break
    }

    $failureReason = Get-FailureReason -StdoutLines $stdoutLines -StderrLines $stderrLines
    if ($failureReason) {
        if ($process) {
            Stop-Process -Id $processId -Force -ErrorAction SilentlyContinue
            $process = $null
        }

        $status = 'failed'
        $reason = $failureReason
        break
    }

    if (-not $process) {
        $status = 'exited'
        $reason = 'Process exited.'
        break
    }

    if ($process.WorkingSet64 -gt $memoryLimitBytes) {
        Stop-Process -Id $processId -Force -ErrorAction SilentlyContinue
        $status = 'killed_memory_cap'
        $reason = "Working set exceeded ${MemoryLimitGiB} GiB."
        break
    }

    Start-Sleep -Seconds $PollSeconds
}

$process = Get-Process -Id $processId -ErrorAction SilentlyContinue
if ($process) {
    $process.Refresh()
}

$stdoutLines = Get-LogTail -Path $stdoutLog
$stderrLines = Get-LogTail -Path $stderrLog

[pscustomobject]@{
    ProcessId      = $processId
    Status         = $status
    Reason         = $reason
    ExitCode       = if ($process) { $null } else { $exitCode }
    Dataset        = $Dataset
    MeshCode       = $MeshCode
    DemTerrainMode = $DemTerrainMode
    Connections    = $Connections
    MemoryLimitGiB = $MemoryLimitGiB
    StdoutLog      = $stdoutLog
    StderrLog      = $stderrLog
    LastImportLine = Get-LastTimestampedLine -Lines $stdoutLines -Category 'import'
    LastLiveLine   = Get-LastTimestampedLine -Lines $stdoutLines -Category 'live'
    StderrEmpty    = $stderrLines.Count -eq 0
}
