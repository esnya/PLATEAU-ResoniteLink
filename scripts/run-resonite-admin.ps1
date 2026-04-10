param(
    [Parameter(Mandatory = $true)]
    [string]$Endpoint,

    [Parameter(Mandatory = $true)]
    [string]$Dataset,

    [switch]$ListOnly
)

$ErrorActionPreference = 'Stop'

$dllPath = 'C:\Users\esnya\Documents\PLATEAU-ResoniteLink\artifacts\build\windows\bin\ResoniteAdmin\Release\net10.0\ResoniteAdmin.dll'
$arguments = @($dllPath, $Endpoint, $Dataset)
if ($ListOnly) {
    $arguments += '--list-only'
}

& 'C:\Program Files\dotnet\dotnet.exe' @arguments
exit $LASTEXITCODE
