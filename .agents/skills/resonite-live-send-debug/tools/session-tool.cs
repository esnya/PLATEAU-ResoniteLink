#!/usr/bin/dotnet

#:sdk Microsoft.NET.Sdk
#:property TargetFramework=net10.0
#:property LangVersion=preview
#:property Nullable=enable
#:property ImplicitUsings=enable
#:property JsonSerializerIsReflectionEnabledByDefault=true
#:package YellowDogMan.ResoniteLink

#pragma warning disable CA2000
#pragma warning disable IL2026
#pragma warning disable IL3050

using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Text.RegularExpressions;
using System.Text;

using ResoniteLink;

using ResoniteComponent = ResoniteLink.Component;
using ResoniteMember = ResoniteLink.Member;

JsonSerializerOptions jsonOptions = new()
{
    WriteIndented = true,
    NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
    TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
};

Regex linkPortRegex = new(@"ResoniteLink Started on port:\s*([0-9]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
Regex sessionIdRegex = new(@"Unique Session ID:\s*(.+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

try
{
    return await RunAsync(args);
}
catch (Exception ex) when (ex is ArgumentException
    or InvalidOperationException
    or IOException
    or JsonException
    or SocketException
    or UnauthorizedAccessException
    or TimeoutException
    or Win32Exception)
{
    await Console.Error.WriteLineAsync(ex.Message);
    return 1;
}

async Task<int> RunAsync(string[] commandLineArgs)
{
    if (commandLineArgs.Length == 0)
    {
        await Console.Error.WriteLineAsync("No command was provided.");
        await Console.Error.WriteLineAsync(GetUsageText());
        return 1;
    }

    if (IsHelpCommand(commandLineArgs[0]))
    {
        await Console.Out.WriteLineAsync(GetUsageText());
        return 0;
    }

    return commandLineArgs[0] switch
    {
        "discover-session" => await ExecuteDiscoverSessionAsync(commandLineArgs[1..]),
        "dump-slot" => await ExecuteDumpSlotAsync(commandLineArgs[1..]),
        "remove-slot" => await ExecuteRemoveSlotAsync(commandLineArgs[1..]),
        "start-headless" => await ExecuteStartHeadlessAsync(commandLineArgs[1..]),
        "stop-headless" => await ExecuteStopHeadlessAsync(commandLineArgs[1..]),
        _ => throw new ArgumentException($"Unknown command '{commandLineArgs[0]}'."),
    };
}

string GetUsageText()
{
    return """
        Usage:
          session-tool.cs discover-session [--listen-port <port>] [--timeout-seconds <seconds>] [--max-announcements <count>]
          session-tool.cs dump-slot [<endpoint>] [--runtime-root <path>] [--state-path <path>] [--slot-id <id> | --root-child-name <name>] [--output <path>] [--depth <n>] [--include-component-data|--exclude-component-data]
          session-tool.cs remove-slot [<endpoint>] [--runtime-root <path>] [--state-path <path>] (--slot-id <id> | --root-child-name <name>)
          session-tool.cs start-headless --runtime-root <path> [--headless-path <path>] [--state-path <path>] [--resonitelink-port <port>] [--session-name <name>] [--session-description <text>] [--log-prefix <prefix>] [--startup-timeout-seconds <seconds>] [--discovery-timeout-seconds <seconds>]
          session-tool.cs stop-headless [--process-id <pid>] [--runtime-root <path>] [--state-path <path>]
        """;
}

bool IsHelpCommand(string value)
{
    return string.Equals(value, "--help", StringComparison.Ordinal)
        || string.Equals(value, "-h", StringComparison.Ordinal)
        || string.Equals(value, "help", StringComparison.Ordinal);
}

async Task<int> ExecuteDiscoverSessionAsync(string[] commandArgs)
{
    int listenPort = 12512;
    int timeoutSeconds = 20;
    int maxAnnouncements = 5;

    for (int index = 0; index < commandArgs.Length; index++)
    {
        string argument = commandArgs[index];
        switch (argument)
        {
            case "--listen-port":
                listenPort = ReadPositiveInt(commandArgs, ref index, argument);
                break;
            case "--timeout-seconds":
                timeoutSeconds = ReadPositiveInt(commandArgs, ref index, argument);
                break;
            case "--max-announcements":
                maxAnnouncements = ReadPositiveInt(commandArgs, ref index, argument);
                break;
            default:
                throw new ArgumentException($"Unknown discover-session option '{argument}'.");
        }
    }

    IReadOnlyList<DiscoveryAnnouncement> announcements = await CaptureAnnouncementsAsync(
        listenPort,
        timeoutSeconds,
        maxAnnouncements,
        CancellationToken.None);

    await Console.Out.WriteLineAsync(JsonSerializer.Serialize(announcements, jsonOptions));
    return 0;
}

async Task<int> ExecuteDumpSlotAsync(string[] commandArgs)
{
    Uri? endpoint = null;
    string? runtimeRoot = null;
    string? statePath = null;
    string? slotId = null;
    string? rootChildName = null;
    string? outputPath = null;
    int depth = -1;
    bool includeComponentData = true;

    int index = 0;
    if ((index < commandArgs.Length) && !IsOption(commandArgs[index]))
    {
        endpoint = ParseAbsoluteUri(commandArgs[index], "dump-slot endpoint");
        index++;
    }

    for (; index < commandArgs.Length; index++)
    {
        string argument = commandArgs[index];
        switch (argument)
        {
            case "--runtime-root":
                runtimeRoot = ReadValue(commandArgs, ref index, argument);
                break;
            case "--state-path":
                statePath = ReadValue(commandArgs, ref index, argument);
                break;
            case "--slot-id":
                slotId = ReadNonEmptyValue(commandArgs, ref index, argument);
                break;
            case "--root-child-name":
                rootChildName = ReadNonEmptyValue(commandArgs, ref index, argument);
                break;
            case "--output":
                outputPath = ReadValue(commandArgs, ref index, argument);
                break;
            case "--depth":
                depth = ReadInt(commandArgs, ref index, argument);
                break;
            case "--include-component-data":
                includeComponentData = true;
                break;
            case "--exclude-component-data":
                includeComponentData = false;
                break;
            default:
                throw new ArgumentException($"Unknown dump-slot option '{argument}'.");
        }
    }

    ValidateSlotSelector(slotId, rootChildName, allowDefaultRoot: true);
    Uri resolvedEndpoint = endpoint ?? ResolveEndpointFromStatePath(statePath, runtimeRoot);
    string resolvedSlotId = slotId ?? await ResolveRootChildSlotIdAsync(resolvedEndpoint, rootChildName) ?? "Root";
    string? resolvedName = rootChildName;

    SlotDump dump = await FetchSlotDumpAsync(resolvedEndpoint, resolvedSlotId, depth, includeComponentData);
    SlotDump output = dump with { ResolvedByRootChildName = resolvedName };
    string json = JsonSerializer.Serialize(output, jsonOptions);

    if (!string.IsNullOrWhiteSpace(outputPath))
    {
        string fullOutputPath = Path.GetFullPath(outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullOutputPath)!);
        await File.WriteAllTextAsync(fullOutputPath, json);
        await Console.Out.WriteLineAsync($"Slot dump written to '{fullOutputPath}'.");
        return 0;
    }

    await Console.Out.WriteLineAsync(json);
    return 0;
}

async Task<int> ExecuteRemoveSlotAsync(string[] commandArgs)
{
    Uri? endpoint = null;
    string? runtimeRoot = null;
    string? statePath = null;
    string? slotId = null;
    string? rootChildName = null;

    int index = 0;
    if ((index < commandArgs.Length) && !IsOption(commandArgs[index]))
    {
        endpoint = ParseAbsoluteUri(commandArgs[index], "remove-slot endpoint");
        index++;
    }

    for (; index < commandArgs.Length; index++)
    {
        string argument = commandArgs[index];
        switch (argument)
        {
            case "--runtime-root":
                runtimeRoot = ReadValue(commandArgs, ref index, argument);
                break;
            case "--state-path":
                statePath = ReadValue(commandArgs, ref index, argument);
                break;
            case "--slot-id":
                slotId = ReadNonEmptyValue(commandArgs, ref index, argument);
                break;
            case "--root-child-name":
                rootChildName = ReadNonEmptyValue(commandArgs, ref index, argument);
                break;
            default:
                throw new ArgumentException($"Unknown remove-slot option '{argument}'.");
        }
    }

    ValidateSlotSelector(slotId, rootChildName, allowDefaultRoot: false);
    Uri resolvedEndpoint = endpoint ?? ResolveEndpointFromStatePath(statePath, runtimeRoot);
    string resolvedSlotId = slotId ?? await ResolveRootChildSlotIdAsync(resolvedEndpoint, rootChildName) ?? throw new InvalidOperationException("A slot selector is required.");

    using LinkInterface link = new();
    using CancellationTokenSource cts = new(TimeSpan.FromSeconds(30));
    await link.Connect(resolvedEndpoint, cts.Token);

    Response response = await link.RemoveSlot(new RemoveSlot { SlotID = resolvedSlotId }).WaitAsync(cts.Token);
    if (!response.Success)
    {
        throw new InvalidOperationException(string.IsNullOrWhiteSpace(response.ErrorInfo)
            ? $"RemoveSlot failed for '{resolvedSlotId}'."
            : $"RemoveSlot failed for '{resolvedSlotId}': {response.ErrorInfo}");
    }

    object result = new
    {
        Endpoint = resolvedEndpoint.ToString(),
        SlotId = resolvedSlotId,
        ResolvedByRootChildName = rootChildName,
    };
    await Console.Out.WriteLineAsync(JsonSerializer.Serialize(result, jsonOptions));
    return 0;
}

async Task<int> ExecuteStartHeadlessAsync(string[] commandArgs)
{
    string? runtimeRoot = null;
    string? statePath = null;
    string? headlessPath = null;
    int? resoniteLinkPort = null;
    string sessionName = "PLATEAU Live Send Debug";
    string sessionDescription = "Disposable headless session for PLATEAU-ResoniteLink live tests.";
    string logPrefix = "headless-session";
    int startupTimeoutSeconds = 90;
    int discoveryTimeoutSeconds = 5;

    for (int index = 0; index < commandArgs.Length; index++)
    {
        string argument = commandArgs[index];
        switch (argument)
        {
            case "--runtime-root":
                runtimeRoot = ReadValue(commandArgs, ref index, argument);
                break;
            case "--state-path":
                statePath = ReadValue(commandArgs, ref index, argument);
                break;
            case "--headless-path":
                headlessPath = ReadValue(commandArgs, ref index, argument);
                break;
            case "--resonitelink-port":
                resoniteLinkPort = ReadPositiveInt(commandArgs, ref index, argument);
                break;
            case "--session-name":
                sessionName = ReadValue(commandArgs, ref index, argument);
                break;
            case "--session-description":
                sessionDescription = ReadValue(commandArgs, ref index, argument);
                break;
            case "--log-prefix":
                logPrefix = ReadNonEmptyValue(commandArgs, ref index, argument);
                break;
            case "--startup-timeout-seconds":
                startupTimeoutSeconds = ReadPositiveInt(commandArgs, ref index, argument);
                break;
            case "--discovery-timeout-seconds":
                discoveryTimeoutSeconds = ReadPositiveInt(commandArgs, ref index, argument);
                break;
            default:
                throw new ArgumentException($"Unknown start-headless option '{argument}'.");
        }
    }

    if (string.IsNullOrWhiteSpace(runtimeRoot))
    {
        throw new ArgumentException("start-headless requires --runtime-root.");
    }

    string resolvedRuntimeRoot = Path.GetFullPath(runtimeRoot);
    string resolvedStatePath = ResolveStatePath(statePath, resolvedRuntimeRoot);
    HeadlessLauncher launcher = ResolveHeadlessLauncherOrDefault(headlessPath);
    string sessionRoot = Path.Combine(resolvedRuntimeRoot, logPrefix);
    string stdoutLog = Path.Combine(resolvedRuntimeRoot, $"{logPrefix}.stdout.log");
    string stderrLog = Path.Combine(resolvedRuntimeRoot, $"{logPrefix}.stderr.log");
    string configPath = Path.Combine(sessionRoot, "Config.json");
    string headlessDataRoot = Path.Combine(sessionRoot, "Data");
    string headlessCacheRoot = Path.Combine(sessionRoot, "Cache");
    string headlessLogsRoot = Path.Combine(sessionRoot, "Logs");

    Directory.CreateDirectory(resolvedRuntimeRoot);
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
        ["sessionName"] = sessionName,
        ["description"] = sessionDescription,
        ["accessLevel"] = "Anyone",
        ["hideFromPublicListing"] = true,
        ["loadWorldPresetName"] = "Grid",
        ["enableResoniteLink"] = true,
        ["saveOnExit"] = false,
        ["autoSleep"] = true,
    };

    if (resoniteLinkPort is int requestedPort)
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

    HeadlessLaunch launch = ResolveHeadlessLaunch(launcher, configPath);
    StartedProcess started = StartProcess(launch, stdoutLog, stderrLog);
    int processId = started.Process.Id;
    DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(startupTimeoutSeconds);
    string? worldReadyLine = null;

    while (DateTimeOffset.UtcNow < deadline)
    {
        if (started.Process.HasExited)
        {
            int exitCode = started.Process.ExitCode;
            throw new InvalidOperationException(
                $"Headless process {processId} exited before readiness. ExitCode={exitCode}`nSTDOUT:`n{GetLogTail(stdoutLog)}`nSTDERR:`n{GetLogTail(stderrLog)}");
        }

        worldReadyLine = FindLastMatchingLine(stdoutLog, static line => line.Contains("World running", StringComparison.OrdinalIgnoreCase));
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
            $"Headless process {processId} did not report 'World running' within {startupTimeoutSeconds}s.`nSTDOUT:`n{GetLogTail(stdoutLog)}`nSTDERR:`n{GetLogTail(stderrLog)}");
    }

    int? resolvedResoniteLinkPort = TryExtractLastInt(stdoutLog, linkPortRegex) ?? resoniteLinkPort;
    if (resolvedResoniteLinkPort is null)
    {
        TryKillProcess(processId);
        throw new InvalidOperationException(
            $"Headless process {processId} became ready but did not report a ResoniteLink port.`nSTDOUT:`n{GetLogTail(stdoutLog)}`nSTDERR:`n{GetLogTail(stderrLog)}");
    }

    if ((resoniteLinkPort is int expectedPort) && (resolvedResoniteLinkPort.Value != expectedPort))
    {
        TryKillProcess(processId);
        throw new InvalidOperationException(
            $"Headless process {processId} reported ResoniteLink port {resolvedResoniteLinkPort.Value}, which does not match requested port {expectedPort}.`nSTDOUT:`n{GetLogTail(stdoutLog)}`nSTDERR:`n{GetLogTail(stderrLog)}");
    }

    IReadOnlyList<DiscoveryAnnouncement> announcements;
    try
    {
        announcements = await CaptureAnnouncementsAsync(12512, Math.Clamp(discoveryTimeoutSeconds, 1, 30), 10, CancellationToken.None);
    }
    catch (Exception ex) when (ex is InvalidOperationException or SocketException)
    {
        announcements = Array.Empty<DiscoveryAnnouncement>();
    }

    DiscoveryAnnouncement? announcement = announcements.FirstOrDefault(candidate =>
        candidate.LinkPort == resolvedResoniteLinkPort.Value
        && string.Equals(candidate.SessionName, sessionName, StringComparison.Ordinal));

    string sessionId = announcement?.SessionId
        ?? TryExtractLastString(stdoutLog, sessionIdRegex)
        ?? string.Empty;

    TrackedHeadlessSessionState state = new(
        ProcessId: processId,
        SessionName: announcement?.SessionName ?? sessionName,
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
        RuntimeRoot: resolvedRuntimeRoot,
        LauncherPath: launcher.LauncherPath,
        WorkingDirectory: launcher.WorkingDirectory,
        WorldReadyLine: worldReadyLine,
        StatePath: resolvedStatePath);

    await WriteJsonFileAsync(resolvedStatePath, state);
    await Console.Out.WriteLineAsync(JsonSerializer.Serialize(state, jsonOptions));
    return 0;
}

