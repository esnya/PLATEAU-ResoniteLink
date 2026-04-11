using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Cli;

public static class CliArgumentsParser
{
    private static readonly string[] DefaultPackageNames = CliDefaultOptions.PackageNames;

    public const string HelpText =
        """
        Plateau.ResoniteLink CLI

        Usage:
          plateau-resonitelink build --dataset <dataset> --mesh-code <mesh-code> [options]

        Options:
          --dataset <value>      Required. PLATEAU dataset identifier.
          --mesh-code <value>    Required. PLATEAU mesh code or regex to construct in Resonite.
          --packages <csv>       Optional. Comma-separated PLATEAU package names. Default: dem,bldg,brid,frn,tran,rwy,trk,tun,ubld,unf,veg.
          --exclude-lod <csv>    Optional. Comma-separated LOD levels to exclude globally.
          --exclude-lod-for-package <csv>
                                Optional. Package-specific LOD exclusions: 'package:lod,package:lod' (e.g., tran:1,bldg:0).
                                Default fallback: tran:1 when this option is omitted. Use 'tran:none' (or 'tran:') to clear tran exclusions explicitly.
          --include-marking <true|false>
                                Optional. Include generated road markings even when marked for exclusion. Default: true.
          --{package}-pattern <pattern>
                                Optional. Pattern filter for specific package (e.g., --tran-pattern "*Marking").
                                Supports: "*suffix", "prefix*", "*middle*", "exact".
          --source <value>       Optional. local or remote. Default: local.
          --dem-terrain-mode <mesh|heightmap>
                                Optional. DEM import mode. Default: mesh.
          --dem-heightmap-meters-per-vertex <value>
                                Optional. Heightmap sampling spacing in meters. Default: 2.0.
          --dem-heightmap-max-resolution <value>
                                Optional. Maximum heightmap resolution per DEM chunk. Default: 1024.
          --local-source-path <path>
                               Required when --source local is used. Mirrors the Unity SDK LocalSourcePath naming.
          --server-url <url>     Required when --source remote is used. Absolute direct .zip/.7z CityGML archive URL. Mirrors the Unity SDK ServerUrl naming.
          --work-root <path>     Optional. Parent directory for dataset-local archive storage and live temporary files. Default: local.
          --resonitelink-port    Required unless --resonitelink-url is used. Connect to ws://localhost:<port>/ and build live in Resonite.
          --resonitelink-url     Required unless --resonitelink-port is used. Absolute ws:// or wss:// endpoint for live ResoniteLink builds.
          --resonitelink-connections <count>
                                                             Optional. Number of parallel ResoniteLink connections for live sends. Default: 4.
          --send-metrics         Optional. Enable opt-in live send metrics and CLI summary output.
          --verbose              Optional. Include debug-level progress logs.
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
        string workRoot = "local";
        Uri? resoniteLinkUri = null;
        int resoniteLinkConnectionCount = CliDefaultOptions.ResoniteLinkConnectionCount;
        bool enableSendMetrics = false;
        bool verboseLogging = false;
        DatasetSourceKind sourceKind = DatasetSourceKind.Local;
        Uri? serverUri = null;
        IReadOnlyList<string> packageNames = DefaultPackageNames;
        IReadOnlySet<int>? globalExcludeLods = null;
        IReadOnlyDictionary<string, IReadOnlySet<int>>? packageExcludeLods = null;
        bool hasPackageExcludeLodsOption = false;
        bool includeMarkingAlways = true;
        Dictionary<string, string>? packagePatterns = null;
        DemTerrainMode demTerrainMode = DemTerrainMode.Mesh;
        double demHeightmapMetersPerVertex = 2.0;
        int demHeightmapMaxResolution = 1024;

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

                            string portValue = ReadValue(args, ref index, token, IsSignedIntegerValue);
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
                            string connectionCountValue = ReadValue(args, ref index, token, IsSignedIntegerValue);
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
                    case "--verbose":
                        verboseLogging = true;
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
                    case "--dem-terrain-mode":
                        {
                            string demTerrainModeValue = ReadValue(args, ref index, token);
                            if (string.Equals(demTerrainModeValue, nameof(DemTerrainMode.Mesh), StringComparison.OrdinalIgnoreCase))
                            {
                                demTerrainMode = DemTerrainMode.Mesh;
                            }
                            else if (string.Equals(demTerrainModeValue, "heightmap", StringComparison.OrdinalIgnoreCase))
                            {
                                demTerrainMode = DemTerrainMode.HeightMap;
                            }
                            else
                            {
                                return CliParseResult.Failure(
                                    $"Unsupported DEM terrain mode '{demTerrainModeValue}'. Use 'mesh' or 'heightmap'.");
                            }

                            break;
                        }
                    case "--dem-heightmap-meters-per-vertex":
                        {
                            string metersPerVertexValue = ReadValue(args, ref index, token, IsSignedDecimalValue);
                            if (!double.TryParse(
                                    metersPerVertexValue,
                                    System.Globalization.NumberStyles.Float | System.Globalization.NumberStyles.AllowThousands,
                                    System.Globalization.CultureInfo.InvariantCulture,
                                    out demHeightmapMetersPerVertex)
                                || demHeightmapMetersPerVertex <= 0.0)
                            {
                                return CliParseResult.Failure(
                                    $"The value '{metersPerVertexValue}' is not a valid positive DEM heightmap meters-per-vertex value.");
                            }

                            break;
                        }
                    case "--dem-heightmap-max-resolution":
                        {
                            string maxResolutionValue = ReadValue(args, ref index, token, IsSignedIntegerValue);
                            if (!int.TryParse(maxResolutionValue, out demHeightmapMaxResolution)
                                || demHeightmapMaxResolution < 2)
                            {
                                return CliParseResult.Failure(
                                    $"The value '{maxResolutionValue}' is not a valid DEM heightmap max resolution.");
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

                            if (!LooksLikeSupportedArchiveUri(serverUri))
                            {
                                return CliParseResult.Failure(
                                    "The --server-url value must point directly to a .zip or .7z CityGML archive over http or https.");
                            }

                            break;
                        }
                    case "--exclude-lod":
                        {
                            string excludeLodValue = ReadValue(args, ref index, token);
                            if (!TryParseLodExclusionRules(excludeLodValue, out globalExcludeLods, out string? lodError))
                            {
                                return CliParseResult.Failure(lodError!);
                            }

                            break;
                        }
                    case "--exclude-lod-for-package":
                        {
                            hasPackageExcludeLodsOption = true;
                            string excludeLodPackageValue = ReadValue(args, ref index, token);
                            if (!TryParsePackageSpecificLodExclusions(excludeLodPackageValue, out packageExcludeLods, out string? packageLodError))
                            {
                                return CliParseResult.Failure(packageLodError!);
                            }

                            break;
                        }
                    case "--include-marking":
                        {
                            string markingValue = ReadValue(args, ref index, token);
                            if (!bool.TryParse(markingValue, out includeMarkingAlways))
                            {
                                return CliParseResult.Failure(
                                    $"The value '{markingValue}' is not a valid boolean. Use 'true' or 'false'.");
                            }

                            break;
                        }
                    default:
                        {
                            if (TryParsePackagePatternOption(token, args, ref index, out string? normalizedPackageName, out string? patternValue))
                            {
                                packagePatterns ??= new(StringComparer.OrdinalIgnoreCase);
                                packagePatterns[normalizedPackageName!] = patternValue!;
                                break;
                            }

                            return CliParseResult.Failure($"Unknown option '{token}'.");
                        }
                }
            }
        }
        catch (ArgumentException exception)
        {
            return CliParseResult.Failure(exception.Message);
        }

        if (!hasPackageExcludeLodsOption)
        {
            packageExcludeLods = new Dictionary<string, IReadOnlySet<int>>(StringComparer.OrdinalIgnoreCase)
            {
                ["tran"] = new HashSet<int> { 1 }
            };
        }

        PlateauImportSource source = sourceKind switch
        {
            DatasetSourceKind.Local => new PlateauLocalImportSource(localSourcePath),
            DatasetSourceKind.Remote => new PlateauRemoteImportSource(serverUri),
            _ => throw new InvalidOperationException($"Unsupported dataset source kind '{sourceKind}'."),
        };

        PlateauImportRequest request = new(
            Dataset: dataset ?? string.Empty,
            MeshCode: meshCode ?? string.Empty,
            Source: source,
            PackageNames: packageNames,
            GlobalExcludeLodLevels: globalExcludeLods,
            ExcludeLodLevelsByPackage: packageExcludeLods,
            PackagePatterns: packagePatterns,
            IncludeMarkingAlways: includeMarkingAlways,
            DemTerrainMode: demTerrainMode,
            DemHeightmapMetersPerVertex: demHeightmapMetersPerVertex,
            DemHeightmapMaxResolution: demHeightmapMaxResolution);

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
                enableSendMetrics,
                verboseLogging));
    }

    private static string ReadValue(
        string[] args,
        ref int index,
        string optionName,
        Func<string, bool>? isClearlyNumericValue = null)
    {
        if (index + 1 >= args.Length)
        {
            throw new ArgumentException($"A value is required after '{optionName}'.");
        }

        string value = args[index + 1];
        if (value.StartsWith('-') && (isClearlyNumericValue is null || !isClearlyNumericValue(value)))
        {
            throw new ArgumentException($"A value is required after '{optionName}'.");
        }

        index++;
        return value;
    }

    private static bool IsSignedIntegerValue(string value)
    {
        return int.TryParse(
            value,
            System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture,
            out _);
    }

    private static bool IsSignedDecimalValue(string value)
    {
        return double.TryParse(
            value,
            System.Globalization.NumberStyles.Float | System.Globalization.NumberStyles.AllowThousands,
            System.Globalization.CultureInfo.InvariantCulture,
            out _);
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

    private static bool TryParseLodExclusionRules(
        string? csvValue,
        out IReadOnlySet<int>? lodLevels,
        out string? error)
    {
        lodLevels = null;
        error = null;

        if (string.IsNullOrWhiteSpace(csvValue))
        {
            return true;
        }

        string[] values = csvValue
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        HashSet<int> parsedLods = new();

        foreach (string value in values)
        {
            if (!int.TryParse(value, out int lod) || lod < 0)
            {
                error = $"Invalid LOD level '{value}'. Must be a non-negative integer.";
                return false;
            }

            parsedLods.Add(lod);
        }

        lodLevels = parsedLods.Count > 0 ? parsedLods : null;
        return true;
    }

    private static bool TryParsePackageSpecificLodExclusions(
        string? csvValue,
        out IReadOnlyDictionary<string, IReadOnlySet<int>>? exclusionMap,
        out string? error)
    {
        exclusionMap = null;
        error = null;

        if (string.IsNullOrWhiteSpace(csvValue))
        {
            return true;
        }

        string[] pairs = csvValue
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        Dictionary<string, HashSet<int>> map = new(StringComparer.OrdinalIgnoreCase);

        foreach (string pair in pairs)
        {
            string[] parts = pair.Split(':', StringSplitOptions.TrimEntries);

            if (parts.Length != 2)
            {
                error = $"Invalid package:lod format '{pair}'. Expected 'package:lod'.";
                return false;
            }

            string packageName = parts[0];
            string lodStr = parts[1];

            if (!PlateauPackageCatalog.TryNormalizePackageName(packageName, out string? normalizedName))
            {
                error = $"Unsupported package '{packageName}' in --exclude-lod-for-package.";
                return false;
            }

            if (!map.TryGetValue(normalizedName!, out HashSet<int>? lodSet))
            {
                lodSet = new HashSet<int>();
                map[normalizedName!] = lodSet;
            }

            if (string.IsNullOrWhiteSpace(lodStr)
                || string.Equals(lodStr, "none", StringComparison.OrdinalIgnoreCase))
            {
                lodSet.Clear();
                continue;
            }

            if (!int.TryParse(lodStr, out int lod) || lod < 0)
            {
                error = $"Invalid LOD level '{lodStr}' for package '{packageName}'. Must be a non-negative integer or 'none'.";
                return false;
            }

            lodSet.Add(lod);
        }

        Dictionary<string, IReadOnlySet<int>> normalizedMap = map
            .ToDictionary(
                static pair => pair.Key,
                static pair => (IReadOnlySet<int>)pair.Value,
                StringComparer.OrdinalIgnoreCase);

        exclusionMap = normalizedMap.Count > 0 ? normalizedMap : null;
        return true;
    }

    private static bool TryParsePackagePatternOption(
        string token,
        string[] args,
        ref int index,
        out string? normalizedPackageName,
        out string? patternValue)
    {
        normalizedPackageName = null;
        patternValue = null;

        const string suffix = "-pattern";
        if (!token.StartsWith("--", StringComparison.Ordinal)
            || !token.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string packageName = token[2..^suffix.Length];
        if (!PlateauPackageCatalog.TryNormalizePackageName(packageName, out string? normalized))
        {
            return false;
        }

        normalizedPackageName = normalized;
        patternValue = ReadValue(args, ref index, token);
        return true;
    }

    private static bool LooksLikeSupportedArchiveUri(Uri serverUri)
    {
        if (!string.Equals(serverUri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(serverUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string extension = Path.GetExtension(serverUri.AbsolutePath);
        return string.Equals(extension, ".zip", StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, ".7z", StringComparison.OrdinalIgnoreCase);
    }

}
