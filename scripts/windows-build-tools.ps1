function Resolve-DotNetCommandPath {
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

function Ensure-WindowsBuildOutput {
    param(
        [string]$DotNetPath,
        [string]$ProjectPath,
        [string]$ExpectedDllPath
    )

    $buildOutput = & "$DotNetPath" build $ProjectPath -c Release -p:RepositoryHostOs=windows 2>&1
    $buildExitCode = $LASTEXITCODE
    $buildOutput | Out-Host

    if ($buildExitCode -ne 0) {
        $outputText = ($buildOutput | ForEach-Object { $_.ToString() }) -join [Environment]::NewLine
        throw "Windows build failed before using ResoniteAdmin tooling. ExitCode=$buildExitCode`n$outputText"
    }

    if (-not (Test-Path -LiteralPath $ExpectedDllPath -PathType Leaf)) {
        throw "Expected fresh Windows build output not found: $ExpectedDllPath"
    }

    return (Get-Item -LiteralPath $ExpectedDllPath).FullName
}
