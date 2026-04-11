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

$runtimeRoot = Join-Path $RepoPath 'runtime\windows\resonite'
$adminProject = Join-Path $RepoPath 'scripts\ResoniteAdmin\ResoniteAdmin.csproj'
$adminDll = Join-Path $RepoPath 'artifacts\build\windows\bin\ResoniteAdmin\Release\net10.0\ResoniteAdmin.dll'

$stoppedProcessIds = @()
if (-not $ListOnly) {
    Write-Warning "This cleanup removes live dataset roots from the current Resonite session and can destroy experiment results."
    Get-CimInstance Win32_Process -Filter "Name = 'dotnet.exe'" |
        Where-Object { $_.CommandLine -like '*Plateau.ResoniteLink.Cli*' -and $_.CommandLine -like "*$RepoPath*" } |
        ForEach-Object {
            $stoppedProcessIds += $_.ProcessId
            Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue
        }
}

if (-not (Test-Path $adminDll)) {
    & 'C:\Program Files\dotnet\dotnet.exe' build $adminProject -c Release | Out-Host
    if (-not (Test-Path $adminDll)) {
        throw "ResoniteAdmin build output not found: $adminDll"
    }
}

if ($stoppedProcessIds.Count -gt 0) {
    Write-Output ("Stopped stale sender PID(s): {0}" -f ($stoppedProcessIds -join ', '))
}

Write-Output ("AdminDllPath={0}" -f $adminDll)
Write-Output ("AdminDllLastWriteTime={0:o}" -f (Get-Item $adminDll).LastWriteTime)

if (-not $ListOnly) {
    & 'C:\Program Files\dotnet\dotnet.exe' $adminDll $Endpoint $Dataset | Out-Host
}

$verification = @()
$deadline = (Get-Date).AddSeconds($VerificationTimeoutSeconds)

do {
    $verification = & 'C:\Program Files\dotnet\dotnet.exe' $adminDll $Endpoint $Dataset --list-only
    $verification | Out-Host

    if ($verification -match "Found 0 dataset root slot\(s\)") {
        break
    }

    if ($ListOnly -or (Get-Date) -ge $deadline) {
        break
    }

    Start-Sleep -Seconds $PollIntervalSeconds
}
while ($true)

if ($ListOnly) {
    return
}

if ($verification -match "Found 0 dataset root slot\(s\)") {
    foreach ($path in @(
        (Join-Path $runtimeRoot '.generated-assets'),
        (Join-Path $runtimeRoot 'resonite-live-asset-state.json'),
        (Join-Path $runtimeRoot 'resonite-live-asset-state.json.778de27fa819415a8310f8d02019bc12.tmp')
    )) {
        if (Test-Path $path) {
            Remove-Item $path -Recurse -Force
        }
    }

    return
}

throw "Dataset root cleanup did not converge to zero roots for '$Dataset'."
