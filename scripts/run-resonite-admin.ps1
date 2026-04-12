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
$adminBuild = Ensure-ResoniteAdminBuildOutput -DotNetPath $dotnet -ProjectPath $projectPath -RepoRoot $repoRoot
$launcherPath = if (-not [string]::IsNullOrWhiteSpace($adminBuild.ExePath)) { $adminBuild.ExePath } else { $dotnet }
$arguments = if (-not [string]::IsNullOrWhiteSpace($adminBuild.ExePath)) {
    @($Endpoint, $Dataset)
}
else {
    @($adminBuild.DllPath, $Endpoint, $Dataset)
}
if ($ListOnly) {
    $arguments += '--list-only'
}

& "$launcherPath" @arguments
exit $LASTEXITCODE
