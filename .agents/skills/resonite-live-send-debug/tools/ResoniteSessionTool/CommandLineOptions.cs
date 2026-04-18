namespace ResoniteSessionTool;

public enum ResoniteSessionToolCommandKind
{
    DiscoverSession,
    DumpRoot,
    RemoveSlot,
    CleanupDatasetRoot,
    StartHeadless,
    StopHeadless,
}

public sealed record ResoniteSessionToolCommandLineOptions(
    ResoniteSessionToolCommandKind Kind,
    Uri? Endpoint,
    string? SlotId,
    string? OutputPath,
    string? Label,
    int Depth,
    bool IncludeComponentData,
    string? RepoPath,
    string? StatePath,
    int ListenPort,
    int TimeoutSeconds,
    int MaxAnnouncements,
    string? Dataset,
    bool ListOnly,
    int VerificationTimeoutSeconds,
    int PollIntervalSeconds,
    string? HeadlessPath,
    int? ResoniteLinkPort,
    string SessionName,
    string SessionDescription,
    string LogPrefix,
    int StartupTimeoutSeconds,
    int DiscoveryTimeoutSeconds,
    int? ProcessId);

public static class ResoniteSessionToolCommandLineParser
{
    public static string UsageText =>
        """
        Usage:
          ResoniteSessionTool --discover-session [--listen-port <port>] [--timeout-seconds <seconds>] [--max-announcements <count>]
          ResoniteSessionTool --dump-root [<endpoint>] [--repo-path <path>] [--state-path <path>] [--output <path>] [--label <label>] [--depth <n>] [--include-component-data|--exclude-component-data]
          ResoniteSessionTool --remove-slot <endpoint> <slot-id>
          ResoniteSessionTool --cleanup-dataset-root <endpoint> <dataset> --repo-path <path> [--list-only] [--verification-timeout-seconds <seconds>] [--poll-interval-seconds <seconds>]
          ResoniteSessionTool --start-headless --repo-path <path> --headless-path <path> [--resonitelink-port <port>] [--session-name <name>] [--session-description <text>] [--log-prefix <prefix>] [--startup-timeout-seconds <seconds>] [--discovery-timeout-seconds <seconds>] [--state-path <path>]
          ResoniteSessionTool --stop-headless [--process-id <pid>] [--repo-path <path>] [--state-path <path>]
        """;

    public static bool TryParse(
        string[] args,
        out ResoniteSessionToolCommandLineOptions? options,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(args);

        if (args.Length == 0)
        {
            options = null;
            error = "No command line arguments were provided.";
            return false;
        }

        try
        {
            return args[0] switch
            {
                "--discover-session" => TryParseDiscoverSession(args, out options, out error),
                "--dump-root" => TryParseDumpRoot(args, out options, out error),
                "--remove-slot" => TryParseRemoveSlot(args, out options, out error),
                "--cleanup-dataset-root" => TryParseCleanupDatasetRoot(args, out options, out error),
                "--start-headless" => TryParseStartHeadless(args, out options, out error),
                "--stop-headless" => TryParseStopHeadless(args, out options, out error),
                _ => Fail(out options, out error, $"Unknown command '{args[0]}'."),
            };
        }
        catch (ArgumentException ex)
        {
            return Fail(out options, out error, ex.Message);
        }
    }

    private static bool TryParseDiscoverSession(
        string[] args,
        out ResoniteSessionToolCommandLineOptions? options,
        out string? error)
    {
        int listenPort = 12512;
        int timeoutSeconds = 20;
        int maxAnnouncements = 5;

        for (int index = 1; index < args.Length; index++)
        {
            string argument = args[index];
            switch (argument)
            {
                case "--listen-port":
                    if (!TryReadPositiveInt(args, ref index, argument, out listenPort, out error))
                    {
                        return Fail(out options, out error, error!);
                    }

                    break;
                case "--timeout-seconds":
                    if (!TryReadPositiveInt(args, ref index, argument, out timeoutSeconds, out error))
                    {
                        return Fail(out options, out error, error!);
                    }

                    break;
                case "--max-announcements":
                    if (!TryReadPositiveInt(args, ref index, argument, out maxAnnouncements, out error))
                    {
                        return Fail(out options, out error, error!);
                    }

                    break;
                default:
                    return Fail(out options, out error, $"Unknown discover-session option '{argument}'.");
            }
        }

        options = CreateDefaultOptions(ResoniteSessionToolCommandKind.DiscoverSession) with
        {
            ListenPort = listenPort,
            TimeoutSeconds = timeoutSeconds,
            MaxAnnouncements = maxAnnouncements,
        };
        error = null;
        return true;
    }

