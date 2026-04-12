param(
    [Parameter(Mandatory = $true)]
    [string]$ResoniteLinkPort,

    [Parameter(Mandatory = $true)]
    [string]$LocalSourcePath,

    [string]$Dataset = '14100-yokohama-shi',
    [string]$MeshCode = '533915[3-6][0-2]',
    [ValidateSet('heightmap', 'mesh')]
    [string]$DemTerrainMode = 'heightmap',
    [string]$Connections = '8',
    [string]$LogPrefix = 'live-send',
    [string]$RepoPath = '',
    [switch]$NoWait
)

$ErrorActionPreference = 'Stop'

function Resolve-RepositoryRoot {
    param(
        [string]$ConfiguredRepoPath
    )

    if (-not [string]::IsNullOrWhiteSpace($ConfiguredRepoPath)) {
        return (Resolve-Path -LiteralPath $ConfiguredRepoPath).Path
    }

    $scriptRoot = if ($PSScriptRoot) {
        $PSScriptRoot
    }
    else {
        Split-Path -Parent $MyInvocation.MyCommand.Path
    }

    return (Resolve-Path -LiteralPath (Join-Path $scriptRoot '..')).Path
}

function Resolve-DotNetExePath {
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
            return $candidate
        }
    }

    throw 'Unable to locate dotnet.exe. Set DOTNET_EXE, DOTNET_HOST_PATH, or DOTNET_ROOT, or ensure dotnet is available on PATH.'
}

$repoRoot = Resolve-RepositoryRoot -ConfiguredRepoPath $RepoPath
$dotNetExePath = Resolve-DotNetExePath
$runtimeRoot = Join-Path $repoRoot 'runtime\windows\resonite'
$stdoutLog = Join-Path $runtimeRoot ("{0}.stdout.log" -f $LogPrefix)
$stderrLog = Join-Path $runtimeRoot ("{0}.stderr.log" -f $LogPrefix)
$workRoot = $runtimeRoot
$cliDllPath = Join-Path $repoRoot 'artifacts\build\windows\bin\Plateau.ResoniteLink.Cli\Release\net10.0\Plateau.ResoniteLink.Cli.dll'

if (-not (Test-Path $cliDllPath)) {
    & $dotNetExePath build (Join-Path $repoRoot 'src\Plateau.ResoniteLink.Cli\Plateau.ResoniteLink.Cli.csproj') -c Release | Out-Host
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
    -FilePath $dotNetExePath `
    -WorkingDirectory $repoRoot `
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
        ProcessId           = $process.Id
        Dataset             = $Dataset
        MeshCode            = $MeshCode
        DemTerrainMode      = $DemTerrainMode
        StdoutLog           = $stdoutLog
        StderrLog           = $stderrLog
        CliDllPath          = $cliDllPath
        CliDllLastWriteTime = (Get-Item $cliDllPath).LastWriteTime
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
    CliDllPath          = $cliDllPath
    CliDllLastWriteTime = (Get-Item $cliDllPath).LastWriteTime
}
