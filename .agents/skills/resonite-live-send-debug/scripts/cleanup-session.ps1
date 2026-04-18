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

. (Join-Path $PSScriptRoot 'windows-build-tools.ps1')

$repoRoot = Resolve-RepoRoot -RepoPath $RepoPath
$dotnet = Resolve-DotNetCommandPath
$runtimeRoot = Resolve-ResoniteRuntimeRoot -RepoRoot $repoRoot
$adminProject = Join-Path (Split-Path -Parent $PSScriptRoot) 'tools\ResoniteAdmin\ResoniteAdmin.csproj'

$stoppedProcessIds = @()
if (-not $ListOnly) {
    Write-Warning "This cleanup removes live dataset roots from the current Resonite session and can destroy experiment results."
    Get-CimInstance Win32_Process |
        Where-Object {
            $isSenderProcess = $_.Name -eq 'dotnet.exe' -or $_.Name -eq 'Plateau.ResoniteLink.Cli.exe'
            $isRepoProcess = $_.CommandLine -like '*Plateau.ResoniteLink.Cli*' -and $_.CommandLine -like "*$repoRoot*"
            $isSenderProcess -and $isRepoProcess
        } |
        ForEach-Object {
            $stoppedProcessIds += $_.ProcessId
            Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue
        }
}

$adminBuild = Ensure-ResoniteAdminBuildOutput -DotNetPath $dotnet -ProjectPath $adminProject -RepoRoot $repoRoot

if ($stoppedProcessIds.Count -gt 0) {
    Write-Output ("Stopped stale sender PID(s): {0}" -f ($stoppedProcessIds -join ', '))
}

Write-Output ("AdminDllPath={0}" -f $adminBuild.DllPath)
Write-Output ("AdminDllLastWriteTime={0:o}" -f $adminBuild.DllLastWriteTime)

if (-not $ListOnly) {
    $cleanupOutput = if (-not [string]::IsNullOrWhiteSpace($adminBuild.ExePath)) {
        & $adminBuild.ExePath $Endpoint $Dataset 2>&1
    }
    else {
        & "$dotnet" $adminBuild.DllPath $Endpoint $Dataset 2>&1
    }
    $cleanupOutput | Out-Host
}

$verification = @()
$deadline = (Get-Date).AddSeconds($VerificationTimeoutSeconds)

do {
    $verification = if (-not [string]::IsNullOrWhiteSpace($adminBuild.ExePath)) {
        & $adminBuild.ExePath $Endpoint $Dataset --list-only
    }
    else {
        & "$dotnet" $adminBuild.DllPath $Endpoint $Dataset --list-only
    }
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
        if (Test-Path -LiteralPath $path) {
            Remove-Item -LiteralPath $path -Recurse -Force
        }
    }

    return
}

throw "Dataset root cleanup did not converge to zero roots for '$Dataset'."