    private static bool TryParseDumpRoot(
        string[] args,
        out ResoniteSessionToolCommandLineOptions? options,
        out string? error)
    {
        Uri? endpoint = null;
        string? repoPath = null;
        string? statePath = null;
        string? outputPath = null;
        string label = "root";
        int depth = -1;
        bool includeComponentData = true;
        int index = 1;

        if ((index < args.Length) && !args[index].StartsWith("--", StringComparison.Ordinal))
        {
            if (!Uri.TryCreate(args[index], UriKind.Absolute, out endpoint))
            {
                return Fail(out options, out error, $"'{args[index]}' is not a valid absolute endpoint URI.");
            }

            index++;
        }

        for (; index < args.Length; index++)
        {
            string argument = args[index];
            switch (argument)
            {
                case "--repo-path":
                    repoPath = ReadValue(args, ref index, argument);
                    break;
                case "--state-path":
                    statePath = ReadValue(args, ref index, argument);
                    break;
                case "--output":
                    outputPath = ReadValue(args, ref index, argument);
                    break;
                case "--label":
                    label = ReadValue(args, ref index, argument);
                    break;
                case "--depth":
                    if (!TryReadInt(args, ref index, argument, out depth, out error))
                    {
                        return Fail(out options, out error, error!);
                    }

                    break;
                case "--include-component-data":
                    includeComponentData = true;
                    break;
                case "--exclude-component-data":
                    includeComponentData = false;
                    break;
                default:
                    return Fail(out options, out error, $"Unknown dump-root option '{argument}'.");
            }
        }

        if ((endpoint is null) && string.IsNullOrWhiteSpace(repoPath) && string.IsNullOrWhiteSpace(statePath))
        {
            return Fail(out options, out error, "Dump-root mode requires <endpoint> or --repo-path/--state-path.");
        }

        options = CreateDefaultOptions(ResoniteSessionToolCommandKind.DumpRoot) with
        {
            Endpoint = endpoint,
            RepoPath = repoPath,
            StatePath = statePath,
            OutputPath = outputPath,
            Label = label,
            Depth = depth,
            IncludeComponentData = includeComponentData,
        };
        error = null;
        return true;
    }

    private static bool TryParseRemoveSlot(
        string[] args,
        out ResoniteSessionToolCommandLineOptions? options,
        out string? error)
    {
        if (args.Length < 3)
        {
            return Fail(out options, out error, "Remove-slot mode requires <endpoint> and <slot-id>.");
        }

        if (!Uri.TryCreate(args[1], UriKind.Absolute, out Uri? endpoint))
        {
            return Fail(out options, out error, $"'{args[1]}' is not a valid absolute endpoint URI.");
        }

        string slotId = args[2];
        if (string.IsNullOrWhiteSpace(slotId))
        {
            return Fail(out options, out error, "Remove-slot mode requires a non-empty <slot-id>.");
        }

        if (args.Length > 3)
        {
            return Fail(out options, out error, $"Unknown remove-slot option '{args[3]}'.");
        }

        options = CreateDefaultOptions(ResoniteSessionToolCommandKind.RemoveSlot) with
        {
            Endpoint = endpoint,
            SlotId = slotId,
            Depth = 1,
            IncludeComponentData = false,
        };
        error = null;
        return true;
    }

