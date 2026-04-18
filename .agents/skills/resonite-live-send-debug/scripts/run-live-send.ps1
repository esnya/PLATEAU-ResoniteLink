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
    [switch]$NoWait,
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'

$helperPath = Join-Path $PSScriptRoot 'windows-build-tools.ps1'
. $helperPath

$repoRoot = Resolve-RepoRoot -RepoPath $RepoPath
$dotNetCommandPath = Resolve-DotNetCommandPath
$runtimeRoot = Resolve-ResoniteRuntimeRoot -RepoRoot $repoRoot
$stdoutLog = Join-Path $runtimeRoot ("{0}.stdout.log" -f $LogPrefix)
$stderrLog = Join-Path $runtimeRoot ("{0}.stderr.log" -f $LogPrefix)
$workRoot = $runtimeRoot
$cliProjectPath = Join-Path $repoRoot 'src\Plateau.ResoniteLink.Cli\Plateau.ResoniteLink.Cli.csproj'
$cliBuild = Ensure-LiveSendCliBuildOutput `
    -DotNetPath $dotNetCommandPath `
    -ProjectPath $cliProjectPath `
    -RepoRoot $repoRoot `
    -SkipBuild:$SkipBuild

foreach ($path in @($stdoutLog, $stderrLog)) {
    if (Test-Path -LiteralPath $path) {
        Remove-Item -LiteralPath $path -Force
    }
}

$process = Start-Process `
    -FilePath $dotNetCommandPath `
    -WorkingDirectory $repoRoot `
    -ArgumentList @(
        $cliBuild.DllPath,
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
        ProcessId           = $process.Id
        Dataset             = $Dataset
        MeshCode            = $MeshCode
        DemTerrainMode      = $DemTerrainMode
        StdoutLog           = $stdoutLog
        StderrLog           = $stderrLog
        CliDllPath          = $cliBuild.DllPath
        CliDllLastWriteTime = $cliBuild.DllLastWriteTime
    }
    exit 0
}

$null = $process | Wait-Process
$process.Refresh()

[pscustomobject]@{
    ProcessId           = $process.Id
    ExitCode            = $process.ExitCode
    Dataset             = $Dataset
    MeshCode            = $MeshCode
    DemTerrainMode      = $DemTerrainMode
    StdoutLog           = $stdoutLog
    StderrLog           = $stderrLog
    CliDllPath          = $cliBuild.DllPath
    CliDllLastWriteTime = $cliBuild.DllLastWriteTime
}
