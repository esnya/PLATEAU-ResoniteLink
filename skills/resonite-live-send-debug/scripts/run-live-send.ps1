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
    [switch]$NoWait
)

$ErrorActionPreference = 'Stop'

$runtimeRoot = Join-Path $RepoPath 'runtime\windows\resonite'
$stdoutLog = Join-Path $runtimeRoot ("{0}.stdout.log" -f $LogPrefix)
$stderrLog = Join-Path $runtimeRoot ("{0}.stderr.log" -f $LogPrefix)
$workRoot = $runtimeRoot
$cliDllPath = Join-Path $RepoPath 'artifacts\build\windows\bin\Plateau.ResoniteLink.Cli\Release\net10.0\Plateau.ResoniteLink.Cli.dll'

if (-not (Test-Path $cliDllPath)) {
    & 'C:\Program Files\dotnet\dotnet.exe' build (Join-Path $RepoPath 'src\Plateau.ResoniteLink.Cli\Plateau.ResoniteLink.Cli.csproj') -c Release | Out-Host
    if (-not (Test-Path $cliDllPath)) {
        throw "CLI build output not found: $cliDllPath"
    }
}

foreach ($path in @($stdoutLog, $stderrLog)) {
    if (Test-Path $path) {
        Remove-Item $path -Force
    }
}

$process = Start-Process `
    -FilePath 'C:\Program Files\dotnet\dotnet.exe' `
    -WorkingDirectory $RepoPath `
    -ArgumentList @(
        $cliDllPath,
        'build',
        '--dataset', $Dataset,
        '--source', 'local',
        '--local-source-path', $LocalSourcePath,
        '--work-root', $workRoot,
        '--dem-terrain-mode', $DemTerrainMode,
        '--resonitelink-port', $ResoniteLinkPort,
        '--mesh-code', $MeshCode,
        '--resonitelink-connections', $Connections
    ) `
    -PassThru `
    -RedirectStandardOutput $stdoutLog `
    -RedirectStandardError $stderrLog

if ($NoWait) {
    [pscustomobject]@{
        ProcessId      = $process.Id
        Dataset        = $Dataset
        MeshCode       = $MeshCode
        DemTerrainMode = $DemTerrainMode
        StdoutLog      = $stdoutLog
        StderrLog      = $stderrLog
        CliDllPath     = $cliDllPath
        CliDllLastWriteTime = (Get-Item $cliDllPath).LastWriteTime
    }
    exit 0
}

$null = $process | Wait-Process
$process.Refresh()

[pscustomobject]@{
    ProcessId      = $process.Id
    ExitCode       = $process.ExitCode
    Dataset        = $Dataset
    MeshCode       = $MeshCode
    DemTerrainMode = $DemTerrainMode
    StdoutLog      = $stdoutLog
    StderrLog      = $stderrLog
    CliDllPath     = $cliDllPath
    CliDllLastWriteTime = (Get-Item $cliDllPath).LastWriteTime
}
