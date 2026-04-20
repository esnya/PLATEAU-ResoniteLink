using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace PlateauResoniteLink.Tests.Tools;

public sealed class ResoniteSessionToolCommandLineParserTests
{
    private static readonly string ScriptPath = TestData.GetRepositoryPath(
        ".agents",
        "skills",
        "resonite-live-send-debug",
        "tools",
        "session-tool.cs");

    [Fact]
    public async Task HelpListsThinPrimitiveSurface()
    {
        ProcessResult result = await RunSessionToolAsync("--help");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("discover-session", result.StdOut, StringComparison.Ordinal);
        Assert.Contains("dump-slot", result.StdOut, StringComparison.Ordinal);
        Assert.Contains("remove-slot", result.StdOut, StringComparison.Ordinal);
        Assert.Contains("start-headless", result.StdOut, StringComparison.Ordinal);
        Assert.Contains("stop-headless", result.StdOut, StringComparison.Ordinal);
        Assert.DoesNotContain("cleanup-dataset-root", result.StdOut, StringComparison.Ordinal);
        Assert.Contains("start-headless --runtime-root <path> [--headless-path <path>]", result.StdOut, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DumpSlotRejectsMissingEndpointAndStateContext()
    {
        ProcessResult result = await RunSessionToolAsync("dump-slot", "--depth", "1");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("Provide an explicit endpoint or a valid --state-path/--runtime-root tracked state.", result.StdErr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RemoveSlotRejectsConflictingSelectors()
    {
        ProcessResult result = await RunSessionToolAsync(
            "remove-slot",
            "ws://localhost:17136/",
            "--slot-id",
            "Root",
            "--root-child-name",
            "PLATEAU plateau-20202-matsumoto-shi-2020");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("Use either --slot-id or --root-child-name, not both.", result.StdErr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DumpSlotUsesEndpointFromTrackedStateFile()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), $"session-tool-test-{Guid.NewGuid():N}");
        string runtimeRoot = Path.Combine(tempRoot, "runtime");

        Directory.CreateDirectory(runtimeRoot);
        try
        {
            await WriteTrackedStateAsync(runtimeRoot, processId: 1234, endpoint: "ws://127.0.0.1:1/");

            ProcessResult result = await RunSessionToolAsync(
                "dump-slot",
                "--runtime-root",
                runtimeRoot,
                "--slot-id",
                "Root",
                "--depth",
                "0",
                "--exclude-component-data");

            Assert.NotEqual(0, result.ExitCode);
            Assert.DoesNotContain("Provide an explicit endpoint or a valid --state-path/--runtime-root tracked state.", result.StdErr, StringComparison.Ordinal);
            Assert.Contains("Unable to connect", result.StdErr, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task StartHeadlessRejectsDirectoryWithoutLauncherCandidates()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), $"session-tool-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);

        try
        {
            ProcessResult result = await RunSessionToolAsync(
                "start-headless",
                "--runtime-root",
                Path.Combine(tempRoot, "runtime"),
                "--headless-path",
                tempRoot);

            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("Expected Resonite.dll, Resonite.exe, Headless/Resonite.dll, or Headless/Resonite.exe.", result.StdErr, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task StartHeadlessRejectsExplicitInvalidLauncherFile()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), $"session-tool-test-{Guid.NewGuid():N}");
        string runtimeRoot = Path.Combine(tempRoot, "runtime");
        string invalidLauncher = Path.Combine(tempRoot, "not-resonite.exe");

        Directory.CreateDirectory(tempRoot);
        try
        {
            await File.WriteAllTextAsync(invalidLauncher, "not-a-launcher");

            ProcessResult result = await RunSessionToolAsync(
                "start-headless",
                "--runtime-root",
                runtimeRoot,
                "--headless-path",
                invalidLauncher);

            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("is not a supported Resonite launcher", result.StdErr, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task StartHeadlessUsesStandardInstallRootWhenHeadlessPathIsOmitted()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), $"session-tool-test-{Guid.NewGuid():N}");
        string runtimeRoot = Path.Combine(tempRoot, "runtime");
        string fakeInstallRoot = Path.Combine(tempRoot, "Resonite");
        string fakeLauncher = Path.Combine(fakeInstallRoot, "Headless", "Resonite.dll");

        Directory.CreateDirectory(Path.GetDirectoryName(fakeLauncher)!);
        await File.WriteAllTextAsync(fakeLauncher, "not-a-real-dotnet-assembly");

        string? originalConfiguredRoots = Environment.GetEnvironmentVariable("RESONITE_SESSION_TOOL_STANDARD_INSTALL_ROOTS");

        try
        {
            Environment.SetEnvironmentVariable("RESONITE_SESSION_TOOL_STANDARD_INSTALL_ROOTS", fakeInstallRoot);

            ProcessResult result = await RunSessionToolAsync(
                "start-headless",
                "--runtime-root",
                runtimeRoot,
                "--startup-timeout-seconds",
                "2");

            Assert.NotEqual(0, result.ExitCode);
            Assert.DoesNotContain("requires --headless-path", result.StdErr, StringComparison.Ordinal);
            Assert.DoesNotContain("No standard headless install root was found", result.StdErr, StringComparison.Ordinal);
            Assert.True(
                Directory.Exists(runtimeRoot),
                $"Expected runtime root '{runtimeRoot}' to be created when standard-root fallback launches.");
        }
        finally
        {
            Environment.SetEnvironmentVariable("RESONITE_SESSION_TOOL_STANDARD_INSTALL_ROOTS", originalConfiguredRoots);
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task DiscoverSessionIgnoresMalformedAnnouncementsUntilValidPacketArrives()
    {
        int listenPort = GetFreeUdpPort();
        using CancellationTokenSource senderCancellation = new(TimeSpan.FromSeconds(8));
        Task sender = Task.Run(async () =>
        {
            using UdpClient client = new();
            byte[] malformedPayload = Encoding.UTF8.GetBytes("""{"sessionName":"x","sessionID":"y","linkPort":"oops"}""");
            byte[] validPayload = Encoding.UTF8.GetBytes("""{"sessionName":"good","sessionID":"session-1","linkPort":19001}""");
            IPEndPoint endpoint = new(IPAddress.Loopback, listenPort);

            bool malformedSent = false;
            while (!senderCancellation.Token.IsCancellationRequested)
            {
                if (!malformedSent)
                {
                    await client.SendAsync(malformedPayload, endpoint);
                    malformedSent = true;
                }

                await client.SendAsync(validPayload, endpoint);

                try
                {
                    await Task.Delay(200, senderCancellation.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        });

        ProcessResult result = await RunSessionToolAsync(
            "discover-session",
            "--listen-port",
            listenPort.ToString(CultureInfo.InvariantCulture),
            "--timeout-seconds",
            "5",
            "--max-announcements",
            "1");

        await senderCancellation.CancelAsync();
        await sender;

        Assert.Equal(0, result.ExitCode);
        Assert.Contains(@"""SessionName"": ""good""", result.StdOut, StringComparison.Ordinal);
        Assert.Contains(@"""LinkPort"": 19001", result.StdOut, StringComparison.Ordinal);
        Assert.DoesNotContain("oops", result.StdOut, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StartHeadlessRejectsOutOfRangeResoniteLinkPort()
    {
        string runtimeRoot = Path.Combine(Path.GetTempPath(), $"session-tool-test-{Guid.NewGuid():N}", "runtime");
        Directory.CreateDirectory(runtimeRoot);

        try
        {
            ProcessResult result = await RunSessionToolAsync(
                "start-headless",
                "--runtime-root",
                runtimeRoot,
                "--headless-path",
                runtimeRoot,
                "--resonitelink-port",
                "70000");

            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("--resonitelink-port requires an integer value between 1 and 65535.", result.StdErr, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(runtimeRoot)!, recursive: true);
        }
    }

    [Fact]
    public async Task StartHeadlessDeletesStaleTrackedStateBeforeFailingLaunch()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), $"session-tool-test-{Guid.NewGuid():N}");
        string runtimeRoot = Path.Combine(tempRoot, "runtime");
        string invalidLauncher = Path.Combine(tempRoot, "not-a-launcher.txt");

        Directory.CreateDirectory(runtimeRoot);
        await File.WriteAllTextAsync(invalidLauncher, "invalid");
        string statePath = await WriteTrackedStateAsync(runtimeRoot, processId: 1234, endpoint: "ws://127.0.0.1:1/");

        try
        {
            ProcessResult result = await RunSessionToolAsync(
                "start-headless",
                "--runtime-root",
                runtimeRoot,
                "--headless-path",
                invalidLauncher);

            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("Expected Resonite.dll or Resonite.exe.", result.StdErr, StringComparison.Ordinal);
            Assert.False(File.Exists(statePath), $"Expected stale tracked state '{statePath}' to be deleted before launch.");
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task StartHeadlessOmitsInvalidFirstStandardRootAndContinuesToLaterConfiguredFallback()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), $"session-tool-test-{Guid.NewGuid():N}");
        string runtimeRoot = Path.Combine(tempRoot, "runtime");
        string invalidRoot = Path.Combine(tempRoot, "invalid-root");
        string fallbackRoot = Path.Combine(tempRoot, "valid-root");
        string fallbackLauncher = Path.Combine(fallbackRoot, "Headless", "Resonite.dll");
        string? originalConfiguredRoots = Environment.GetEnvironmentVariable("RESONITE_SESSION_TOOL_STANDARD_INSTALL_ROOTS");

        Directory.CreateDirectory(invalidRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(fallbackLauncher)!);
        await File.WriteAllTextAsync(fallbackLauncher, "not-a-real-dotnet-assembly");
        try
        {
            Environment.SetEnvironmentVariable(
                "RESONITE_SESSION_TOOL_STANDARD_INSTALL_ROOTS",
                string.Join(Path.PathSeparator, new[] { invalidRoot, fallbackRoot }));

            ProcessResult result = await RunSessionToolAsync(
                "start-headless",
                "--runtime-root",
                runtimeRoot,
                "--startup-timeout-seconds",
                "2");

            Assert.DoesNotContain($"No Resonite launcher was found under '{invalidRoot}'", result.StdErr, StringComparison.Ordinal);
            Assert.DoesNotContain("No standard headless install root was found", result.StdErr, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("RESONITE_SESSION_TOOL_STANDARD_INSTALL_ROOTS", originalConfiguredRoots);

            string statePath = Path.Combine(runtimeRoot, "active-session.json");
            if (File.Exists(statePath))
            {
                await RunSessionToolAsync("stop-headless", "--runtime-root", runtimeRoot);
            }

            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task StopHeadlessUsesTrackedStateProcessIdAndDeletesStateFile()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), $"session-tool-test-{Guid.NewGuid():N}");
        string runtimeRoot = Path.Combine(tempRoot, "runtime");

        Directory.CreateDirectory(runtimeRoot);
        using Process sleeper = StartSleepingProcess();
        try
        {
            string statePath = await WriteTrackedStateAsync(runtimeRoot, processId: sleeper.Id, endpoint: "ws://127.0.0.1:1/");

            ProcessResult result = await RunSessionToolAsync(
                "stop-headless",
                "--runtime-root",
                runtimeRoot);

            Assert.Equal(0, result.ExitCode);
            using JsonDocument document = JsonDocument.Parse(result.StdOut);
            Assert.Equal(sleeper.Id, document.RootElement.GetProperty("ProcessId").GetInt32());
            if (!sleeper.HasExited)
            {
                await sleeper.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            }

            Assert.True(sleeper.HasExited, "Expected the spawned sleeper process to be terminated by stop-headless.");
            Assert.False(File.Exists(statePath), $"Expected tracked state '{statePath}' to be deleted.");
        }
        finally
        {
            if (!sleeper.HasExited)
            {
                sleeper.Kill(entireProcessTree: true);
                await sleeper.WaitForExitAsync();
            }

            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void SessionToolSourceContainsExactDirectRootChildNameGuardrail()
    {
        string source = File.ReadAllText(ScriptPath);

        Assert.Contains("EnumerateDirectChildren(rootDump.Slot)", source, StringComparison.Ordinal);
        Assert.Contains(".Where(child => string.Equals(child.Name, rootChildName, StringComparison.Ordinal))", source, StringComparison.Ordinal);
        Assert.Contains("Root direct child '", source, StringComparison.Ordinal);
        Assert.Contains("was not found.", source, StringComparison.Ordinal);
        Assert.Contains("is ambiguous:", source, StringComparison.Ordinal);
    }

    [Fact]
    public void SessionToolSourceContinuesStandardRootFallbackAfterInvalidCandidate()
    {
        string source = File.ReadAllText(ScriptPath);

        Assert.Contains("catch (InvalidOperationException)", source, StringComparison.Ordinal);
        Assert.Contains("continue;", source, StringComparison.Ordinal);
    }

    [Fact]
    public void SessionToolSourceTreatsGetProcessByIdRaceAsNonFatalDuringStop()
    {
        string source = File.ReadAllText(ScriptPath);

        Assert.Contains("catch (ArgumentException)", source, StringComparison.Ordinal);
        Assert.Contains("The process exited between the liveness probe and GetProcessById.", source, StringComparison.Ordinal);
    }

    [Fact]
    public void SessionToolSourceUsesNonLoginShellForPosixLaunch()
    {
        string source = File.ReadAllText(ScriptPath);

        Assert.Contains("startInfo.ArgumentList.Add(\"-c\");", source, StringComparison.Ordinal);
        Assert.DoesNotContain("startInfo.ArgumentList.Add(\"-lc\");", source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StopHeadlessRejectsMissingLocator()
    {
        ProcessResult result = await RunSessionToolAsync("stop-headless");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("stop-headless requires --process-id or --runtime-root/--state-path.", result.StdErr, StringComparison.Ordinal);
    }

    private static Task<ProcessResult> RunSessionToolAsync(params string[] sessionToolArgs)
    {
        return RunSessionToolAsyncCore(sessionToolArgs);
    }

    private static async Task<ProcessResult> RunSessionToolAsyncCore(string[] sessionToolArgs)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = ResolveDotNetCommand(),
            WorkingDirectory = TestData.GetRepositoryPath(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        startInfo.ArgumentList.Add(ScriptPath);
        startInfo.ArgumentList.Add("--");

        foreach (string argument in sessionToolArgs)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process process = new() { StartInfo = startInfo };
        process.Start();

        Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
        Task<string> stderrTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        return new(process.ExitCode, await stdoutTask, await stderrTask);
    }

    private static string ResolveDotNetCommand()
    {
        return Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")
            ?? Environment.GetEnvironmentVariable("DOTNET_EXE")
            ?? "dotnet";
    }

    private static Process StartSleepingProcess()
    {
        ProcessStartInfo startInfo;
        if (OperatingSystem.IsWindows())
        {
            startInfo = new()
            {
                FileName = "powershell",
                UseShellExecute = false,
            };
            startInfo.ArgumentList.Add("-NoLogo");
            startInfo.ArgumentList.Add("-NoProfile");
            startInfo.ArgumentList.Add("-Command");
            startInfo.ArgumentList.Add("Start-Sleep -Seconds 30");
        }
        else
        {
            startInfo = new()
            {
                FileName = "bash",
                UseShellExecute = false,
            };
            startInfo.ArgumentList.Add("-lc");
            startInfo.ArgumentList.Add("sleep 30");
        }

        Process process = new() { StartInfo = startInfo };
        process.Start();
        return process;
    }

    private static int GetFreeUdpPort()
    {
        using UdpClient client = new(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)client.Client.LocalEndPoint!).Port;
    }

    private static async Task<string> WriteTrackedStateAsync(string runtimeRoot, int processId, string endpoint)
    {
        string statePath = Path.Combine(runtimeRoot, "active-session.json");
        Dictionary<string, object?> state = new()
        {
            ["ProcessId"] = processId,
            ["SessionName"] = "test-session",
            ["SessionId"] = "test-session-id",
            ["LinkPort"] = 19001,
            ["Endpoint"] = endpoint,
            ["DiscoveryMode"] = "udp",
            ["ConfigPath"] = Path.Combine(runtimeRoot, "Config.json"),
            ["SessionRoot"] = runtimeRoot,
            ["StdoutLog"] = Path.Combine(runtimeRoot, "stdout.log"),
            ["StderrLog"] = Path.Combine(runtimeRoot, "stderr.log"),
            ["DataFolder"] = Path.Combine(runtimeRoot, "Data"),
            ["CacheFolder"] = Path.Combine(runtimeRoot, "Cache"),
            ["LogsFolder"] = Path.Combine(runtimeRoot, "Logs"),
            ["RuntimeRoot"] = runtimeRoot,
            ["LauncherPath"] = Path.Combine(runtimeRoot, "Resonite.dll"),
            ["WorkingDirectory"] = runtimeRoot,
            ["WorldReadyLine"] = "World running...",
            ["StatePath"] = statePath,
        };

        await File.WriteAllTextAsync(statePath, JsonSerializer.Serialize(state));
        return statePath;
    }

    private sealed record ProcessResult(int ExitCode, string StdOut, string StdErr);
}