async Task<int> ExecuteStopHeadlessAsync(string[] commandArgs)
{
    string? runtimeRoot = null;
    string? statePath = null;
    int? processId = null;

    for (int index = 0; index < commandArgs.Length; index++)
    {
        string argument = commandArgs[index];
        switch (argument)
        {
            case "--runtime-root":
                runtimeRoot = ReadValue(commandArgs, ref index, argument);
                break;
            case "--state-path":
                statePath = ReadValue(commandArgs, ref index, argument);
                break;
            case "--process-id":
                processId = ReadPositiveInt(commandArgs, ref index, argument);
                break;
            default:
                throw new ArgumentException($"Unknown stop-headless option '{argument}'.");
        }
    }

    if (processId is null && string.IsNullOrWhiteSpace(runtimeRoot) && string.IsNullOrWhiteSpace(statePath))
    {
        throw new ArgumentException("stop-headless requires --process-id or --runtime-root/--state-path.");
    }

    string? resolvedStatePath = ResolveOptionalStatePath(statePath, runtimeRoot);
    bool usedTrackedState = processId is null;
    int resolvedProcessId = processId ?? ReadTrackedState(resolvedStatePath!).ProcessId;

    ProcessStopResult result;
    if (!IsProcessRunning(resolvedProcessId))
    {
        result = new(resolvedProcessId, WasRunning: false, HasExited: true, Forced: false);
    }
    else
    {
        bool forced = false;
        try
        {
            using Process process = Process.GetProcessById(resolvedProcessId);
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
            TryKillProcess(resolvedProcessId);
        }

        if (IsProcessRunning(resolvedProcessId))
        {
            throw new InvalidOperationException($"Headless process {resolvedProcessId} is still running after targeted shutdown.");
        }

        result = new(resolvedProcessId, WasRunning: true, HasExited: true, Forced: forced);
    }

    if (usedTrackedState && !string.IsNullOrWhiteSpace(resolvedStatePath) && File.Exists(resolvedStatePath))
    {
        File.Delete(resolvedStatePath);
    }

    await Console.Out.WriteLineAsync(JsonSerializer.Serialize(result, jsonOptions));
    return 0;
}

