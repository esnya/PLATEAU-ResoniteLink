using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

using ResoniteLink;

namespace ResoniteSessionTool;

internal static partial class SessionToolApplication
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
    };

    public static async Task<int> RunAsync(string[] args)
    {
        if (!ResoniteSessionToolCommandLineParser.TryParse(args, out ResoniteSessionToolCommandLineOptions? options, out string? error))
        {
            await Console.Error.WriteLineAsync(error);
            await Console.Error.WriteLineAsync(ResoniteSessionToolCommandLineParser.UsageText);
            return 1;
        }

        if (options is null)
        {
            throw new InvalidOperationException("Command line parsing succeeded without producing options.");
        }

        ResoniteSessionToolCommandLineOptions parsedOptions = options;
        ResoniteSessionToolCommandKind commandKind = parsedOptions.Kind;
        try
        {
            return commandKind switch
            {
                ResoniteSessionToolCommandKind.DiscoverSession => await ExecuteDiscoverSessionAsync(parsedOptions),
                ResoniteSessionToolCommandKind.DumpRoot => await ExecuteDumpRootAsync(parsedOptions),
                ResoniteSessionToolCommandKind.RemoveSlot => await ExecuteRemoveSlotAsync(parsedOptions),
                ResoniteSessionToolCommandKind.CleanupDatasetRoot => await ExecuteCleanupDatasetRootAsync(parsedOptions),
                ResoniteSessionToolCommandKind.StartHeadless => await ExecuteStartHeadlessAsync(parsedOptions),
                ResoniteSessionToolCommandKind.StopHeadless => await ExecuteStopHeadlessAsync(parsedOptions),
                _ => throw new InvalidOperationException($"Unsupported command kind '{commandKind}'."),
            };
        }
        catch (Exception ex) when (ex is InvalidOperationException
            or IOException
            or JsonException
            or SocketException
            or UnauthorizedAccessException
            or PlatformNotSupportedException
            or Win32Exception
            or TimeoutException
            or ArgumentException)
        {
            await Console.Error.WriteLineAsync(ex.Message);
            return commandKind switch
            {
                ResoniteSessionToolCommandKind.DumpRoot => 2,
                ResoniteSessionToolCommandKind.RemoveSlot => 3,
                ResoniteSessionToolCommandKind.CleanupDatasetRoot => 4,
                ResoniteSessionToolCommandKind.StartHeadless => 5,
                ResoniteSessionToolCommandKind.StopHeadless => 6,
                ResoniteSessionToolCommandKind.DiscoverSession => 7,
                _ => 99,
            };
        }
    }

    private static async Task<int> ExecuteDiscoverSessionAsync(ResoniteSessionToolCommandLineOptions options)
    {
        IReadOnlyList<DiscoveryAnnouncement> announcements = await CaptureAnnouncementsAsync(
            options.ListenPort,
            options.TimeoutSeconds,
            options.MaxAnnouncements,
            CancellationToken.None);

        await Console.Out.WriteLineAsync(JsonSerializer.Serialize(announcements, JsonOptions));
        return 0;
    }

    private static async Task<int> ExecuteDumpRootAsync(ResoniteSessionToolCommandLineOptions options)
    {
        string? repoRoot = string.IsNullOrWhiteSpace(options.RepoPath)
            ? null
            : SessionToolPaths.ResolveRepoRoot(options.RepoPath!);
        string? resolvedStatePath = SessionToolPaths.ResolveStatePath(options.StatePath, repoRoot);
        Uri endpoint = options.Endpoint ?? SessionToolPaths.ResolveEndpointFromState(resolvedStatePath);
        string? resolvedOutputPath = SessionToolPaths.ResolveDumpOutputPath(options.OutputPath, repoRoot, options.Label ?? "root");

        RootDump dump = await FetchRootDumpAsync(endpoint, options.Depth, options.IncludeComponentData);
        string json = JsonSerializer.Serialize(dump, JsonOptions);

        if (!string.IsNullOrWhiteSpace(resolvedOutputPath))
        {
            string fullOutputPath = Path.GetFullPath(resolvedOutputPath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullOutputPath)!);
            await File.WriteAllTextAsync(fullOutputPath, json);
            await Console.Out.WriteLineAsync($"Root dump written to '{fullOutputPath}'.");
            return 0;
        }

        await Console.Out.WriteLineAsync(json);
        return 0;
    }

    private static async Task<int> ExecuteRemoveSlotAsync(ResoniteSessionToolCommandLineOptions options)
    {
        using LinkInterface link = new();
        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(30));
        await link.Connect(options.Endpoint!, cts.Token);

        Response response = await link.RemoveSlot(
            new RemoveSlot
            {
                SlotID = options.SlotId!,
            });

        if (!response.Success)
        {
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(response.ErrorInfo)
                ? $"RemoveSlot failed for '{options.SlotId}'."
                : $"RemoveSlot failed for '{options.SlotId}': {response.ErrorInfo}");
        }

        await Console.Out.WriteLineAsync($"Removed slot '{options.SlotId}'.");
        return 0;
    }

    private static async Task<int> ExecuteCleanupDatasetRootAsync(ResoniteSessionToolCommandLineOptions options)
    {
        string repoRoot = SessionToolPaths.ResolveRepoRoot(options.RepoPath!);
        string runtimeRoot = SessionToolPaths.ResolveResoniteRuntimeRoot(repoRoot);
        Directory.CreateDirectory(runtimeRoot);

        string verificationDumpPath = Path.Combine(runtimeRoot, "cleanup-dataset-root-scan.json");
        string datasetRootName = $"PLATEAU {options.Dataset}";
        List<string> removedSlotIds = [];

        using LinkInterface link = new();
        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(30));
        await link.Connect(options.Endpoint!, cts.Token);

        if (!options.ListOnly)
        {
            RootDump initialDump = await FetchRootDumpAsync(options.Endpoint!, 1, includeComponentData: false, link);
            await WriteJsonFileAsync(verificationDumpPath, initialDump);
            List<SlotSummary> initialTargets = RootDumpCleanupTargets.FindDatasetRootTargets(initialDump, datasetRootName);
            await Console.Out.WriteLineAsync($"Found {initialTargets.Count} dataset root slot(s) named '{datasetRootName}'.");

            foreach (SlotSummary target in initialTargets)
            {
                Response removeResponse = await link.RemoveSlot(new RemoveSlot { SlotID = target.Id });
                if (!removeResponse.Success)
                {
                    throw new InvalidOperationException(string.IsNullOrWhiteSpace(removeResponse.ErrorInfo)
                        ? $"RemoveSlot failed for '{target.Id}'."
                        : $"RemoveSlot failed for '{target.Id}': {removeResponse.ErrorInfo}");
                }

                removedSlotIds.Add(target.Id);
                await Console.Out.WriteLineAsync($"Removed slot '{target.Id}' ({target.Name}).");
            }
        }

        DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(options.VerificationTimeoutSeconds);
        List<SlotSummary> remainingTargets;
        RootDump latestDump;
        do
        {
            latestDump = await FetchRootDumpAsync(options.Endpoint!, 1, includeComponentData: false, link);
            await WriteJsonFileAsync(verificationDumpPath, latestDump);
            remainingTargets = RootDumpCleanupTargets.FindDatasetRootTargets(latestDump, datasetRootName);
            await Console.Out.WriteLineAsync($"Found {remainingTargets.Count} dataset root slot(s) named '{datasetRootName}'.");

            if ((remainingTargets.Count == 0) && options.ListOnly)
            {
                foreach (SlotSummary child in RootDumpCleanupTargets.EnumerateRootChildren(latestDump))
                {
                    await Console.Out.WriteLineAsync($"Root child: {child.Id} :: {child.Name}");
                }
            }

            if ((remainingTargets.Count == 0) || options.ListOnly || (DateTimeOffset.UtcNow >= deadline))
            {
                break;
            }

            await Task.Delay(TimeSpan.FromSeconds(options.PollIntervalSeconds));
        }
        while (true);

        if (!options.ListOnly)
        {
            if (remainingTargets.Count != 0)
            {
                throw new InvalidOperationException($"Dataset root cleanup did not converge to zero roots for '{options.Dataset}'.");
            }

            foreach (string path in SessionToolPaths.GetCleanupArtifactPaths(repoRoot))
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                }
                else if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }

        var result = new
        {
            Endpoint = options.Endpoint!.ToString(),
            Dataset = options.Dataset,
            DatasetRootName = datasetRootName,
            ListOnly = options.ListOnly,
            RemovedSlotIds = removedSlotIds,
            RemainingSlotIds = remainingTargets.Select(static target => target.Id).ToArray(),
            VerificationDumpPath = verificationDumpPath,
        };

        await Console.Out.WriteLineAsync(JsonSerializer.Serialize(result, JsonOptions));
        return 0;
    }

    private static async Task<int> ExecuteStartHeadlessAsync(ResoniteSessionToolCommandLineOptions options)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Start-headless is only supported on Windows.");
        }

        string repoRoot = SessionToolPaths.ResolveRepoRoot(options.RepoPath!);
        string runtimeRoot = SessionToolPaths.ResolveHeadlessRuntimeRoot(repoRoot);
        string resolvedStatePath = SessionToolPaths.ResolveStatePath(options.StatePath, repoRoot)!;
        HeadlessLauncherSpec launcher = SessionToolPaths.ResolveHeadlessLauncher(options.HeadlessPath!);
        string sessionRoot = Path.Combine(runtimeRoot, options.LogPrefix);
        string stdoutLog = Path.Combine(runtimeRoot, $"{options.LogPrefix}.stdout.log");
        string stderrLog = Path.Combine(runtimeRoot, $"{options.LogPrefix}.stderr.log");
        string configPath = Path.Combine(sessionRoot, "Config.json");
        string headlessDataRoot = Path.Combine(sessionRoot, "Data");
        string headlessCacheRoot = Path.Combine(sessionRoot, "Cache");
        string headlessLogsRoot = Path.Combine(sessionRoot, "Logs");

        Directory.CreateDirectory(runtimeRoot);
        Directory.CreateDirectory(sessionRoot);
        Directory.CreateDirectory(headlessDataRoot);
        Directory.CreateDirectory(headlessCacheRoot);
        Directory.CreateDirectory(headlessLogsRoot);

        foreach (string path in new[] { stdoutLog, stderrLog, configPath })
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        Dictionary<string, object?> startWorld = new()
        {
            ["sessionName"] = options.SessionName,
            ["description"] = options.SessionDescription,
            ["accessLevel"] = "Anyone",
            ["hideFromPublicListing"] = true,
            ["loadWorldPresetName"] = "Grid",
            ["enableResoniteLink"] = true,
            ["saveOnExit"] = false,
            ["autoSleep"] = true,
        };

        if (options.ResoniteLinkPort is int requestedPort)
        {
            startWorld["forceResoniteLinkPort"] = requestedPort;
        }

        Dictionary<string, object?> config = new()
        {
            ["comment"] = "Disposable headless session for PLATEAU-ResoniteLink live tests.",
            ["dataFolder"] = headlessDataRoot,
            ["cacheFolder"] = headlessCacheRoot,
            ["logsFolder"] = headlessLogsRoot,
            ["startWorlds"] = new[] { startWorld },
        };

        await WriteJsonFileAsync(configPath, config);

        string filePath = launcher.RequiresDotNetHost
            ? SessionToolPaths.ResolveDotNetCommandPath()
            : launcher.LauncherPath;
        IReadOnlyList<string> argumentList = launcher.RequiresDotNetHost
            ? new[] { launcher.LauncherPath, "-HeadlessConfig", configPath }
            : new[] { "-HeadlessConfig", configPath };

        using NativeProcessLauncher.NativeLaunchedProcess launched = NativeProcessLauncher.Start(
            filePath,
            argumentList,
            launcher.WorkingDirectory,
            stdoutLog,
            stderrLog);

        int processId = launched.ProcessId;
        DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(options.StartupTimeoutSeconds);
        string? worldReadyLine = null;

        while (DateTimeOffset.UtcNow < deadline)
        {
            if (!IsProcessRunning(processId))
            {
                uint exitCode = launched.TryGetExitCode(out uint code) ? code : uint.MaxValue;
                throw new InvalidOperationException(
                    $"Headless process {processId} exited before readiness. ExitCode={exitCode}`nSTDOUT:`n{SessionToolPaths.GetLogTail(stdoutLog)}`nSTDERR:`n{SessionToolPaths.GetLogTail(stderrLog)}");
            }

            worldReadyLine = SessionToolPaths.FindLastMatchingLine(stdoutLog, static line => line.Contains("World running", StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(worldReadyLine))
            {
                break;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500));
        }

        if (string.IsNullOrWhiteSpace(worldReadyLine))
        {
            TryKillProcess(processId);
            throw new InvalidOperationException(
                $"Headless process {processId} did not report 'World running' within {options.StartupTimeoutSeconds}s.`nSTDOUT:`n{SessionToolPaths.GetLogTail(stdoutLog)}`nSTDERR:`n{SessionToolPaths.GetLogTail(stderrLog)}");
        }

        int? resolvedResoniteLinkPort = SessionToolPaths.TryExtractLastInt(stdoutLog, LinkPortRegex());
        if (resolvedResoniteLinkPort is null)
        {
            resolvedResoniteLinkPort = options.ResoniteLinkPort;
        }

        if (resolvedResoniteLinkPort is null)
        {
            TryKillProcess(processId);
            throw new InvalidOperationException(
                $"Headless process {processId} became ready but did not report a ResoniteLink port.`nSTDOUT:`n{SessionToolPaths.GetLogTail(stdoutLog)}`nSTDERR:`n{SessionToolPaths.GetLogTail(stderrLog)}");
        }

        if ((options.ResoniteLinkPort is int expectedPort) && (resolvedResoniteLinkPort.Value != expectedPort))
        {
            TryKillProcess(processId);
            throw new InvalidOperationException(
                $"Headless process {processId} reported ResoniteLink port {resolvedResoniteLinkPort.Value}, which does not match requested port {expectedPort}.`nSTDOUT:`n{SessionToolPaths.GetLogTail(stdoutLog)}`nSTDERR:`n{SessionToolPaths.GetLogTail(stderrLog)}");
        }

        int discoveryTimeoutSeconds = Math.Min(Math.Max(options.DiscoveryTimeoutSeconds, 1), 30);
        IReadOnlyList<DiscoveryAnnouncement> announcements;
        try
        {
            announcements = await CaptureAnnouncementsAsync(12512, discoveryTimeoutSeconds, 10, CancellationToken.None);
        }
        catch (InvalidOperationException)
        {
            announcements = Array.Empty<DiscoveryAnnouncement>();
        }

        DiscoveryAnnouncement? announcement = announcements
            .FirstOrDefault(candidate =>
                candidate.LinkPort == resolvedResoniteLinkPort.Value &&
                string.Equals(candidate.SessionName, options.SessionName, StringComparison.Ordinal));

        string sessionId = announcement?.SessionId
            ?? SessionToolPaths.TryExtractLastString(stdoutLog, SessionIdRegex())
            ?? string.Empty;

        TrackedHeadlessSessionState state = new(
            ProcessId: processId,
            SessionName: announcement?.SessionName ?? options.SessionName,
            SessionId: sessionId,
            LinkPort: announcement?.LinkPort ?? resolvedResoniteLinkPort.Value,
            Endpoint: $"ws://localhost:{announcement?.LinkPort ?? resolvedResoniteLinkPort.Value}/",
            DiscoveryMode: announcement is null ? "log-fallback" : "udp",
            ConfigPath: configPath,
            SessionRoot: sessionRoot,
            StdoutLog: stdoutLog,
            StderrLog: stderrLog,
            DataFolder: headlessDataRoot,
            CacheFolder: headlessCacheRoot,
            LogsFolder: headlessLogsRoot,
            LauncherPath: launcher.LauncherPath,
            WorkingDirectory: launcher.WorkingDirectory,
            WorldReadyLine: worldReadyLine,
            StatePath: resolvedStatePath);

        Directory.CreateDirectory(Path.GetDirectoryName(resolvedStatePath)!);
        await WriteJsonFileAsync(resolvedStatePath, state);
        await Console.Out.WriteLineAsync(JsonSerializer.Serialize(state, JsonOptions));
        return 0;
    }

    private static async Task<int> ExecuteStopHeadlessAsync(ResoniteSessionToolCommandLineOptions options)
    {
        string? repoRoot = string.IsNullOrWhiteSpace(options.RepoPath)
            ? null
            : SessionToolPaths.ResolveRepoRoot(options.RepoPath!);
        string? resolvedStatePath = SessionToolPaths.ResolveStatePath(options.StatePath, repoRoot);
        bool usedTrackedState = options.ProcessId is null;
        int processId = options.ProcessId ?? SessionToolPaths.ReadTrackedState(resolvedStatePath!).ProcessId;

        ProcessStopResult result;
        if (!IsProcessRunning(processId))
        {
            result = new ProcessStopResult(processId, WasRunning: false, HasExited: true, Forced: false);
        }
        else
        {
            bool forced = false;
            try
            {
                using Process process = Process.GetProcessById(processId);
                if (process.CloseMainWindow())
                {
                    await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));
                }
                else
                {
                    throw new InvalidOperationException();
                }
            }
            catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException or TimeoutException)
            {
                forced = true;
                TryKillProcess(processId);
            }

            if (IsProcessRunning(processId))
            {
                throw new InvalidOperationException($"Headless process {processId} is still running after targeted shutdown.");
            }

            result = new ProcessStopResult(processId, WasRunning: true, HasExited: true, Forced: forced);
        }

        if (usedTrackedState && !string.IsNullOrWhiteSpace(resolvedStatePath) && File.Exists(resolvedStatePath))
        {
            File.Delete(resolvedStatePath);
        }

        await Console.Out.WriteLineAsync(JsonSerializer.Serialize(result, JsonOptions));
        return 0;
    }

    private static async Task<RootDump> FetchRootDumpAsync(
        Uri endpoint,
        int depth,
        bool includeComponentData,
        LinkInterface? existingLink = null)
    {
        return await FetchSlotDumpAsync(endpoint, "Root", depth, includeComponentData, existingLink);
    }

    private static async Task<RootDump> FetchSlotDumpAsync(
        Uri endpoint,
        string slotId,
        int depth,
        bool includeComponentData,
        LinkInterface? existingLink = null)
    {
        bool ownsLink = existingLink is null;
        using LinkInterface? ownedLink = ownsLink ? new LinkInterface() : null;
        LinkInterface link = existingLink ?? ownedLink!;
        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(30));

        if (ownsLink)
        {
            await link.Connect(endpoint, cts.Token);
        }

        SlotData root = await link.GetSlotData(
            new GetSlot
            {
                SlotID = slotId,
                Depth = depth,
                IncludeComponentData = includeComponentData,
            });

        if (!root.Success)
        {
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(root.ErrorInfo)
                ? $"GetSlot '{slotId}' failed."
                : $"GetSlot '{slotId}' failed: {root.ErrorInfo}");
        }

        return new RootDump(
            endpoint.ToString(),
            DateTimeOffset.UtcNow,
            depth,
            includeComponentData,
            root.Data);
    }

    private static async Task<IReadOnlyList<DiscoveryAnnouncement>> CaptureAnnouncementsAsync(
        int listenPort,
        int timeoutSeconds,
        int maxAnnouncements,
        CancellationToken cancellationToken)
    {
        using UdpClient udp = new(AddressFamily.InterNetwork);
        udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        udp.Client.Bind(new IPEndPoint(IPAddress.Any, listenPort));

        List<DiscoveryAnnouncement> announcements = [];
        DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(timeoutSeconds);

        while ((DateTimeOffset.UtcNow < deadline) && (announcements.Count < maxAnnouncements))
        {
            TimeSpan remaining = deadline - DateTimeOffset.UtcNow;
            using CancellationTokenSource linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            linkedCts.CancelAfter(remaining <= TimeSpan.Zero ? TimeSpan.FromMilliseconds(1) : remaining);

            try
            {
                UdpReceiveResult received = await udp.ReceiveAsync(linkedCts.Token);
                DiscoveryAnnouncement? parsed = TryParseAnnouncement(received);
                if (parsed is not null)
                {
                    announcements.Add(parsed);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        if (announcements.Count == 0)
        {
            throw new InvalidOperationException($"No ResoniteLink announcements were captured on UDP {listenPort} within {timeoutSeconds}s.");
        }

        return announcements;
    }

    private static DiscoveryAnnouncement? TryParseAnnouncement(UdpReceiveResult received)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(received.Buffer);
            JsonElement root = document.RootElement;
            string? sessionName = root.TryGetProperty("sessionName", out JsonElement sessionNameElement) ? sessionNameElement.GetString() : null;
            string? sessionId = root.TryGetProperty("sessionID", out JsonElement sessionIdElement) ? sessionIdElement.GetString() : null;
            int? linkPort = root.TryGetProperty("linkPort", out JsonElement linkPortElement) ? linkPortElement.GetInt32() : null;

            if (string.IsNullOrWhiteSpace(sessionName) || string.IsNullOrWhiteSpace(sessionId) || (linkPort is null))
            {
                return null;
            }

            return new DiscoveryAnnouncement(
                sessionName,
                sessionId,
                linkPort.Value,
                received.RemoteEndPoint.Address.ToString(),
                DateTimeOffset.UtcNow);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static async Task WriteJsonFileAsync<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string json = JsonSerializer.Serialize(value, JsonOptions);
        await File.WriteAllTextAsync(path, json);
    }

    private static bool IsProcessRunning(int processId)
    {
        try
        {
            using Process process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static void TryKillProcess(int processId)
    {
        try
        {
            using Process process = Process.GetProcessById(processId);
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(5000);
            }
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception or NotSupportedException)
        {
        }
    }

    [GeneratedRegex(@"ResoniteLink Started on port:\s*([0-9]+)", RegexOptions.IgnoreCase)]
    private static partial Regex LinkPortRegex();

    [GeneratedRegex(@"Unique Session ID:\s*(.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex SessionIdRegex();
}
