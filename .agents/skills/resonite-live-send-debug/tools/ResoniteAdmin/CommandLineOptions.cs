namespace ResoniteAdmin;

public enum ResoniteAdminCommandKind
{
    CleanupDataset,
    DumpRoot,
}

public sealed record ResoniteAdminCommandLineOptions(
    ResoniteAdminCommandKind Kind,
    Uri Endpoint,
    string? Dataset,
    bool ListOnly,
    string? OutputPath,
    int Depth,
    bool IncludeComponentData);

public static class ResoniteAdminCommandLineParser
{
    public static string UsageText =>
        "Usage:" + Environment.NewLine +
        "  ResoniteAdmin <endpoint> <dataset> [--list-only]" + Environment.NewLine +
        "  ResoniteAdmin --dump-root <endpoint> [--output <path>] [--depth <n>] [--include-component-data|--exclude-component-data]";

    public static bool TryParse(
        string[] args,
        out ResoniteAdminCommandLineOptions? options,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(args);

        if (args.Length == 0)
        {
            options = null;
            error = "No command line arguments were provided.";
            return false;
        }

        return string.Equals(args[0], "--dump-root", StringComparison.Ordinal)
            ? TryParseDumpRoot(args, out options, out error)
            : TryParseCleanupDataset(args, out options, out error);
    }

    private static bool TryParseCleanupDataset(
        string[] args,
        out ResoniteAdminCommandLineOptions? options,
        out string? error)
    {
        if (args.Length < 2)
        {
            options = null;
            error = "Cleanup mode requires <endpoint> and <dataset>.";
            return false;
        }

        if (!Uri.TryCreate(args[0], UriKind.Absolute, out Uri? endpoint))
        {
            options = null;
            error = $"'{args[0]}' is not a valid absolute endpoint URI.";
            return false;
        }

        string dataset = args[1];
        bool listOnly = false;

        for (int index = 2; index < args.Length; index++)
        {
            if (string.Equals(args[index], "--list-only", StringComparison.Ordinal))
            {
                listOnly = true;
                continue;
            }

            options = null;
            error = $"Unknown cleanup option '{args[index]}'.";
            return false;
        }

        options = new ResoniteAdminCommandLineOptions(
            ResoniteAdminCommandKind.CleanupDataset,
            endpoint,
            dataset,
            listOnly,
            OutputPath: null,
            Depth: 1,
            IncludeComponentData: false);
        error = null;
        return true;
    }

    private static bool TryParseDumpRoot(
        string[] args,
        out ResoniteAdminCommandLineOptions? options,
        out string? error)
    {
        if (args.Length < 2)
        {
            options = null;
            error = "Dump-root mode requires <endpoint>.";
            return false;
        }

        if (!Uri.TryCreate(args[1], UriKind.Absolute, out Uri? endpoint))
        {
            options = null;
            error = $"'{args[1]}' is not a valid absolute endpoint URI.";
            return false;
        }

        string? outputPath = null;
        int depth = -1;
        bool includeComponentData = true;

        for (int index = 2; index < args.Length; index++)
        {
            string argument = args[index];
            if (string.Equals(argument, "--output", StringComparison.Ordinal))
            {
                if (index + 1 >= args.Length)
                {
                    options = null;
                    error = "--output requires a path value.";
                    return false;
                }

                outputPath = args[++index];
                continue;
            }

            if (string.Equals(argument, "--depth", StringComparison.Ordinal))
            {
                if (index + 1 >= args.Length)
                {
                    options = null;
                    error = "--depth requires an integer value.";
                    return false;
                }

                if (!int.TryParse(args[++index], out depth))
                {
                    options = null;
                    error = $"'{args[index]}' is not a valid integer depth.";
                    return false;
                }

                continue;
            }

            if (string.Equals(argument, "--include-component-data", StringComparison.Ordinal))
            {
                includeComponentData = true;
                continue;
            }

            if (string.Equals(argument, "--exclude-component-data", StringComparison.Ordinal))
            {
                includeComponentData = false;
                continue;
            }

            options = null;
            error = $"Unknown dump-root option '{argument}'.";
            return false;
        }

        options = new ResoniteAdminCommandLineOptions(
            ResoniteAdminCommandKind.DumpRoot,
            endpoint,
            Dataset: null,
            ListOnly: false,
            outputPath,
            depth,
            includeComponentData);
        error = null;
        return true;
    }
}