bool IsOption(string value)
{
    return value.StartsWith("--", StringComparison.Ordinal);
}

string ReadValue(string[] args, ref int index, string optionName)
{
    if ((index + 1) >= args.Length)
    {
        throw new ArgumentException($"{optionName} requires a value.");
    }

    index++;
    return args[index];
}

string ReadNonEmptyValue(string[] args, ref int index, string optionName)
{
    string value = ReadValue(args, ref index, optionName);
    if (string.IsNullOrWhiteSpace(value))
    {
        throw new ArgumentException($"{optionName} requires a non-empty value.");
    }

    return value;
}

int ReadInt(string[] args, ref int index, string optionName)
{
    string value = ReadValue(args, ref index, optionName);
    if (!int.TryParse(value, out int parsed))
    {
        throw new ArgumentException($"{optionName} requires an integer value.");
    }

    return parsed;
}

int ReadPositiveInt(string[] args, ref int index, string optionName)
{
    int parsed = ReadInt(args, ref index, optionName);
    if (parsed <= 0)
    {
        throw new ArgumentException($"{optionName} requires a positive integer value.");
    }

    return parsed;
}

Uri ParseAbsoluteUri(string value, string description)
{
    if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? endpoint))
    {
        throw new ArgumentException($"'{value}' is not a valid absolute URI for {description}.");
    }

    return endpoint;
}