    private static bool TryParseCleanupDatasetRoot(
        string[] args,
        out ResoniteSessionToolCommandLineOptions? options,
        out string? error)
    {
        if (args.Length < 3)
        {
            return Fail(out options, out error, "Cleanup-dataset-root mode requires <endpoint> and <dataset>.");
        }

        if (!Uri.TryCreate(args[1], UriKind.Absolute, out Uri? endpoint))
        {
            return Fail(out options, out error, $"'{args[1]}' is not a valid absolute endpoint URI.");
        }

        string dataset = args[2];
        if (string.IsNullOrWhiteSpace(dataset))
        {
            return Fail(out options, out error, "Cleanup-dataset-root mode requires a non-empty <dataset>.");
        }

        string? repoPath = null;
        bool listOnly = false;
        int verificationTimeoutSeconds = 20;
        int pollIntervalSeconds = 2;

        for (int index = 3; index < args.Length; index++)
        {
            string argument = args[index];
            switch (argument)
            {
                case "--repo-path":
                    repoPath = ReadValue(args, ref index, argument);
                    break;
                case "--list-only":
                    listOnly = true;
                    break;
                case "--verification-timeout-seconds":
                    if (!TryReadPositiveInt(args, ref index, argument, out verificationTimeoutSeconds, out error))
                    {
                        return Fail(out options, out error, error!);
                    }

                    break;
                case "--poll-interval-seconds":
                    if (!TryReadPositiveInt(args, ref index, argument, out pollIntervalSeconds, out error))
                    {
                        return Fail(out options, out error, error!);
                    }

                    break;
                default:
                    return Fail(out options, out error, $"Unknown cleanup-dataset-root option '{argument}'.");
            }
        }

        if (string.IsNullOrWhiteSpace(repoPath))
        {
            return Fail(out options, out error, "Cleanup-dataset-root mode requires --repo-path.");
        }

        options = CreateDefaultOptions(ResoniteSessionToolCommandKind.CleanupDatasetRoot) with
        {
            Endpoint = endpoint,
            Dataset = dataset,
            RepoPath = repoPath,
            ListOnly = listOnly,
            VerificationTimeoutSeconds = verificationTimeoutSeconds,
            PollIntervalSeconds = pollIntervalSeconds,
            Depth = 1,
            IncludeComponentData = false,
        };
        error = null;
        return true;
    }

    private static bool TryParseStartHeadless(
        string[] args,
        out ResoniteSessionToolCommandLineOptions? options,
        out string? error)
    {
        string? repoPath = null;
        string? headlessPath = null;
        int? resoniteLinkPort = null;
        string sessionName = "PLATEAU Headless Test";
        string sessionDescription = "Disposable headless session for PLATEAU-ResoniteLink live tests.";
        string logPrefix = "headless";
        int startupTimeoutSeconds = 120;
        int discoveryTimeoutSeconds = 3;
        string? statePath = null;

        for (int index = 1; index < args.Length; index++)
        {
            string argument = args[index];
            switch (argument)
            {
                case "--repo-path":
                    repoPath = ReadValue(args, ref index, argument);
                    break;
                case "--headless-path":
                    headlessPath = ReadValue(args, ref index, argument);
                    break;
                case "--resonitelink-port":
                    if (!TryReadPositiveInt(args, ref index, argument, out int parsedPort, out error))
                    {
                        return Fail(out options, out error, error!);
                    }

                    resoniteLinkPort = parsedPort;
                    break;
                case "--session-name":
                    sessionName = ReadValue(args, ref index, argument);
                    break;
                case "--session-description":
                    sessionDescription = ReadValue(args, ref index, argument);
                    break;
                case "--log-prefix":
                    logPrefix = ReadValue(args, ref index, argument);
                    break;
                case "--startup-timeout-seconds":
                    if (!TryReadPositiveInt(args, ref index, argument, out startupTimeoutSeconds, out error))
                    {
                        return Fail(out options, out error, error!);
                    }

                    break;
                case "--discovery-timeout-seconds":
                    if (!TryReadPositiveInt(args, ref index, argument, out discoveryTimeoutSeconds, out error))
                    {
                        return Fail(out options, out error, error!);
                    }

                    break;
                case "--state-path":
                    statePath = ReadValue(args, ref index, argument);
                    break;
                default:
                    return Fail(out options, out error, $"Unknown start-headless option '{argument}'.");
            }
        }

        if (string.IsNullOrWhiteSpace(repoPath))
        {
            return Fail(out options, out error, "Start-headless mode requires --repo-path.");
        }

        if (string.IsNullOrWhiteSpace(headlessPath))
        {
            return Fail(out options, out error, "Start-headless mode requires --headless-path.");
        }

        options = CreateDefaultOptions(ResoniteSessionToolCommandKind.StartHeadless) with
        {
            RepoPath = repoPath,
            HeadlessPath = headlessPath,
            ResoniteLinkPort = resoniteLinkPort,
            SessionName = sessionName,
            SessionDescription = sessionDescription,
            LogPrefix = logPrefix,
            StartupTimeoutSeconds = startupTimeoutSeconds,
            DiscoveryTimeoutSeconds = discoveryTimeoutSeconds,
            StatePath = statePath,
        };
        error = null;
        return true;
    }

