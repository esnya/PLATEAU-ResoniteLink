param(
    [Parameter(Mandatory = $true)]
    [string]$HeadlessPath,

    [string]$ResoniteLinkPort,

    [string]$SessionName = 'PLATEAU Headless Test',
    [string]$SessionDescription = 'Disposable headless session for PLATEAU-ResoniteLink live tests.',
    [string]$LogPrefix = 'headless',
    [string]$RepoPath = '',
    [int]$StartupTimeoutSeconds = 120,
    [ValidateSet('Private', 'LAN', 'Contacts', 'ContactsPlus', 'RegisteredUsers', 'Anyone')]
    [string]$AccessLevel = 'Anyone',
    [string]$WorldPresetName = 'Grid'
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

function Resolve-HeadlessLauncher {
    param(
        [string]$ConfiguredHeadlessPath
    )

    $resolvedPath = (Resolve-Path -LiteralPath $ConfiguredHeadlessPath).Path
    if ((Split-Path -Leaf $resolvedPath) -ieq 'Resonite' -and (Test-Path -LiteralPath (Join-Path $resolvedPath 'Headless') -PathType Container)) {
        $resolvedPath = (Resolve-Path -LiteralPath (Join-Path $resolvedPath 'Headless')).Path
    }

    if (Test-Path -LiteralPath $resolvedPath -PathType Container) {
        foreach ($candidateName in @('Resonite.exe', 'Resonite.dll')) {
            $candidatePath = Join-Path $resolvedPath $candidateName
            if (Test-Path -LiteralPath $candidatePath -PathType Leaf) {
                return [pscustomobject]@{
                    LauncherPath        = $candidatePath
                    WorkingDirectory    = $resolvedPath
                    RequiresDotNetHost  = $candidateName.EndsWith('.dll', [System.StringComparison]::OrdinalIgnoreCase)
                }
            }
        }

        throw "No Resonite launcher was found under '$resolvedPath'. Expected Resonite.exe or Resonite.dll."
    }

    $workingDirectory = Split-Path -Parent $resolvedPath
    return [pscustomobject]@{
        LauncherPath        = $resolvedPath
        WorkingDirectory    = $workingDirectory
        RequiresDotNetHost  = $resolvedPath.EndsWith('.dll', [System.StringComparison]::OrdinalIgnoreCase)
    }
}

function Get-LogTail {
    param(
        [string]$Path,
        [int]$LineCount = 20
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        return ''
    }

    return ((Get-Content -LiteralPath $Path -Tail $LineCount) -join [Environment]::NewLine)
}

function Get-FreeTcpPort {
    $listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, 0)
    try {
        $listener.Start()
        return ([System.Net.IPEndPoint]$listener.LocalEndpoint).Port
    }
    finally {
        $listener.Stop()
    }
}

$repoRoot = Resolve-RepositoryRoot -ConfiguredRepoPath $RepoPath
$launcher = Resolve-HeadlessLauncher -ConfiguredHeadlessPath $HeadlessPath
$dotNetExePath = Resolve-DotNetExePath
$runtimeRoot = Join-Path $repoRoot 'runtime\windows\headless'
$sessionRoot = Join-Path $runtimeRoot $LogPrefix
$stdoutLog = Join-Path $runtimeRoot ("{0}.stdout.log" -f $LogPrefix)
$stderrLog = Join-Path $runtimeRoot ("{0}.stderr.log" -f $LogPrefix)
$configPath = Join-Path $sessionRoot 'Config.json'
$headlessDataRoot = Join-Path $sessionRoot 'Data'
$headlessCacheRoot = Join-Path $sessionRoot 'Cache'
$headlessLogsRoot = Join-Path $sessionRoot 'Logs'
$parsedResoniteLinkPort = 0

