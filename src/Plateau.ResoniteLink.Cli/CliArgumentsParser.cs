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
          --packages <csv>       Optional. Comma-separated PLATEAU package names. Default: dem,bldg,brid,frn,tran,rwy,trk,tun,ubld,unf,veg.
          --source <value>       Optional. local or remote. Default: local.
          --local-source-path <path>
                               Required when --source local is used. Mirrors the Unity SDK LocalSourcePath naming.
          --server-url <url>     Optional. Absolute URL for a remote dataset source or direct .zip/.7z archive. Mirrors the Unity SDK ServerUrl naming.
          --work-root <path>     Optional. Working directory for live-generated assets and remote download cache. Default: runtime/<os>/resonite.
          --resonitelink-port    Required unless --resonitelink-url is used. Connect to ws://localhost:<port>/ and build live in Resonite.
          --resonitelink-url     Required unless --resonitelink-port is used. Absolute ws:// or wss:// endpoint for live ResoniteLink builds.
          --resonitelink-connections <count>
                                                             Optional. Number of parallel ResoniteLink connections for live sends. Default: 1.
          --send-metrics         Optional. Enable opt-in live send metrics and CLI summary output.
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
        string? localSourcePath = null;
        string workRoot = Path.Combine("runtime", GetCurrentOsDirectoryName(), "resonite");
        Uri? resoniteLinkUri = null;
        int resoniteLinkConnectionCount = 1;
        bool enableSendMetrics = false;
        DatasetSourceKind sourceKind = DatasetSourceKind.Local;
        Uri? serverUri = null;
        IReadOnlyList<string> packageNames = PlateauPackageCatalog.CliDefaultPackageNames;

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
                    case "--packages":
                        {
                            string packageValue = ReadValue(args, ref index, token);
                            if (!TryParsePackageNames(packageValue, out string[]? parsedPackageNames, out string? packageError))
                            {
                                return CliParseResult.Failure(packageError!);
                            }

                            packageNames = parsedPackageNames!;
                            break;
                        }
                    case "--tile":
                        return CliParseResult.Failure(
                            "The --tile option has been replaced. Use --mesh-code.");
                    case "--local-source-path":
                        localSourcePath = ReadValue(args, ref index, token);
                        break;
                    case "--work-root":
                        workRoot = ReadValue(args, ref index, token);
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
                    case "--resonitelink-connections":
                        {
                            string connectionCountValue = ReadValue(args, ref index, token);
                            if (!int.TryParse(connectionCountValue, out resoniteLinkConnectionCount)
                                || resoniteLinkConnectionCount < 1)
                            {
                                return CliParseResult.Failure(
                                    $"The value '{connectionCountValue}' is not a valid ResoniteLink connection count.");
                            }

                            break;
                        }
                    case "--send-metrics":
                        enableSendMetrics = true;
                        break;
                    case "--source":
                        {
                            string sourceValue = ReadValue(args, ref index, token);
                            if (!Enum.TryParse<DatasetSourceKind>(sourceValue, ignoreCase: true, out sourceKind))
                            {
                                return CliParseResult.Failure(
                                    $"Unsupported source '{sourceValue}'. Use 'local' or 'remote'.");
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
            LocalSourcePath: localSourcePath,
            ServerUri: serverUri,
            PackageNames: packageNames);

        if (resoniteLinkUri is null)
        {
            return CliParseResult.Failure(
                "Specify either --resonitelink-port or --resonitelink-url.");
        }

        return CliParseResult.Success(
            new BuildCommandOptions(
                request,
                workRoot,
                resoniteLinkUri,
                resoniteLinkConnectionCount,
                enableSendMetrics));
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

    private static bool TryParsePackageNames(
        string csvValue,
        out string[]? packageNames,
        out string? error)
    {
        packageNames = null;
        error = null;

        string[] parsedValues = csvValue
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        if (parsedValues.Length == 0)
        {
            error = "The --packages option requires at least one package name.";
            return false;
        }

        string[] unsupportedPackageNames = parsedValues
            .Where(packageName => !PlateauPackageCatalog.TryNormalizePackageName(packageName, out _))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (unsupportedPackageNames.Length > 0)
        {
            error =
                $"Unsupported package name(s): {string.Join(", ", unsupportedPackageNames)}. Supported packages: {string.Join(", ", PlateauPackageCatalog.SupportedPackageNames)}.";
            return false;
        }

        packageNames = PlateauPackageCatalog.NormalizeRequestedPackageNames(parsedValues);
        return true;
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
