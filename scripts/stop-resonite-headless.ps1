param(
    [Parameter(Mandatory = $true)]
    [int]$ProcessId,

    [int]$ShutdownTimeoutSeconds = 20
)

$ErrorActionPreference = 'Stop'

$process = Get-Process -Id $ProcessId -ErrorAction SilentlyContinue
if ($null -eq $process) {
    [pscustomobject]@{
        ProcessId  = $ProcessId
        WasRunning = $false
        HasExited  = $true
    }
    exit 0
}

Stop-Process -Id $ProcessId -ErrorAction SilentlyContinue
$deadline = (Get-Date).AddSeconds($ShutdownTimeoutSeconds)

while ((Get-Date) -lt $deadline) {
    if (-not (Get-Process -Id $ProcessId -ErrorAction SilentlyContinue)) {
        [pscustomobject]@{
            ProcessId  = $ProcessId
            WasRunning = $true
            HasExited  = $true
        }
        exit 0
    }

    Start-Sleep -Milliseconds 500
}

Stop-Process -Id $ProcessId -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 1

if (Get-Process -Id $ProcessId -ErrorAction SilentlyContinue) {
    throw "Headless process $ProcessId is still running after targeted shutdown."
}

[pscustomobject]@{
    ProcessId  = $ProcessId
    WasRunning = $true
    HasExited  = $true
    Forced     = $true
}