if ([string]::IsNullOrWhiteSpace($ResoniteLinkPort)) {
    $parsedResoniteLinkPort = Get-FreeTcpPort
}
elseif (-not [int]::TryParse($ResoniteLinkPort, [ref]$parsedResoniteLinkPort) -or $parsedResoniteLinkPort -lt 1 -or $parsedResoniteLinkPort -gt 65535) {
    throw "The value '$ResoniteLinkPort' is not a valid TCP port."
}

New-Item -ItemType Directory -Force -Path $runtimeRoot, $sessionRoot, $headlessDataRoot, $headlessCacheRoot, $headlessLogsRoot | Out-Null

foreach ($path in @($stdoutLog, $stderrLog, $configPath)) {
    if (Test-Path -LiteralPath $path) {
        Remove-Item -LiteralPath $path -Force
    }
}

$config = [ordered]@{
    comment = 'Disposable headless session for PLATEAU-ResoniteLink live tests.'
    dataFolder = $headlessDataRoot
    cacheFolder = $headlessCacheRoot
    logsFolder = $headlessLogsRoot
    startWorlds = @(
        [ordered]@{
            sessionName = $SessionName
            description = $SessionDescription
            accessLevel = $AccessLevel
            hideFromPublicListing = $true
            loadWorldPresetName = $WorldPresetName
            enableResoniteLink = $true
            forceResoniteLinkPort = $parsedResoniteLinkPort
            saveOnExit = $false
            autoSleep = $true
        }
    )
}

$config | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $configPath -Encoding utf8

$processArguments = if ($launcher.RequiresDotNetHost) {
    @($launcher.LauncherPath, '-HeadlessConfig', $configPath)
}
else {
    @('-HeadlessConfig', $configPath)
}

$filePath = if ($launcher.RequiresDotNetHost) { $dotNetExePath } else { $launcher.LauncherPath }
$process = Start-Process `
    -FilePath $filePath `
    -WorkingDirectory $launcher.WorkingDirectory `
    -ArgumentList $processArguments `
    -PassThru `
    -RedirectStandardOutput $stdoutLog `
    -RedirectStandardError $stderrLog

$deadline = (Get-Date).AddSeconds($StartupTimeoutSeconds)
$worldReadyLine = $null

while ((Get-Date) -lt $deadline) {
    $process.Refresh()
    if ($process.HasExited) {
        $stdoutTail = Get-LogTail -Path $stdoutLog
        $stderrTail = Get-LogTail -Path $stderrLog
        throw "Headless process $($process.Id) exited before readiness. ExitCode=$($process.ExitCode)`nSTDOUT:`n$stdoutTail`nSTDERR:`n$stderrTail"
    }

    if (Test-Path -LiteralPath $stdoutLog) {
        $match = Select-String -LiteralPath $stdoutLog -Pattern 'World running' | Select-Object -Last 1
        if ($null -ne $match) {
            $worldReadyLine = $match.Line
            break
        }
    }

    Start-Sleep -Milliseconds 500
}

if ($null -eq $worldReadyLine) {
    if (Get-Process -Id $process.Id -ErrorAction SilentlyContinue) {
        Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
    }

    $stdoutTail = Get-LogTail -Path $stdoutLog
    $stderrTail = Get-LogTail -Path $stderrLog
    throw "Headless process $($process.Id) did not report 'World Running' within ${StartupTimeoutSeconds}s.`nSTDOUT:`n$stdoutTail`nSTDERR:`n$stderrTail"
}

[pscustomobject]@{
    ProcessId        = $process.Id
    ResoniteLinkPort = $parsedResoniteLinkPort
    SessionName      = $SessionName
    SessionRoot      = $sessionRoot
    ConfigPath       = $configPath
    StdoutLog        = $stdoutLog
    StderrLog        = $stderrLog
    DataFolder       = $headlessDataRoot
    CacheFolder      = $headlessCacheRoot
    LogsFolder       = $headlessLogsRoot
    LauncherPath     = $launcher.LauncherPath
    WorkingDirectory = $launcher.WorkingDirectory
    WorldReadyLine   = $worldReadyLine
}