void ValidateSlotSelector(string? slotId, string? rootChildName, bool allowDefaultRoot)
{
    if (!string.IsNullOrWhiteSpace(slotId) && !string.IsNullOrWhiteSpace(rootChildName))
    {
        throw new ArgumentException("Use either --slot-id or --root-child-name, not both.");
    }

    if (!allowDefaultRoot && string.IsNullOrWhiteSpace(slotId) && string.IsNullOrWhiteSpace(rootChildName))
    {
        throw new ArgumentException("remove-slot requires --slot-id or --root-child-name.");
    }
}

string ResolveStatePath(string? configuredStatePath, string runtimeRoot)
{
    if (!string.IsNullOrWhiteSpace(configuredStatePath))
    {
        return Path.GetFullPath(configuredStatePath);
    }

    return Path.Combine(Path.GetFullPath(runtimeRoot), "active-session.json");
}

string? ResolveOptionalStatePath(string? configuredStatePath, string? runtimeRoot)
{
    if (!string.IsNullOrWhiteSpace(configuredStatePath))
    {
        return Path.GetFullPath(configuredStatePath);
    }

    if (string.IsNullOrWhiteSpace(runtimeRoot))
    {
        return null;
    }

    return Path.Combine(Path.GetFullPath(runtimeRoot), "active-session.json");
}

Uri ResolveEndpointFromStatePath(string? configuredStatePath, string? runtimeRoot)
{
    string? resolvedStatePath = ResolveOptionalStatePath(configuredStatePath, runtimeRoot);
    if (string.IsNullOrWhiteSpace(resolvedStatePath) || !File.Exists(resolvedStatePath))
    {
        throw new InvalidOperationException("Provide an explicit endpoint or a valid --state-path/--runtime-root tracked state.");
    }

    TrackedHeadlessSessionState state = ReadTrackedState(resolvedStatePath);
    return ParseAbsoluteUri(state.Endpoint, "tracked endpoint");
}

TrackedHeadlessSessionState ReadTrackedState(string statePath)
{
    string json = File.ReadAllText(statePath);
    TrackedHeadlessSessionState? state = JsonSerializer.Deserialize<TrackedHeadlessSessionState>(json);
    if (state is null)
    {
        throw new InvalidOperationException($"Tracked state '{statePath}' could not be parsed.");
    }

    return state;
}

HeadlessLauncher ResolveHeadlessLauncherOrDefault(string? configuredPath)
{
    if (!string.IsNullOrWhiteSpace(configuredPath))
    {
        return ResolveHeadlessLauncher(configuredPath);
    }

    List<string> checkedRoots = [];
    foreach (string candidateRoot in GetStandardHeadlessInstallRoots())
    {
        checkedRoots.Add(candidateRoot);
        if (Directory.Exists(candidateRoot))
        {
            try
            {
                return ResolveHeadlessLauncher(candidateRoot);
            }
            catch (InvalidOperationException)
            {
                continue;
            }
        }
    }

    throw new InvalidOperationException(
        checkedRoots.Count == 0
            ? "No standard headless install roots are configured on this machine. Provide --headless-path explicitly."
            : $"No standard headless install root was found. Checked: {string.Join(", ", checkedRoots.Select(static path => $"'{path}'"))}. Provide --headless-path explicitly.");
}

