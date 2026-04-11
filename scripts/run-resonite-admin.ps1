param(
    [Parameter(Mandatory = $true)]
    [string]$Endpoint,

    [Parameter(Mandatory = $true)]
    [string]$Dataset,

    [switch]$ListOnly
)

$ErrorActionPreference = 'Stop'

function Get-DotNetCommandPath {
    if ($env:DOTNET_EXE -and (Test-Path $env:DOTNET_EXE)) {
        return (Get-Item $env:DOTNET_EXE).FullName
    }

    if ($env:DOTNET_HOST_PATH -and (Test-Path $env:DOTNET_HOST_PATH)) {
        return (Get-Item $env:DOTNET_HOST_PATH).FullName
    }

    if ($env:DOTNET_ROOT) {
        foreach ($candidate in @(
            (Join-Path $env:DOTNET_ROOT 'dotnet.exe'),
            (Join-Path $env:DOTNET_ROOT 'dotnet')
        )) {
            if (Test-Path $candidate) {
                return (Get-Item $candidate).FullName
            }
        }
    }

    return (Get-Command dotnet -ErrorAction Stop).Source
}

$repoRoot = (Split-Path -Parent $PSScriptRoot)
$dllPath = Join-Path $repoRoot 'artifacts\build\windows\bin\ResoniteAdmin\Release\net10.0\ResoniteAdmin.dll'
$dotnet = Get-DotNetCommandPath
$arguments = @($dllPath, $Endpoint, $Dataset)
if ($ListOnly) {
    $arguments += '--list-only'
}

& $dotnet @arguments
exit $LASTEXITCODE
