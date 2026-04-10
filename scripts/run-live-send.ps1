param(
    [Parameter(Mandatory = $true)]
    [string]$ResoniteLinkPort,

    [string]$Dataset = '14100-yokohama-shi',
    [string]$MeshCode = '533915[3-6][0-2]',
    [string]$DemTerrainMode = 'heightmap',
    [string]$Connections = '8',
    [string]$LogPrefix = 'live-send',
    [switch]$NoWait
)

$ErrorActionPreference = 'Stop'

$repo = 'C:\Users\esnya\Documents\PLATEAU-ResoniteLink'
$stdoutLog = Join-Path $repo ("runtime\windows\resonite\{0}.stdout.log" -f $LogPrefix)
$stderrLog = Join-Path $repo ("runtime\windows\resonite\{0}.stderr.log" -f $LogPrefix)
$localSourcePath = Join-Path $repo 'runtime\windows\resonite\cache\remote\14100-yokohama-shi\53391530\14100_yokohama-shi_city_2023_citygml_1_op'
$workRoot = Join-Path $repo 'runtime\windows\resonite'
$cliDllPath = Join-Path $repo 'artifacts\build\windows\bin\Plateau.ResoniteLink.Cli\Release\net10.0\Plateau.ResoniteLink.Cli.dll'

if (Test-Path $stdoutLog) {
    Remove-Item $stdoutLog -Force
}

if (Test-Path $stderrLog) {
    Remove-Item $stderrLog -Force
}

$process = Start-Process `
    -FilePath 'C:\Program Files\dotnet\dotnet.exe' `
    -WorkingDirectory $repo `
    -ArgumentList @(
        $cliDllPath,
        'build',
        '--dataset', $Dataset,
        '--source', 'local',
        '--local-source-path', $localSourcePath,
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
    Write-Output "PID=$($process.Id)"
    Write-Output "STDOUT=$stdoutLog"
    Write-Output "STDERR=$stderrLog"
    exit 0
}

$null = $process | Wait-Process

Write-Output "EXIT=$($process.ExitCode)"
Write-Output '---STDOUT---'
if (Test-Path $stdoutLog) {
    Get-Content $stdoutLog -Tail 200
}
Write-Output '---STDERR---'
if (Test-Path $stderrLog) {
    Get-Content $stderrLog -Tail 200
}
