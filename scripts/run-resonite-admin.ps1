param(
    [Parameter(Mandatory = $true)]
    [string]$Endpoint,

    [Parameter(Mandatory = $true)]
    [string]$Dataset,

    [switch]$ListOnly
)

$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'windows-build-tools.ps1')

$repoRoot = (Split-Path -Parent $PSScriptRoot)
$projectPath = Join-Path $repoRoot 'scripts\ResoniteAdmin\ResoniteAdmin.csproj'
$dotnet = Resolve-DotNetCommandPath
$dllPath = Ensure-WindowsBuildOutput -DotNetPath $dotnet -ProjectPath $projectPath -ExpectedDllPath (Join-Path $repoRoot 'artifacts\build\windows\bin\ResoniteAdmin\Release\net10.0\ResoniteAdmin.dll')
$arguments = @($dllPath, $Endpoint, $Dataset)
if ($ListOnly) {
    $arguments += '--list-only'
}

& "$dotnet" @arguments
exit $LASTEXITCODE
