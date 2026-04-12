param(
    [Parameter(Mandatory = $true)]
    [string]$Endpoint,

    [Parameter(Mandatory = $true)]
    [string]$Dataset,

    [switch]$ListOnly
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

    foreach ($hostOs in @('windows', 'linux', 'macos')) {
        $candidatePath = Join-Path $RepoRoot ("artifacts\build\{0}\bin\ResoniteAdmin\{1}\net10.0\ResoniteAdmin.dll" -f $hostOs, $Configuration)
        if (Test-Path -LiteralPath $candidatePath -PathType Leaf) {
            return $candidatePath
        }
    }

    throw "ResoniteAdmin build output was not found under '$(Join-Path $RepoRoot 'artifacts\build')'."
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

$repoRoot = (Split-Path -Parent $PSScriptRoot)
$projectPath = Join-Path $repoRoot 'scripts\ResoniteAdmin\ResoniteAdmin.csproj'
$dotnet = Get-DotNetCommandPath
$dllPath = Ensure-AdminBuildOutput -DotNetPath $dotnet -ProjectPath $projectPath -RepoRoot $repoRoot
$arguments = @($dllPath, $Endpoint, $Dataset)
if ($ListOnly) {
    $arguments += '--list-only'
}

& "$dotnet" @arguments
exit $LASTEXITCODE
