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
$sessionToolProject = Join-Path (Split-Path -Parent $PSScriptRoot) 'tools\ResoniteSessionTool\ResoniteSessionTool.csproj'

function Invoke-SessionTool {
    param(
        [string[]]$Arguments
    )

    $launchSpec = Get-BuiltDotNetToolLaunchSpec -DotNetPath $dotnet -ToolBuild $sessionToolBuild -Arguments $Arguments
    $launchArguments = @($launchSpec.Arguments)
    $output = & $launchSpec.FilePath @launchArguments 2>&1
    if ($LASTEXITCODE -ne 0) {
        $renderedOutput = ($output | Out-String)
        throw "ResoniteSessionTool failed. ExitCode=$LASTEXITCODE`nOutput:`n$renderedOutput"
    }

    return $output
}

function Get-DatasetRootTargets {
    param(
        [string]$DumpPath,
        [string]$DatasetRootName
    )

    if (-not (Test-Path -LiteralPath $DumpPath -PathType Leaf)) {
        throw "Dataset root dump was not created: $DumpPath"
    }

    $dump = Get-Content -LiteralPath $DumpPath -Raw | ConvertFrom-Json -Depth 100
    return @(
        $dump.Root.Children |
            Where-Object {
                $_.Name.Value -eq $DatasetRootName
            }
    )
}

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

$sessionToolBuild = Ensure-ResoniteSessionToolBuildOutput -DotNetPath $dotnet -ProjectPath $sessionToolProject -RepoRoot $repoRoot

if ($stoppedProcessIds.Count -gt 0) {
    Write-Output ("Stopped stale sender PID(s): {0}" -f ($stoppedProcessIds -join ', '))
}

Write-Output ("SessionToolDllPath={0}" -f $sessionToolBuild.DllPath)
Write-Output ("SessionToolDllLastWriteTime={0:o}" -f $sessionToolBuild.DllLastWriteTime)

$datasetRootName = "PLATEAU $Dataset"
New-Item -ItemType Directory -Force -Path $runtimeRoot | Out-Null
$verificationDumpPath = Join-Path $runtimeRoot 'cleanup-dataset-root-scan.json'

if (-not $ListOnly) {
    $initialDumpOutput = Invoke-SessionTool -Arguments @(
        '--dump-root',
        $Endpoint,
        '--output',
        $verificationDumpPath,
        '--depth',
        '1',
        '--exclude-component-data'
    )
    $initialDumpOutput | Out-Host

    $targets = Get-DatasetRootTargets -DumpPath $verificationDumpPath -DatasetRootName $datasetRootName
    Write-Output ("Found {0} dataset root slot(s) named '{1}'." -f $targets.Count, $datasetRootName)

    foreach ($target in $targets) {
        if ([string]::IsNullOrWhiteSpace($target.ID)) {
            Write-Output "Skipping unnamed-id slot match."
            continue
        }

        Write-Output "Warning: removing this slot destroys the matching dataset root in the current live Resonite session."
        Write-Output ("Removing slot '{0}' ({1})." -f $target.ID, $target.Name.Value)
        $removeOutput = Invoke-SessionTool -Arguments @(
            '--remove-slot',
            $Endpoint,
            [string]$target.ID
        )
        $removeOutput | Out-Host
    }
}

$verification = @()
$deadline = (Get-Date).AddSeconds($VerificationTimeoutSeconds)

do {
    $verification = Invoke-SessionTool -Arguments @(
        '--dump-root',
        $Endpoint,
        '--output',
        $verificationDumpPath,
        '--depth',
        '1',
        '--exclude-component-data'
    )
    $verification | Out-Host

    $targets = Get-DatasetRootTargets -DumpPath $verificationDumpPath -DatasetRootName $datasetRootName
    Write-Output ("Found {0} dataset root slot(s) named '{1}'." -f $targets.Count, $datasetRootName)
    if (($targets.Count -eq 0) -and $ListOnly) {
        $dump = Get-Content -LiteralPath $verificationDumpPath -Raw | ConvertFrom-Json -Depth 100
        foreach ($child in @($dump.Root.Children)) {
            Write-Output ("Root child: {0} :: {1}" -f $child.ID, $child.Name.Value)
        }
    }

    if ($targets.Count -eq 0) {
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

if ($targets.Count -eq 0) {
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