IReadOnlyList<string> GetStandardHeadlessInstallRoots()
{
    string? configuredRoots = Environment.GetEnvironmentVariable("RESONITE_SESSION_TOOL_STANDARD_INSTALL_ROOTS");
    if (!string.IsNullOrWhiteSpace(configuredRoots))
    {
        return configuredRoots
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    if (!OperatingSystem.IsWindows())
    {
        return Array.Empty<string>();
    }

    HashSet<string> roots = new(StringComparer.OrdinalIgnoreCase);

    foreach (string? programFilesX86 in new[]
    {
        Environment.GetEnvironmentVariable("ProgramFiles(x86)"),
        @"C:\Program Files (x86)",
    })
    {
        if (string.IsNullOrWhiteSpace(programFilesX86))
        {
            continue;
        }

        roots.Add(Path.Combine(programFilesX86, "Steam", "steamapps", "common", "Resonite"));
    }

    return roots.ToArray();
}

HeadlessLauncher ResolveHeadlessLauncher(string configuredPath)
{
    string resolvedPath = Path.GetFullPath(configuredPath);
    if (!File.Exists(resolvedPath) && !Directory.Exists(resolvedPath))
    {
        throw new FileNotFoundException(
            $"The configured headless path '{configuredPath}' does not exist. " +
            "Provide an installed Resonite root or launcher file. " +
            "On a standard Windows Steam install, start with 'C:\\Program Files (x86)\\Steam\\steamapps\\common\\Resonite'.");
    }

    if (string.Equals(Path.GetFileName(resolvedPath), "Resonite", StringComparison.OrdinalIgnoreCase))
    {
        string headlessDirectory = Path.Combine(resolvedPath, "Headless");
        if (Directory.Exists(headlessDirectory))
        {
            resolvedPath = headlessDirectory;
        }
    }

    if (Directory.Exists(resolvedPath))
    {
        foreach (string candidate in new[]
        {
            "Resonite.dll",
            "Resonite.exe",
            Path.Combine("Headless", "Resonite.dll"),
            Path.Combine("Headless", "Resonite.exe"),
        })
        {
            string candidatePath = Path.Combine(resolvedPath, candidate);
            if (File.Exists(candidatePath))
            {
                return new(
                    candidatePath,
                    Path.GetDirectoryName(candidatePath) ?? resolvedPath,
                    candidatePath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase));
            }
        }

        throw new InvalidOperationException(
            $"No Resonite launcher was found under '{resolvedPath}'. " +
            "Expected Resonite.dll, Resonite.exe, Headless/Resonite.dll, or Headless/Resonite.exe. " +
            "On a standard Windows Steam install, start with 'C:\\Program Files (x86)\\Steam\\steamapps\\common\\Resonite'.");
    }

    if (!IsAcceptedLauncherFileName(Path.GetFileName(resolvedPath)))
    {
        throw new InvalidOperationException(
            $"The configured headless file '{resolvedPath}' is not a supported Resonite launcher. " +
            "Expected Resonite.dll or Resonite.exe. " +
            "On a standard Windows Steam install, start with 'C:\\Program Files (x86)\\Steam\\steamapps\\common\\Resonite'.");
    }

    return new(
        resolvedPath,
        Path.GetDirectoryName(resolvedPath) ?? throw new InvalidOperationException($"Cannot resolve a working directory for '{resolvedPath}'."),
        resolvedPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase));
}

bool IsAcceptedLauncherFileName(string? fileName)
{
    return string.Equals(fileName, "Resonite.dll", StringComparison.OrdinalIgnoreCase)
        || string.Equals(fileName, "Resonite.exe", StringComparison.OrdinalIgnoreCase);
}

HeadlessLaunch ResolveHeadlessLaunch(HeadlessLauncher launcher, string configPath)
{
    if (launcher.RequiresDotNetHost)
    {
        return new(
            ResolveDotNetCommand(),
            launcher.WorkingDirectory,
            [launcher.LauncherPath, "-HeadlessConfig", configPath]);
    }

    return new(
        launcher.LauncherPath,
        launcher.WorkingDirectory,
        ["-HeadlessConfig", configPath]);
}

string ResolveDotNetCommand()
{
    foreach (string? candidate in new[]
    {
        Environment.GetEnvironmentVariable("DOTNET_EXE"),
        Environment.GetEnvironmentVariable("DOTNET_HOST_PATH"),
        "dotnet",
    })
    {
        if (!string.IsNullOrWhiteSpace(candidate))
        {
            return candidate;
        }
    }

    return "dotnet";
}

StartedProcess StartProcess(HeadlessLaunch launch, string stdoutPath, string stderrPath)
{
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(stdoutPath))!);
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(stderrPath))!);

    if (OperatingSystem.IsWindows())
    {
        int processId = StartWindowsProcessDirect(
            launch.FileName,
            launch.ArgumentList,
            launch.WorkingDirectory,
            stdoutPath,
            stderrPath);

        return new(Process.GetProcessById(processId));
    }

    string shellCommand = BuildPosixExecCommand(launch, stdoutPath, stderrPath);
    ProcessStartInfo startInfo = new()
    {
        FileName = "/bin/sh",
        WorkingDirectory = launch.WorkingDirectory,
        UseShellExecute = false,
        RedirectStandardOutput = false,
        RedirectStandardError = false,
        CreateNoWindow = true,
    };
    startInfo.ArgumentList.Add("-lc");
    startInfo.ArgumentList.Add(shellCommand);

    Process process = new()
    {
        StartInfo = startInfo,
        EnableRaisingEvents = true,
    };

    if (!process.Start())
    {
        process.Dispose();
        throw new InvalidOperationException($"Failed to start process '{launch.FileName}'.");
    }

    return new(process);
}

string BuildPosixExecCommand(HeadlessLaunch launch, string stdoutPath, string stderrPath)
{
    string command = string.Join(
        ' ',
        new[] { QuotePosixShellArgument(launch.FileName) }
            .Concat(launch.ArgumentList.Select(QuotePosixShellArgument)));

    return $"exec {command} > {QuotePosixShellArgument(stdoutPath)} 2> {QuotePosixShellArgument(stderrPath)}";
}

string QuotePosixShellArgument(string value)
{
    return "'" + value.Replace("'", "'\"'\"'", StringComparison.Ordinal) + "'";
}

