using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Cli;

public static class CliArgumentsParser
{
    public const string HelpText =
        """
        Plateau.ResoniteLink CLI

        Usage:
          plateau-resonitelink build --dataset <dataset> --mesh-code <mesh-code> [options]

        Options:
          --dataset <value>      Required. PLATEAU dataset identifier.
          --mesh-code <value>    Required. PLATEAU mesh code to construct in Resonite.
          --source <value>       Optional. local or server. Default: local.
          --input <path>         Required when --source local is used. Treat as the PLATEAU dataset root.
          --server-url <url>     Optional. Absolute URL for a server-backed dataset source.
          --output-root <path>   Optional. Output directory. Default: artifacts/<os>/resonite.
          --resonitelink-port    Optional. Connect to ws://localhost:<port>/ and build live in Resonite.
          --resonitelink-url     Optional. Absolute ws:// or wss:// endpoint for live ResoniteLink builds.
          -h, --help             Show this help text.
        """;

    public static CliParseResult Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        if (args.Length == 0 || args[0] is "-h" or "--help")
        {
            return CliParseResult.Help();
        }

        if (!string.Equals(args[0], "build", StringComparison.OrdinalIgnoreCase))
        {
            return CliParseResult.Failure($"Unknown command '{args[0]}'.");
        }

        string? dataset = null;
        string? meshCode = null;
        string? input = null;
        string outputRoot = Path.Combine("artifacts", GetCurrentOsDirectoryName(), "resonite");
        Uri? resoniteLinkUri = null;
        DatasetSourceKind sourceKind = DatasetSourceKind.Local;
        Uri? serverUri = null;

        try
        {
            for (int index = 1; index < args.Length; index++)
            {
                string token = args[index];

                switch (token)
                {
                    case "-h":
                    case "--help":
                        return CliParseResult.Help();
                    case "--dataset":
                        dataset = ReadValue(args, ref index, token);
                        break;
                    case "--mesh-code":
                        meshCode = ReadValue(args, ref index, token);
                        break;
                    case "--tile":
                        return CliParseResult.Failure(
                            "The --tile option has been replaced. Use --mesh-code.");
                    case "--input":
                        input = ReadValue(args, ref index, token);
                        break;
                    case "--output-root":
                        outputRoot = ReadValue(args, ref index, token);
                        break;
                    case "--resonitelink-port":
                        {
                            if (resoniteLinkUri is not null)
                            {
                                return CliParseResult.Failure(
                                    "Specify either --resonitelink-port or --resonitelink-url, not both.");
                            }

                            string portValue = ReadValue(args, ref index, token);
                            if (!int.TryParse(portValue, out int port) || port is < 1 or > 65535)
                            {
                                return CliParseResult.Failure(
                                    $"The value '{portValue}' is not a valid TCP port.");
                            }

                            resoniteLinkUri = new Uri($"ws://localhost:{port}/", UriKind.Absolute);
                            break;
                        }
                    case "--resonitelink-url":
                        {
                            if (resoniteLinkUri is not null)
                            {
                                return CliParseResult.Failure(
                                    "Specify either --resonitelink-port or --resonitelink-url, not both.");
                            }

                            string resoniteLinkUrlValue = ReadValue(args, ref index, token);
                            if (!Uri.TryCreate(
                                resoniteLinkUrlValue,
                                UriKind.Absolute,
                                out resoniteLinkUri))
                            {
                                return CliParseResult.Failure(
                                    $"The value '{resoniteLinkUrlValue}' is not a valid absolute URL.");
                            }

                            if (!string.Equals(resoniteLinkUri.Scheme, "ws", StringComparison.OrdinalIgnoreCase)
                                && !string.Equals(resoniteLinkUri.Scheme, "wss", StringComparison.OrdinalIgnoreCase))
                            {
                                return CliParseResult.Failure(
                                    "The --resonitelink-url value must use the ws or wss scheme.");
                            }

                            break;
                        }
                    case "--source":
                        {
                            string sourceValue = ReadValue(args, ref index, token);
                            if (!Enum.TryParse<DatasetSourceKind>(sourceValue, ignoreCase: true, out sourceKind))
                            {
                                return CliParseResult.Failure(
                                    $"Unsupported source '{sourceValue}'. Use 'local' or 'server'.");
                            }

                            break;
                        }
                    case "--server-url":
                        {
                            string serverUrl = ReadValue(args, ref index, token);
                            if (!Uri.TryCreate(serverUrl, UriKind.Absolute, out serverUri))
                            {
                                return CliParseResult.Failure(
                                    $"The value '{serverUrl}' is not a valid absolute URL.");
                            }

                            break;
                        }
                    default:
                        return CliParseResult.Failure($"Unknown option '{token}'.");
                }
            }
        }
        catch (ArgumentException exception)
        {
            return CliParseResult.Failure(exception.Message);
        }

        PlateauImportRequest request = new(
            Dataset: dataset ?? string.Empty,
            MeshCode: meshCode ?? string.Empty,
            SourceKind: sourceKind,
            InputPath: input,
            ServerUri: serverUri);

        return CliParseResult.Success(new BuildCommandOptions(request, outputRoot, resoniteLinkUri));
    }

    private static string ReadValue(string[] args, ref int index, string optionName)
    {
        if (index + 1 >= args.Length)
        {
            throw new ArgumentException($"A value is required after '{optionName}'.");
        }

        index++;
        return args[index];
    }

    internal static string GetCurrentOsDirectoryName()
    {
        if (OperatingSystem.IsWindows())
        {
            return "windows";
        }

        if (OperatingSystem.IsLinux())
        {
            return "linux";
        }

        if (OperatingSystem.IsMacOS())
        {
            return "macos";
        }

        return "unknown";
    }
}
