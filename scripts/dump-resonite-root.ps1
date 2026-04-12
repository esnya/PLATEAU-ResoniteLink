param(
    [Parameter(Mandatory = $true)]
    [string]$Endpoint,

    [string]$RepoPath = (Split-Path -Parent $PSScriptRoot),
    [string]$OutputPath = '',
    [int]$Depth = -1,
    [bool]$IncludeComponentData = $true
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

function Resolve-AdminDllPath {
    param(
        [string]$RepoRoot,
        [string]$Configuration = 'Release'
    )

    $candidateDlls = @()
    foreach ($hostOs in @('windows', 'linux', 'macos')) {
        $candidatePath = Join-Path $RepoRoot ("artifacts\build\{0}\bin\ResoniteAdmin\{1}\net10.0\ResoniteAdmin.dll" -f $hostOs, $Configuration)
        if (Test-Path -LiteralPath $candidatePath -PathType Leaf) {
            $candidateDlls += (Get-Item -LiteralPath $candidatePath)
        }
    }

    if ($candidateDlls.Count -eq 0) {
        throw "ResoniteAdmin build output was not found under '$(Join-Path $RepoRoot 'artifacts\build')'."
    }

    return $candidateDlls[0].FullName
}

function Ensure-AdminBuildOutput {
    param(
        [string]$DotNetPath,
        [string]$ProjectPath,
        [string]$RepoRoot
    )

    $existingDllPath = $null
    try {
        $existingDllPath = Resolve-AdminDllPath -RepoRoot $RepoRoot
    }
    catch {
        $existingDllPath = $null
    }

    $buildOutput = & "$DotNetPath" build $ProjectPath -c Release -p:RepositoryHostOs=windows 2>&1
    $buildExitCode = $LASTEXITCODE
    $buildOutput | Out-Host

    if ($buildExitCode -eq 0) {
        return Resolve-AdminDllPath -RepoRoot $RepoRoot
    }

    if (-not [string]::IsNullOrWhiteSpace($existingDllPath)) {
        Write-Warning "ResoniteAdmin build failed; continuing with the existing build output at '$existingDllPath'."
        return $existingDllPath
    }

    $outputText = ($buildOutput | ForEach-Object { $_.ToString() }) -join [Environment]::NewLine
    throw "ResoniteAdmin build failed and no existing build output is available. ExitCode=$buildExitCode`n$outputText"
}

$dotnet = Get-DotNetCommandPath
$runtimeRoot = Join-Path $RepoPath 'runtime\windows\resonite\root-dumps'
$adminRuntimeRoot = Join-Path $RepoPath 'runtime\windows\resonite'
$adminProject = Join-Path $RepoPath 'scripts\ResoniteAdmin\ResoniteAdmin.csproj'

$adminDll = Ensure-AdminBuildOutput -DotNetPath $dotnet -ProjectPath $adminProject -RepoRoot $RepoPath

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    New-Item -ItemType Directory -Force -Path $runtimeRoot | Out-Null
    $timestamp = Get-Date -Format 'yyyyMMdd-HHmmss'
    $OutputPath = Join-Path $runtimeRoot ("root-dump-{0}.json" -f $timestamp)
}

$arguments = @(
    '--dump-root',
    $Endpoint,
    '--output',
    $OutputPath,
    '--depth',
    $Depth.ToString([System.Globalization.CultureInfo]::InvariantCulture)
)

if ($IncludeComponentData) {
    $arguments += '--include-component-data'
}
else {
    $arguments += '--exclude-component-data'
}

New-Item -ItemType Directory -Force -Path $adminRuntimeRoot | Out-Null
$stdoutPath = Join-Path $adminRuntimeRoot 'resonite-admin-dump.stdout.log'
$stderrPath = Join-Path $adminRuntimeRoot 'resonite-admin-dump.stderr.log'
foreach ($path in @($stdoutPath, $stderrPath)) {
    if (Test-Path -LiteralPath $path) {
        Remove-Item -LiteralPath $path -Force
    }
}

$process = Start-Process `
    -FilePath $dotnet `
    -ArgumentList @($adminDll) + $arguments `
    -WorkingDirectory $RepoPath `
    -Wait `
    -PassThru `
    -RedirectStandardOutput $stdoutPath `
    -RedirectStandardError $stderrPath

$stdoutText = if (Test-Path -LiteralPath $stdoutPath) { Get-Content -LiteralPath $stdoutPath -Raw } else { '' }
$stderrText = if (Test-Path -LiteralPath $stderrPath) { Get-Content -LiteralPath $stderrPath -Raw } else { '' }
if (-not [string]::IsNullOrWhiteSpace($stdoutText)) {
    $stdoutText | Out-Host
}

if ($process.ExitCode -ne 0) {
    throw "ResoniteAdmin dump-root failed. ExitCode=$($process.ExitCode)`nSTDOUT:`n$stdoutText`nSTDERR:`n$stderrText"
}

if (-not (Test-Path -LiteralPath $OutputPath -PathType Leaf)) {
    throw "Root dump output was not created: $OutputPath`nSTDOUT:`n$stdoutText`nSTDERR:`n$stderrText"
}

[pscustomobject]@{
    Endpoint             = $Endpoint
    OutputPath           = (Resolve-Path -LiteralPath $OutputPath).Path
    Depth                = $Depth
    IncludeComponentData = $IncludeComponentData
    AdminDllPath         = $adminDll
    AdminDllLastWriteTime = (Get-Item -LiteralPath $adminDll).LastWriteTime
}