int StartWindowsProcessDirect(
    string filePath,
    IReadOnlyList<string> argumentList,
    string workingDirectory,
    string stdoutPath,
    string stderrPath)
{
    bool hasExplicitPath = Path.IsPathRooted(filePath)
        || filePath.Contains(Path.DirectorySeparatorChar)
        || filePath.Contains(Path.AltDirectorySeparatorChar);
    string? resolvedApplicationPath = hasExplicitPath ? Path.GetFullPath(filePath) : null;
    string executableForCommandLine = resolvedApplicationPath ?? filePath;

    NativeMethods.SECURITY_ATTRIBUTES securityAttributes = new()
    {
        nLength = Marshal.SizeOf<NativeMethods.SECURITY_ATTRIBUTES>(),
        bInheritHandle = true,
        lpSecurityDescriptor = nint.Zero,
    };

    nint stdoutHandle = CreateFileW(
        stdoutPath,
        NativeMethods.GenericWrite,
        NativeMethods.FileShareRead | NativeMethods.FileShareWrite,
        ref securityAttributes,
        NativeMethods.CreateAlways,
        NativeMethods.FileAttributeNormal,
        nint.Zero);
    if (stdoutHandle == NativeMethods.InvalidHandleValue)
    {
        throw new InvalidOperationException($"Failed to open redirected log file '{stdoutPath}'. {DescribeLastWin32Error()}");
    }

    nint stderrHandle = CreateFileW(
        stderrPath,
        NativeMethods.GenericWrite,
        NativeMethods.FileShareRead | NativeMethods.FileShareWrite,
        ref securityAttributes,
        NativeMethods.CreateAlways,
        NativeMethods.FileAttributeNormal,
        nint.Zero);
    if (stderrHandle == NativeMethods.InvalidHandleValue)
    {
        CloseHandle(stdoutHandle);
        throw new InvalidOperationException($"Failed to open redirected log file '{stderrPath}'. {DescribeLastWin32Error()}");
    }

    try
    {
        NativeMethods.STARTUPINFO startupInfo = new()
        {
            cb = Marshal.SizeOf<NativeMethods.STARTUPINFO>(),
            dwFlags = NativeMethods.StartfUseStdHandles,
            hStdInput = NativeMethods.GetStdHandle(-10),
            hStdOutput = stdoutHandle,
            hStdError = stderrHandle,
        };

        string commandLine = ToWindowsCommandLine(executableForCommandLine, argumentList);
        bool created = CreateProcessW(
            resolvedApplicationPath,
            commandLine,
            nint.Zero,
            nint.Zero,
            true,
            NativeMethods.CreateNoWindow,
            nint.Zero,
            workingDirectory,
            ref startupInfo,
            out NativeMethods.PROCESS_INFORMATION processInformation);

        if (!created)
        {
            throw new InvalidOperationException($"Failed to start process '{executableForCommandLine}'. {DescribeLastWin32Error()}");
        }

        CloseHandle(processInformation.hThread);
        CloseHandle(processInformation.hProcess);
        return processInformation.dwProcessId;
    }
    finally
    {
        CloseHandle(stdoutHandle);
        CloseHandle(stderrHandle);
    }
}

string ToWindowsCommandLine(string filePath, IReadOnlyList<string> argumentList)
{
    List<string> segments = new(argumentList.Count + 1)
    {
        QuoteWindowsCommandLineArgument(filePath),
    };

    segments.AddRange(argumentList.Select(QuoteWindowsCommandLineArgument));
    return string.Join(' ', segments);
}

string QuoteWindowsCommandLineArgument(string value)
{
    if (string.IsNullOrEmpty(value))
    {
        return "\"\"";
    }

    if (!value.Any(static character => char.IsWhiteSpace(character) || (character == '"')))
    {
        return value;
    }

    StringBuilder builder = new();
    builder.Append('"');
    int backslashCount = 0;

    foreach (char character in value)
    {
        if (character == '\\')
        {
            backslashCount++;
            continue;
        }

        if (character == '"')
        {
            builder.Append('\\', backslashCount * 2 + 1);
            builder.Append('"');
            backslashCount = 0;
            continue;
        }

        if (backslashCount > 0)
        {
            builder.Append('\\', backslashCount);
            backslashCount = 0;
        }

        builder.Append(character);
    }

    if (backslashCount > 0)
    {
        builder.Append('\\', backslashCount * 2);
    }

    builder.Append('"');
    return builder.ToString();
}

string DescribeLastWin32Error()
{
    return new Win32Exception(Marshal.GetLastWin32Error()).Message;
}

async Task<SlotDump> FetchSlotDumpAsync(
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

    SlotData slot = await link.GetSlotData(new GetSlot
    {
        SlotID = slotId,
        Depth = depth,
        IncludeComponentData = includeComponentData,
    }).WaitAsync(cts.Token);

    if (!slot.Success)
    {
        throw new InvalidOperationException(string.IsNullOrWhiteSpace(slot.ErrorInfo)
            ? $"GetSlot '{slotId}' failed."
            : $"GetSlot '{slotId}' failed: {slot.ErrorInfo}");
    }

    return new(
        Endpoint: endpoint.ToString(),
        SlotId: slotId,
        ResolvedByRootChildName: null,
        CapturedAtUtc: DateTimeOffset.UtcNow,
        Depth: depth,
        IncludeComponentData: includeComponentData,
        Slot: ToJsonElement(slot.Data));
}

async Task<string?> ResolveRootChildSlotIdAsync(Uri endpoint, string? rootChildName)
{
    if (string.IsNullOrWhiteSpace(rootChildName))
    {
        return null;
    }

    SlotDump rootDump = await FetchSlotDumpAsync(endpoint, "Root", 1, includeComponentData: false);
    IReadOnlyList<SlotSummary> matches = EnumerateDirectChildren(rootDump.Slot)
        .Where(child => string.Equals(child.Name, rootChildName, StringComparison.Ordinal))
        .ToArray();

    return matches.Count switch
    {
        0 => throw new InvalidOperationException($"Root direct child '{rootChildName}' was not found."),
        > 1 => throw new InvalidOperationException($"Root direct child '{rootChildName}' is ambiguous: {string.Join(", ", matches.Select(static child => child.Id))}."),
        _ => matches[0].Id,
    };
}

IReadOnlyList<SlotSummary> EnumerateDirectChildren(JsonElement slotData)
{
    if (!TryGetPropertyIgnoreCase(slotData, "children", out JsonElement childrenElement) || (childrenElement.ValueKind != JsonValueKind.Array))
    {
        return Array.Empty<SlotSummary>();
    }

    List<SlotSummary> children = [];
    foreach (JsonElement child in childrenElement.EnumerateArray())
    {
        string? id = TryReadNestedStringIgnoreCase(child, "id");
        string? name = TryReadNestedStringIgnoreCase(child, "name", "value");
        if (!string.IsNullOrWhiteSpace(id))
        {
            children.Add(new(id, name ?? string.Empty));
        }
    }

    return children;
}

string? TryReadNestedStringIgnoreCase(JsonElement element, params string[] propertyPath)
{
    JsonElement current = element;
    foreach (string property in propertyPath)
    {
        if ((current.ValueKind != JsonValueKind.Object) || !TryGetPropertyIgnoreCase(current, property, out JsonElement nested))
        {
            return null;
        }

        current = nested;
    }

    return current.ValueKind == JsonValueKind.String ? current.GetString() : current.ToString();
}

