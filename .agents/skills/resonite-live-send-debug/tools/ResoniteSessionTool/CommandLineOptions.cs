namespace ResoniteSessionTool;

public enum ResoniteSessionToolCommandKind
{
    DumpRoot,
    RemoveSlot,
}

public sealed record ResoniteSessionToolCommandLineOptions(
    ResoniteSessionToolCommandKind Kind,
    Uri Endpoint,
    string? SlotId,
    string? OutputPath,
    int Depth,
    bool IncludeComponentData);

public static class ResoniteSessionToolCommandLineParser
{
    public static string UsageText =>
        "Usage:" + Environment.NewLine +
        "  ResoniteSessionTool --dump-root <endpoint> [--output <path>] [--depth <n>] [--include-component-data|--exclude-component-data]" + Environment.NewLine +
        "  ResoniteSessionTool --remove-slot <endpoint> <slot-id>";

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

        return args[0] switch
        {
            "--dump-root" => TryParseDumpRoot(args, out options, out error),
            "--remove-slot" => TryParseRemoveSlot(args, out options, out error),
            _ => Fail(out options, out error, $"Unknown command '{args[0]}'."),
        };
    }

    private static bool TryParseDumpRoot(
        string[] args,
        out ResoniteSessionToolCommandLineOptions? options,
        out string? error)
    {
        if (args.Length < 2)
        {
            return Fail(out options, out error, "Dump-root mode requires <endpoint>.");
        }

        if (!Uri.TryCreate(args[1], UriKind.Absolute, out Uri? endpoint))
        {
            return Fail(out options, out error, $"'{args[1]}' is not a valid absolute endpoint URI.");
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
                    return Fail(out options, out error, "--output requires a path value.");
                }

                outputPath = args[++index];
                continue;
            }

            if (string.Equals(argument, "--depth", StringComparison.Ordinal))
            {
                if (index + 1 >= args.Length)
                {
                    return Fail(out options, out error, "--depth requires an integer value.");
                }

                if (!int.TryParse(args[++index], out depth))
                {
                    return Fail(out options, out error, $"'{args[index]}' is not a valid integer depth.");
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

            return Fail(out options, out error, $"Unknown dump-root option '{argument}'.");
        }

        options = new ResoniteSessionToolCommandLineOptions(
            ResoniteSessionToolCommandKind.DumpRoot,
            endpoint,
            SlotId: null,
            outputPath,
            depth,
            includeComponentData);
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

        options = new ResoniteSessionToolCommandLineOptions(
            ResoniteSessionToolCommandKind.RemoveSlot,
            endpoint,
            slotId,
            OutputPath: null,
            Depth: 1,
            IncludeComponentData: false);
        error = null;
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