    private static bool TryParseStopHeadless(
        string[] args,
        out ResoniteSessionToolCommandLineOptions? options,
        out string? error)
    {
        string? repoPath = null;
        string? statePath = null;
        int? processId = null;

        for (int index = 1; index < args.Length; index++)
        {
            string argument = args[index];
            switch (argument)
            {
                case "--repo-path":
                    repoPath = ReadValue(args, ref index, argument);
                    break;
                case "--state-path":
                    statePath = ReadValue(args, ref index, argument);
                    break;
                case "--process-id":
                    if (!TryReadPositiveInt(args, ref index, argument, out int parsedProcessId, out error))
                    {
                        return Fail(out options, out error, error!);
                    }

                    processId = parsedProcessId;
                    break;
                default:
                    return Fail(out options, out error, $"Unknown stop-headless option '{argument}'.");
            }
        }

        if ((processId is null) && string.IsNullOrWhiteSpace(repoPath) && string.IsNullOrWhiteSpace(statePath))
        {
            return Fail(out options, out error, "Stop-headless mode requires --process-id or --repo-path/--state-path.");
        }

        options = CreateDefaultOptions(ResoniteSessionToolCommandKind.StopHeadless) with
        {
            RepoPath = repoPath,
            StatePath = statePath,
            ProcessId = processId,
        };
        error = null;
        return true;
    }

    private static ResoniteSessionToolCommandLineOptions CreateDefaultOptions(ResoniteSessionToolCommandKind kind)
    {
        return new ResoniteSessionToolCommandLineOptions(
            kind,
            Endpoint: null,
            SlotId: null,
            OutputPath: null,
            Label: "root",
            Depth: -1,
            IncludeComponentData: true,
            RepoPath: null,
            StatePath: null,
            ListenPort: 12512,
            TimeoutSeconds: 20,
            MaxAnnouncements: 5,
            Dataset: null,
            ListOnly: false,
            VerificationTimeoutSeconds: 20,
            PollIntervalSeconds: 2,
            HeadlessPath: null,
            ResoniteLinkPort: null,
            SessionName: "PLATEAU Headless Test",
            SessionDescription: "Disposable headless session for PLATEAU-ResoniteLink live tests.",
            LogPrefix: "headless",
            StartupTimeoutSeconds: 120,
            DiscoveryTimeoutSeconds: 3,
            ProcessId: null);
    }

    private static string ReadValue(string[] args, ref int index, string optionName)
    {
        if (index + 1 >= args.Length)
        {
            throw new ArgumentException($"{optionName} requires a value.");
        }

        index++;
        return args[index];
    }

    private static bool TryReadInt(
        string[] args,
        ref int index,
        string optionName,
        out int value,
        out string? error)
    {
        if (index + 1 >= args.Length)
        {
            value = default;
            error = $"{optionName} requires an integer value.";
            return false;
        }

        index++;
        if (!int.TryParse(args[index], out value))
        {
            error = $"'{args[index]}' is not a valid integer value for {optionName}.";
            return false;
        }

        error = null;
        return true;
    }

    private static bool TryReadPositiveInt(
        string[] args,
        ref int index,
        string optionName,
        out int value,
        out string? error)
    {
        if (!TryReadInt(args, ref index, optionName, out value, out error))
        {
            return false;
        }

        if (value < 1)
        {
            error = $"{optionName} requires a positive integer value.";
            return false;
        }

        return true;
    }

    private static bool Fail(
        out ResoniteSessionToolCommandLineOptions? options,
        out string? error,
        string message)
    {
        options = null;
        error = message;
        return false;
    }
}