bool TryGetPropertyIgnoreCase(JsonElement element, string propertyName, out JsonElement value)
{
    if (element.ValueKind == JsonValueKind.Object)
    {
        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }
    }

    value = default;
    return false;
}

JsonElement ToJsonElement(object? value)
{
    object? plainValue = value switch
    {
        null => null,
        Slot slot => ConvertSlotToPlainJson(slot),
        Reference reference => ConvertReferenceToPlainJson(reference),
        ResoniteComponent component => ConvertComponentToPlainJson(component),
        ResoniteMember member => ConvertMemberToPlainJson(member),
        _ => value,
    };

    if (plainValue is null)
    {
        using JsonDocument document = JsonDocument.Parse("null");
        return document.RootElement.Clone();
    }

    return JsonSerializer.SerializeToElement(plainValue, plainValue.GetType(), jsonOptions);
}

object ConvertSlotToPlainJson(Slot slot)
{
    return new Dictionary<string, object?>
    {
        ["id"] = slot.ID,
        ["isReferenceOnly"] = slot.IsReferenceOnly,
        ["parent"] = ConvertReferenceToPlainJson(slot.Parent),
        ["position"] = ConvertFieldToPlainJson(slot.Position),
        ["rotation"] = ConvertFieldToPlainJson(slot.Rotation),
        ["scale"] = ConvertFieldToPlainJson(slot.Scale),
        ["isActive"] = ConvertFieldToPlainJson(slot.IsActive),
        ["isPersistent"] = ConvertFieldToPlainJson(slot.IsPersistent),
        ["name"] = ConvertFieldToPlainJson(slot.Name),
        ["tag"] = ConvertFieldToPlainJson(slot.Tag),
        ["orderOffset"] = ConvertFieldToPlainJson(slot.OrderOffset),
        ["components"] = (slot.Components ?? []).Select(component => ConvertComponentToPlainJson(component)).ToArray(),
        ["children"] = (slot.Children ?? []).Select(child => ConvertSlotToPlainJson(child)).ToArray(),
    };
}

object ConvertComponentToPlainJson(ResoniteComponent component)
{
    return new Dictionary<string, object?>
    {
        ["id"] = component.ID,
        ["componentType"] = component.ComponentType,
        ["isReferenceOnly"] = component.IsReferenceOnly,
        ["members"] = (component.Members ?? []).ToDictionary(
            pair => pair.Key,
            pair => ConvertMemberToPlainJson(pair.Value),
            StringComparer.Ordinal),
    };
}

object ConvertMemberToPlainJson(ResoniteMember member)
{
    return member switch
    {
        Reference reference => ConvertReferenceToPlainJson(reference),
        _ => ConvertFieldOrMemberToPlainJson(member),
    };
}

object ConvertReferenceToPlainJson(Reference? reference)
{
    if (reference is null)
    {
        return new Dictionary<string, object?>();
    }

    return new Dictionary<string, object?>
    {
        ["id"] = reference.ID,
        ["targetId"] = reference.TargetID,
        ["targetType"] = reference.TargetType,
    };
}

object ConvertFieldOrMemberToPlainJson(ResoniteMember member)
{
    Type memberType = member.GetType();
    var valueProperty = memberType.GetProperty("Value");
    var boxedValueProperty = memberType.GetProperty("BoxedValue");
    var valueTypeProperty = memberType.GetProperty("ValueType");

    object? value = valueProperty?.GetValue(member)
        ?? boxedValueProperty?.GetValue(member);

    return new Dictionary<string, object?>
    {
        ["id"] = member.ID,
        ["memberType"] = memberType.FullName ?? memberType.Name,
        ["valueType"] = valueTypeProperty?.GetValue(member)?.ToString(),
        ["value"] = ConvertScalarValue(value),
    };
}

object? ConvertFieldToPlainJson(object? field)
{
    if (field is ResoniteMember member)
    {
        return ConvertFieldOrMemberToPlainJson(member);
    }

    return ConvertScalarValue(field);
}

object? ConvertScalarValue(object? value)
{
    return value switch
    {
        null => null,
        float3 vector3 => new Dictionary<string, float>
        {
            ["x"] = vector3.x,
            ["y"] = vector3.y,
            ["z"] = vector3.z,
        },
        floatQ quaternion => new Dictionary<string, float>
        {
            ["x"] = quaternion.x,
            ["y"] = quaternion.y,
            ["z"] = quaternion.z,
            ["w"] = quaternion.w,
        },
        _ => value,
    };
}

