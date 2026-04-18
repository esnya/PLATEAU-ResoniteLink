function Resolve-RepoRoot {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RepoPath
    )

    return (Resolve-Path -LiteralPath $RepoPath).Path
}

function Resolve-HeadlessRuntimeRoot {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RepoRoot
    )

    return (Join-Path $RepoRoot 'runtime\windows\headless')
}

function Resolve-ResoniteRuntimeRoot {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RepoRoot
    )

    return (Join-Path $RepoRoot 'runtime\windows\resonite')
}

function Resolve-StatePath {
    param(
        [string]$ConfiguredStatePath,
        [Parameter(Mandatory = $true)]
        [string]$RuntimeRootPath
    )

    if (-not [string]::IsNullOrWhiteSpace($ConfiguredStatePath)) {
        return $ConfiguredStatePath
    }

    return (Join-Path $RuntimeRootPath 'active-session.json')
}

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
        [Parameter(Mandatory = $true)]
        [string]$DotNetPath,
        [Parameter(Mandatory = $true)]
        [string]$ProjectPath,
        [Parameter(Mandatory = $true)]
        [string]$ExpectedDllPath,
        [string]$ToolName = 'Windows build',
        [switch]$SkipBuild
    )

    if (-not $SkipBuild) {
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
    }

    if (-not (Test-Path -LiteralPath $ExpectedDllPath -PathType Leaf)) {
        throw "$ToolName build output not found: $ExpectedDllPath"
    }

    return (Get-Item -LiteralPath $ExpectedDllPath)
}

function Resolve-ResoniteSessionToolOutputPaths {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RepoRoot,
        [string]$Configuration = 'Release'
    )

    $outputRoot = Join-Path $RepoRoot ("artifacts\build\windows\bin\ResoniteSessionTool\{0}\net10.0" -f $Configuration)
    [pscustomobject]@{
        DllPath = (Join-Path $outputRoot 'ResoniteSessionTool.dll')
        ExePath = (Join-Path $outputRoot 'ResoniteSessionTool.exe')
    }
}

function Ensure-ResoniteSessionToolBuildOutput {
    param(
        [Parameter(Mandatory = $true)]
        [string]$DotNetPath,
        [Parameter(Mandatory = $true)]
        [string]$ProjectPath,
        [Parameter(Mandatory = $true)]
        [string]$RepoRoot
    )

    $paths = Resolve-ResoniteSessionToolOutputPaths -RepoRoot $RepoRoot
    $dll = Ensure-WindowsBuildOutput `
        -DotNetPath $DotNetPath `
        -ProjectPath $ProjectPath `
        -ExpectedDllPath $paths.DllPath `
        -ToolName 'ResoniteSessionTool'

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

function Get-BuiltDotNetToolLaunchSpec {
    param(
        [Parameter(Mandatory = $true)]
        [string]$DotNetPath,
        [Parameter(Mandatory = $true)]
        [psobject]$ToolBuild,
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments
    )

    if (-not [string]::IsNullOrWhiteSpace($ToolBuild.ExePath)) {
        return [pscustomobject]@{
            FilePath = $ToolBuild.ExePath
            Arguments = $Arguments
        }
    }

    return [pscustomobject]@{
        FilePath = $DotNetPath
        Arguments = @($ToolBuild.DllPath) + $Arguments
    }
}

function Resolve-LiveSendCliOutputPaths {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RepoRoot,
        [string]$Configuration = 'Release'
    )

    $outputRoot = Join-Path $RepoRoot ("artifacts\build\windows\bin\Plateau.ResoniteLink.Cli\{0}\net10.0" -f $Configuration)
    [pscustomobject]@{
        DllPath = (Join-Path $outputRoot 'Plateau.ResoniteLink.Cli.dll')
        ExePath = (Join-Path $outputRoot 'Plateau.ResoniteLink.Cli.exe')
    }
}

function Ensure-LiveSendCliBuildOutput {
    param(
        [Parameter(Mandatory = $true)]
        [string]$DotNetPath,
        [Parameter(Mandatory = $true)]
        [string]$ProjectPath,
        [Parameter(Mandatory = $true)]
        [string]$RepoRoot,
        [switch]$SkipBuild
    )

    $paths = Resolve-LiveSendCliOutputPaths -RepoRoot $RepoRoot
    $dll = Ensure-WindowsBuildOutput `
        -DotNetPath $DotNetPath `
        -ProjectPath $ProjectPath `
        -ExpectedDllPath $paths.DllPath `
        -ToolName 'PLATEAU-ResoniteLink CLI' `
        -SkipBuild:$SkipBuild

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
