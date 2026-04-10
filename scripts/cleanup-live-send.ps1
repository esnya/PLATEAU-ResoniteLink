param()

$ErrorActionPreference = 'Stop'

$repo = 'C:\Users\esnya\Documents\PLATEAU-ResoniteLink'
$runtimeRoot = Join-Path $repo 'runtime\windows\resonite'

Get-CimInstance Win32_Process -Filter "Name = 'dotnet.exe'" |
    Where-Object { $_.CommandLine -like '*Plateau.ResoniteLink.Cli*' } |
    ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }

$pathsToDelete = @(
    (Join-Path $runtimeRoot '.generated-assets'),
    (Join-Path $runtimeRoot 'resonite-live-asset-state.json'),
    (Join-Path $runtimeRoot 'resonite-live-asset-state.json.778de27fa819415a8310f8d02019bc12.tmp'),
    (Join-Path $runtimeRoot 'live-send.stdout.log'),
    (Join-Path $runtimeRoot 'live-send.stderr.log')
)

foreach ($path in $pathsToDelete) {
    if (Test-Path $path) {
        Remove-Item $path -Recurse -Force
    }
}

Get-ChildItem $runtimeRoot -Force |
    Select-Object Name, Length, LastWriteTime |
    Format-Table -AutoSize