async Task<IReadOnlyList<DiscoveryAnnouncement>> CaptureAnnouncementsAsync(
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

DiscoveryAnnouncement? TryParseAnnouncement(UdpReceiveResult received)
{
    try
    {
        using JsonDocument document = JsonDocument.Parse(received.Buffer);
        JsonElement root = document.RootElement;
        string? sessionName = root.TryGetProperty("sessionName", out JsonElement sessionNameElement) ? sessionNameElement.GetString() : null;
        string? sessionId = root.TryGetProperty("sessionID", out JsonElement sessionIdElement) ? sessionIdElement.GetString() : null;
        int? linkPort = null;
        if (root.TryGetProperty("linkPort", out JsonElement linkPortElement))
        {
            if (linkPortElement.ValueKind != JsonValueKind.Number || !linkPortElement.TryGetInt32(out int parsedLinkPort))
            {
                return null;
            }

            linkPort = parsedLinkPort;
        }

        if (string.IsNullOrWhiteSpace(sessionName) || string.IsNullOrWhiteSpace(sessionId) || (linkPort is null))
        {
            return null;
        }

        return new(sessionName, sessionId, linkPort.Value, received.RemoteEndPoint.Address.ToString(), DateTimeOffset.UtcNow);
    }
    catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
    {
        return null;
    }
}

async Task WriteJsonFileAsync<T>(string path, T value)
{
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
    string json = JsonSerializer.Serialize(value, jsonOptions);
    await File.WriteAllTextAsync(path, json);
}

bool IsProcessRunning(int processId)
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

void TryKillProcess(int processId)
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

string GetLogTail(string path, int lineCount = 20)
{
    if (!File.Exists(path))
    {
        return string.Empty;
    }

    return string.Join(Environment.NewLine, ReadLogLinesShared(path).TakeLast(lineCount));
}

string? FindLastMatchingLine(string path, Func<string, bool> predicate)
{
    if (!File.Exists(path))
    {
        return null;
    }

    return ReadLogLinesShared(path).LastOrDefault(predicate);
}

int? TryExtractLastInt(string path, Regex pattern)
{
    string? value = TryExtractLastString(path, pattern);
    return int.TryParse(value, out int parsed) ? parsed : null;
}

string? TryExtractLastString(string path, Regex pattern)
{
    if (!File.Exists(path))
    {
        return null;
    }

    foreach (string line in ReadLogLinesShared(path).Reverse())
    {
        Match match = pattern.Match(line);
        if (match.Success && (match.Groups.Count > 1))
        {
            return match.Groups[1].Value.Trim();
        }
    }

    return null;
}

IEnumerable<string> ReadLogLinesShared(string path)
{
    using FileStream stream = new(
        path,
        FileMode.Open,
        FileAccess.Read,
        FileShare.ReadWrite | FileShare.Delete);
    using StreamReader reader = new(stream);

    string? line;
    while ((line = reader.ReadLine()) is not null)
    {
        yield return line;
    }
}

static nint CreateFileW(
    string lpFileName,
    uint dwDesiredAccess,
    uint dwShareMode,
    ref NativeMethods.SECURITY_ATTRIBUTES lpSecurityAttributes,
    uint dwCreationDisposition,
    uint dwFlagsAndAttributes,
    nint hTemplateFile)
{
    return NativeMethods.CreateFileW(
        lpFileName,
        dwDesiredAccess,
        dwShareMode,
        ref lpSecurityAttributes,
        dwCreationDisposition,
        dwFlagsAndAttributes,
        hTemplateFile);
}

static bool CreateProcessW(
    string? lpApplicationName,
    string lpCommandLine,
    nint lpProcessAttributes,
    nint lpThreadAttributes,
    [MarshalAs(UnmanagedType.Bool)] bool bInheritHandles,
    uint dwCreationFlags,
    nint lpEnvironment,
    string lpCurrentDirectory,
    ref NativeMethods.STARTUPINFO lpStartupInfo,
    out NativeMethods.PROCESS_INFORMATION lpProcessInformation)
{
    return NativeMethods.CreateProcessW(
        lpApplicationName,
        lpCommandLine,
        lpProcessAttributes,
        lpThreadAttributes,
        bInheritHandles,
        dwCreationFlags,
        lpEnvironment,
        lpCurrentDirectory,
        ref lpStartupInfo,
        out lpProcessInformation);
}

static bool CloseHandle(nint hObject)
{
    return NativeMethods.CloseHandle(hObject);
}

static class NativeMethods
{
    public const uint CreateNoWindow = 0x08000000;
    public const int StartfUseStdHandles = 0x00000100;
    public const uint GenericWrite = 0x40000000;
    public const uint FileShareRead = 0x00000001;
    public const uint FileShareWrite = 0x00000002;
    public const uint CreateAlways = 2;
    public const uint FileAttributeNormal = 0x00000080;
    public static readonly nint InvalidHandleValue = new(-1);

    [StructLayout(LayoutKind.Sequential)]
    public struct SECURITY_ATTRIBUTES
    {
        public int nLength;
        public nint lpSecurityDescriptor;
        [MarshalAs(UnmanagedType.Bool)]
        public bool bInheritHandle;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct STARTUPINFO
    {
        public int cb;
        public string? lpReserved;
        public string? lpDesktop;
        public string? lpTitle;
        public int dwX;
        public int dwY;
        public int dwXSize;
        public int dwYSize;
        public int dwXCountChars;
        public int dwYCountChars;
        public int dwFillAttribute;
        public int dwFlags;
        public short wShowWindow;
        public short cbReserved2;
        public nint lpReserved2;
        public nint hStdInput;
        public nint hStdOutput;
        public nint hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PROCESS_INFORMATION
    {
        public nint hProcess;
        public nint hThread;
        public int dwProcessId;
        public int dwThreadId;
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern nint CreateFileW(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        ref SECURITY_ATTRIBUTES lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        nint hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool CreateProcessW(
        string? lpApplicationName,
        string lpCommandLine,
        nint lpProcessAttributes,
        nint lpThreadAttributes,
        [MarshalAs(UnmanagedType.Bool)] bool bInheritHandles,
        uint dwCreationFlags,
        nint lpEnvironment,
        string lpCurrentDirectory,
        ref STARTUPINFO lpStartupInfo,
        out PROCESS_INFORMATION lpProcessInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool CloseHandle(nint hObject);

    [DllImport("kernel32.dll", SetLastError = false)]
    public static extern nint GetStdHandle(int nStdHandle);
}

sealed record DiscoveryAnnouncement(
    string SessionName,
    string SessionId,
    int LinkPort,
    string RemoteIp,
    DateTimeOffset ReceivedAt);

sealed record SlotSummary(
    string Id,
    string Name);

sealed record SlotDump(
    string Endpoint,
    string SlotId,
    string? ResolvedByRootChildName,
    DateTimeOffset CapturedAtUtc,
    int Depth,
    bool IncludeComponentData,
    JsonElement Slot);

sealed record HeadlessLauncher(
    string LauncherPath,
    string WorkingDirectory,
    bool RequiresDotNetHost);

sealed record HeadlessLaunch(
    string FileName,
    string WorkingDirectory,
    IReadOnlyList<string> ArgumentList);

sealed record StartedProcess(Process Process);

sealed record TrackedHeadlessSessionState(
    int ProcessId,
    string SessionName,
    string SessionId,
    int LinkPort,
    string Endpoint,
    string DiscoveryMode,
    string ConfigPath,
    string SessionRoot,
    string StdoutLog,
    string StderrLog,
    string DataFolder,
    string CacheFolder,
    string LogsFolder,
    string RuntimeRoot,
    string LauncherPath,
    string WorkingDirectory,
    string WorldReadyLine,
    string StatePath);

sealed record ProcessStopResult(
    int ProcessId,
    bool WasRunning,
    bool HasExited,
    bool Forced);
