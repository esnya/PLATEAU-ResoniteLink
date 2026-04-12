param(
    [Parameter(Mandatory = $true)]
    [string]$Endpoint,

    [string]$Dataset = '14100-yokohama-shi',
    [string]$RepoPath = (Split-Path -Parent $PSScriptRoot),
    [switch]$ListOnly,
    [switch]$KeepLogs,
    [int]$VerificationTimeoutSeconds = 20,
    [int]$PollIntervalSeconds = 2
)

$ErrorActionPreference = 'Stop'

function Get-DotNetCommandPath {
    $candidates = @()

    if (-not [string]::IsNullOrWhiteSpace($env:DOTNET_EXE)) {
        $candidates += $env:DOTNET_EXE
    }

    if (-not [string]::IsNullOrWhiteSpace($env:DOTNET_HOST_PATH)) {
        $candidates += $env:DOTNET_HOST_PATH
    }

    if (-not [string]::IsNullOrWhiteSpace($env:DOTNET_ROOT)) {
        $candidates += (Join-Path $env:DOTNET_ROOT 'dotnet.exe')
    }

    $command = Get-Command -Name dotnet.exe -CommandType Application -ErrorAction SilentlyContinue
    if ($command) {
        return $command.Source
    }

    $command = Get-Command -Name dotnet -CommandType Application -ErrorAction SilentlyContinue
    if ($command) {
        return $command.Source
    }

    if (-not [string]::IsNullOrWhiteSpace($env:ProgramFiles)) {
        $candidates += (Join-Path $env:ProgramFiles 'dotnet\dotnet.exe')
    }

    foreach ($candidate in $candidates) {
        if (-not [string]::IsNullOrWhiteSpace($candidate) -and (Test-Path -LiteralPath $candidate)) {
            return (Get-Item -LiteralPath $candidate).FullName
        }
    }

    throw 'Unable to locate dotnet.exe. Set DOTNET_EXE, DOTNET_HOST_PATH, or DOTNET_ROOT, or ensure dotnet is available on PATH.'
}

$dotnet = Get-DotNetCommandPath
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
    $buildOutput = & "$dotnet" build $adminProject -c Release 2>&1
    $buildOutput | Out-Host
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
    $cleanupOutput = & "$dotnet" $adminDll $Endpoint $Dataset 2>&1
    $cleanupOutput | Out-Host
}

$verification = @()
$deadline = (Get-Date).AddSeconds($VerificationTimeoutSeconds)

do {
    $verification = & "$dotnet" $adminDll $Endpoint $Dataset --list-only
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

    if (-not $KeepLogs) {
        Get-ChildItem $runtimeRoot -Filter '*.stdout.log' -Force -ErrorAction SilentlyContinue | Remove-Item -Force
        Get-ChildItem $runtimeRoot -Filter '*.stderr.log' -Force -ErrorAction SilentlyContinue | Remove-Item -Force
    }

    return
}

throw "Dataset root cleanup did not converge to zero roots for '$Dataset'."
