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
        [string]$ExpectedDllPath,
        [string]$ToolName = 'Windows build'
    )

    foreach ($commandSpec in @(
        @{
            Kind      = 'restore'
            Arguments = @('restore', $ProjectPath, '-p:RepositoryHostOs=windows', '--force-evaluate', '-v', 'minimal')
        },
        @{
            Kind      = 'build'
            Arguments = @('build', $ProjectPath, '-c', 'Release', '-p:RepositoryHostOs=windows', '--no-restore')
        }
    )) {
        $stdoutPath = [System.IO.Path]::GetTempFileName()
        $stderrPath = [System.IO.Path]::GetTempFileName()

        try {
            $process = Start-Process `
                -FilePath $DotNetPath `
                -ArgumentList $commandSpec.Arguments `
                -Wait `
                -PassThru `
                -RedirectStandardOutput $stdoutPath `
                -RedirectStandardError $stderrPath

            if (Test-Path -LiteralPath $stdoutPath) {
                Get-Content -LiteralPath $stdoutPath | Out-Host
            }

            if (Test-Path -LiteralPath $stderrPath) {
                Get-Content -LiteralPath $stderrPath | Out-Host
            }

            $exitCode = $process.ExitCode
        }
        finally {
            foreach ($tempPath in @($stdoutPath, $stderrPath)) {
                if (Test-Path -LiteralPath $tempPath) {
                    Remove-Item -LiteralPath $tempPath -Force -ErrorAction SilentlyContinue
                }
            }
        }

        if ($exitCode -ne 0) {
            throw "$ToolName $($commandSpec.Kind) failed before launch. ExitCode=$exitCode"
        }
    }

    if (-not (Test-Path -LiteralPath $ExpectedDllPath -PathType Leaf)) {
        throw "$ToolName build output not found: $ExpectedDllPath"
    }

    return (Get-Item -LiteralPath $ExpectedDllPath)
}

function Resolve-ResoniteAdminOutputPaths {
    param(
        [string]$RepoRoot,
        [string]$Configuration = 'Release'
    )

    $outputRoot = Join-Path $RepoRoot ("artifacts\build\windows\bin\ResoniteAdmin\{0}\net10.0" -f $Configuration)
    [pscustomobject]@{
        DllPath = (Join-Path $outputRoot 'ResoniteAdmin.dll')
        ExePath = (Join-Path $outputRoot 'ResoniteAdmin.exe')
    }
}

function Ensure-ResoniteAdminBuildOutput {
    param(
        [string]$DotNetPath,
        [string]$ProjectPath,
        [string]$RepoRoot
    )

    $paths = Resolve-ResoniteAdminOutputPaths -RepoRoot $RepoRoot
    $dll = Ensure-WindowsBuildOutput `
        -DotNetPath $DotNetPath `
        -ProjectPath $ProjectPath `
        -ExpectedDllPath $paths.DllPath `
        -ToolName 'ResoniteAdmin'

    [pscustomobject]@{
        DllPath          = $dll.FullName
        DllLastWriteTime = $dll.LastWriteTime
        ExePath          = if (Test-Path -LiteralPath $paths.ExePath -PathType Leaf) {
            (Get-Item -LiteralPath $paths.ExePath).FullName
        }
        else {
            $null
        }
    }
}
