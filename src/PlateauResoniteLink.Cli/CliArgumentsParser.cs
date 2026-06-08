using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Cli;

public static class CliArgumentsParser
{
    private static readonly string[] DefaultPackageNames = CliDefaultOptions.PackageNames;

    public const string HelpText =
        """
        PlateauResoniteLink CLI

        Usage:
          plateau-resonitelink import --dataset <dataset> --mesh-code <mesh-code> [options]
          plateau-resonitelink search --citygml-source <path> --mesh-code <mesh-code> [options]
          plateau-resonitelink stats --citygml-source <path> [options]

        Import options:
          --dataset <value>      Required. PLATEAU dataset identifier.
          --mesh-code <value>    Required. PLATEAU mesh-code or regex to construct in Resonite.
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
          --citygml-source <path-or-url>
                                Required. Local dataset/archive path or absolute direct .zip/.7z CityGML archive URL.
          --geotiff-source <path-or-url>
                                Optional. Local GeoTIFF file/archive path or absolute .tif/.tiff/.zip/.7z GeoTIFF URL.
          --terrain-mesh <static|grid|dynamic>
                                Optional. Terrain mesh style. Default: static.
          --terrain-grid-meters-per-vertex <value>
                                Optional. Terrain grid sampling spacing in meters. Default: 2.0.
          --terrain-grid-max-resolution <value>
                                Optional. Maximum terrain grid resolution per DEM chunk. Default: 1024.
          --work-root <path>     Optional. Parent directory for dataset-local archive storage and live temporary files. Default: local.
          --terrain-tile-cache-root <path>
                                Optional. Override the persistent terrain tile cache root.
          --disable-terrain-tile-cache
                                Optional. Disable persistent terrain tile caching across runs.
          --canonical-scene-dump <path>
                                Optional. Developer semantic verification mode. Apply the import to a fake ResoniteLink sink
                                and write the canonical final scene JSON instead of connecting to ResoniteLink.
          --resonitelink-port    Required unless --resonitelink-url is used. Connect to ws://localhost:<port>/ and import live into Resonite.
          --resonitelink-url     Required unless --resonitelink-port is used. Absolute ws:// or wss:// endpoint for live ResoniteLink imports.
          --resonitelink-connections <count>
                                Optional. Parallel ResoniteLink connection count for live sends. Default: 4.
          --memory-profile <small|large>
                                Optional. Texture/import memory budget profile. Default: large.
          --send-metrics         Optional. Enable opt-in live send metrics and CLI summary output.
          --verbose              Optional. Include debug-level progress logs.

        Search/stats options:
          --citygml-source <path>
                                Required. Local dataset directory or .zip/.7z archive to inspect.
          --mesh-code <value>    Required for search. PLATEAU mesh-code or regex to search within the CityGML source.
          --packages <csv>       Optional. Restrict inspection to specific PLATEAU packages.
          --format <text|json>   Optional. Output format. Default: text.

          -h, --help             Show this help text.
        """;

    public static CliParseResult Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        if (args.Length == 0 || args[0] is "-h" or "--help")
        {
            return CliParseResult.Help();
        }

        return args[0].ToLowerInvariant() switch
        {
            "import" => ParseImport(args),
            "search" => ParseSearch(args),
            "stats" => ParseStats(args),
            _ => CliParseResult.Failure($"Unknown command '{args[0]}'."),
        };
    }

    private static CliParseResult ParseImport(string[] args)
    {
        string? dataset = null;
        string? meshCode = null;
        string? cityGmlSourceInput = null;
        string? geotiffSourceInput = null;
        string workRoot = "local";
        string? terrainTileCacheRoot = null;
        bool disableTerrainTileCache = false;
        string? canonicalSceneDumpPath = null;
        Uri? resoniteLinkUri = null;
        int resoniteLinkConnectionCount = CliDefaultOptions.ResoniteLinkConnectionCount;
        PlateauImportMemoryProfile memoryProfile = CliDefaultOptions.MemoryProfile;
        bool enableSendMetrics = false;
        bool verboseLogging = false;
        IReadOnlyList<string> packageNames = DefaultPackageNames;
        IReadOnlySet<int>? globalExcludeLods = null;
        IReadOnlyDictionary<string, IReadOnlySet<int>>? packageExcludeLods = null;
        bool hasPackageExcludeLodsOption = false;
        bool includeMarkingAlways = true;
        Dictionary<string, string>? packagePatterns = null;
        TerrainMeshMode terrainMesh = TerrainMeshMode.Static;
        double terrainGridMetersPerVertex = 2.0;
        int terrainGridMaxResolution = 1024;

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
                            packageNames = ParsePackageNames(packageValue, out string? packageError);
                            if (packageError is not null)
                            {
                                return CliParseResult.Failure(packageError);
                            }

                            break;
                        }
                    case "--citygml-source":
                        cityGmlSourceInput = ReadValue(args, ref index, token);
                        break;
                    case "--geotiff-source":
                        geotiffSourceInput = ReadValue(args, ref index, token);
                        break;
                    case "--work-root":
                        workRoot = ReadValue(args, ref index, token);
                        break;
                    case "--terrain-tile-cache-root":
                        terrainTileCacheRoot = ReadValue(args, ref index, token);
                        break;
                    case "--disable-terrain-tile-cache":
                        disableTerrainTileCache = true;
                        break;
                    case "--canonical-scene-dump":
                        canonicalSceneDumpPath = ReadValue(args, ref index, token);
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
                    case "--memory-profile":
                        {
                            string memoryProfileValue = ReadValue(args, ref index, token);
                            if (!Enum.TryParse(memoryProfileValue, ignoreCase: true, out memoryProfile)
                                || !string.Equals(
                                    Enum.GetName(memoryProfile),
                                    memoryProfileValue,
                                    StringComparison.OrdinalIgnoreCase))
                            {
                                return CliParseResult.Failure(
                                    $"The value '{memoryProfileValue}' is not a valid memory profile. Use 'small' or 'large'.");
                            }

                            break;
                        }
                    case "--send-metrics":
                        enableSendMetrics = true;
                        break;
                    case "--verbose":
                        verboseLogging = true;
                        break;
                    case "--terrain-mesh":
                        {
                            string terrainMeshValue = ReadValue(args, ref index, token);
                            if (string.Equals(terrainMeshValue, "static", StringComparison.OrdinalIgnoreCase))
                            {
                                terrainMesh = TerrainMeshMode.Static;
                            }
                            else if (string.Equals(terrainMeshValue, "grid", StringComparison.OrdinalIgnoreCase))
                            {
                                terrainMesh = TerrainMeshMode.Grid;
                            }
                            else if (string.Equals(terrainMeshValue, "dynamic", StringComparison.OrdinalIgnoreCase))
                            {
                                terrainMesh = TerrainMeshMode.Dynamic;
                            }
                            else
                            {
                                return CliParseResult.Failure(
                                    $"Unsupported terrain mesh '{terrainMeshValue}'. Use 'static', 'grid', or 'dynamic'.");
                            }

                            break;
                        }
                    case "--terrain-grid-meters-per-vertex":
                        {
                            string metersPerVertexValue = ReadValue(args, ref index, token, IsSignedDecimalValue);
                            if (!double.TryParse(
                                    metersPerVertexValue,
                                    System.Globalization.NumberStyles.Float | System.Globalization.NumberStyles.AllowThousands,
                                    System.Globalization.CultureInfo.InvariantCulture,
                                    out terrainGridMetersPerVertex)
                                || !double.IsFinite(terrainGridMetersPerVertex)
                                || terrainGridMetersPerVertex <= 0.0)
                            {
                                return CliParseResult.Failure(
                                    $"The value '{metersPerVertexValue}' is not a valid positive terrain grid meters-per-vertex value.");
                            }

                            break;
                        }
                    case "--terrain-grid-max-resolution":
                        {
                            string maxResolutionValue = ReadValue(args, ref index, token, IsSignedIntegerValue);
                            if (!int.TryParse(maxResolutionValue, out terrainGridMaxResolution)
                                || terrainGridMaxResolution < 2)
                            {
                                return CliParseResult.Failure(
                                    $"The value '{maxResolutionValue}' is not a valid terrain grid max resolution.");
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
                            if (TryParsePackagePatternOption(token, args, ref index, out string? packageName, out string? patternValue))
                            {
                                packagePatterns ??= new(StringComparer.OrdinalIgnoreCase);
                                packagePatterns[packageName!] = patternValue!;
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

        if (string.IsNullOrWhiteSpace(cityGmlSourceInput))
        {
            return CliParseResult.Failure("Specify --citygml-source.");
        }

        if (string.IsNullOrWhiteSpace(dataset))
        {
            return CliParseResult.Failure("Specify --dataset.");
        }

        if (string.IsNullOrWhiteSpace(meshCode))
        {
            return CliParseResult.Failure("Specify --mesh-code.");
        }

        if (!TryParseDatasetLocationInput(cityGmlSourceInput, out DatasetLocation? cityGmlSource, out string? sourceError))
        {
            return CliParseResult.Failure(sourceError!);
        }

        DatasetLocation? demTextureSource = null;
        if (!string.IsNullOrWhiteSpace(geotiffSourceInput)
            && !TryParseDatasetLocationInput(geotiffSourceInput, out demTextureSource, out string? demTextureSourceError))
        {
            return CliParseResult.Failure(demTextureSourceError!);
        }

        if (canonicalSceneDumpPath is not null && string.IsNullOrWhiteSpace(canonicalSceneDumpPath))
        {
            return CliParseResult.Failure("Specify a non-empty --canonical-scene-dump path.");
        }

        PlateauImportRequest request = new(
            Dataset: dataset ?? string.Empty,
            MeshCode: meshCode ?? string.Empty,
            CityGmlSource: cityGmlSource!,
            DemTextureSource: demTextureSource,
            PackageNames: packageNames,
            GlobalExcludeLodLevels: globalExcludeLods,
            ExcludeLodLevelsByPackage: packageExcludeLods,
            PackagePatterns: packagePatterns,
            IncludeMarkingAlways: includeMarkingAlways,
            TerrainMeshMode: terrainMesh,
            TerrainGridMetersPerVertex: terrainGridMetersPerVertex,
            TerrainGridMaxResolution: terrainGridMaxResolution);

        if (canonicalSceneDumpPath is not null && resoniteLinkUri is not null)
        {
            return CliParseResult.Failure(
                "Do not specify --resonitelink-port or --resonitelink-url when --canonical-scene-dump is used.");
        }

        if (resoniteLinkUri is null && canonicalSceneDumpPath is null)
        {
            return CliParseResult.Failure(
                "Specify either --resonitelink-port or --resonitelink-url.");
        }

        return CliParseResult.Success(
            new ImportCommandOptions(
                request,
                workRoot,
                resoniteLinkUri,
                resoniteLinkConnectionCount,
                memoryProfile,
                terrainTileCacheRoot,
                disableTerrainTileCache,
                canonicalSceneDumpPath,
                enableSendMetrics,
                verboseLogging));
    }

    private static CliParseResult ParseSearch(string[] args)
    {
        string? cityGmlSourcePath = null;
        string? meshCode = null;
        IReadOnlyList<string>? packageNames = null;
        CliOutputFormat outputFormat = CliOutputFormat.Text;

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
                    case "--citygml-source":
                        cityGmlSourcePath = ReadValue(args, ref index, token);
                        break;
                    case "--mesh-code":
                        meshCode = ReadValue(args, ref index, token);
                        break;
                    case "--packages":
                        {
                            string packageValue = ReadValue(args, ref index, token);
                            packageNames = ParsePackageNames(packageValue, out string? packageError);
                            if (packageError is not null)
                            {
                                return CliParseResult.Failure(packageError);
                            }

                            break;
                        }
                    case "--format":
                        {
                            string formatValue = ReadValue(args, ref index, token);
                            if (!TryParseOutputFormat(formatValue, out outputFormat))
                            {
                                return CliParseResult.Failure(
                                    $"Unsupported output format '{formatValue}'. Use 'text' or 'json'.");
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

        if (string.IsNullOrWhiteSpace(cityGmlSourcePath))
        {
            return CliParseResult.Failure("Specify --citygml-source.");
        }

        if (string.IsNullOrWhiteSpace(meshCode))
        {
            return CliParseResult.Failure("Specify --mesh-code.");
        }

        return CliParseResult.Success(
            new SearchCommandOptions(cityGmlSourcePath, meshCode, packageNames, outputFormat));
    }

    private static CliParseResult ParseStats(string[] args)
    {
        string? cityGmlSourcePath = null;
        IReadOnlyList<string>? packageNames = null;
        CliOutputFormat outputFormat = CliOutputFormat.Text;

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
                    case "--citygml-source":
                        cityGmlSourcePath = ReadValue(args, ref index, token);
                        break;
                    case "--packages":
                        {
                            string packageValue = ReadValue(args, ref index, token);
                            packageNames = ParsePackageNames(packageValue, out string? packageError);
                            if (packageError is not null)
                            {
                                return CliParseResult.Failure(packageError);
                            }

                            break;
                        }
                    case "--format":
                        {
                            string formatValue = ReadValue(args, ref index, token);
                            if (!TryParseOutputFormat(formatValue, out outputFormat))
                            {
                                return CliParseResult.Failure(
                                    $"Unsupported output format '{formatValue}'. Use 'text' or 'json'.");
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

        if (string.IsNullOrWhiteSpace(cityGmlSourcePath))
        {
            return CliParseResult.Failure("Specify --citygml-source.");
        }

        return CliParseResult.Success(
            new StatsCommandOptions(cityGmlSourcePath, packageNames, outputFormat));
    }

    private static bool TryParseOutputFormat(string value, out CliOutputFormat outputFormat)
    {
        if (string.Equals(value, "text", StringComparison.OrdinalIgnoreCase))
        {
            outputFormat = CliOutputFormat.Text;
            return true;
        }

        if (string.Equals(value, "json", StringComparison.OrdinalIgnoreCase))
        {
            outputFormat = CliOutputFormat.Json;
            return true;
        }

        outputFormat = default;
        return false;
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

    private static string[] ParsePackageNames(
        string csvValue,
        out string? error)
    {
        error = null;

        string[] parsedValues = csvValue
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        if (parsedValues.Length == 0)
        {
            error = "The --packages option requires at least one package name.";
        }

        return parsedValues;
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

        HashSet<int> parsedLods = [];

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

            if (!map.TryGetValue(packageName, out HashSet<int>? lodSet))
            {
                lodSet = [];
                map[packageName] = lodSet;
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

        exclusionMap = map.ToDictionary(
            static pair => pair.Key,
            static pair => (IReadOnlySet<int>)pair.Value,
            StringComparer.OrdinalIgnoreCase);
        return true;
    }

    private static bool TryParsePackagePatternOption(
        string token,
        string[] args,
        ref int index,
        out string? packageName,
        out string? patternValue)
    {
        packageName = null;
        patternValue = null;

        if (!token.StartsWith("--", StringComparison.Ordinal)
            || !token.EndsWith("-pattern", StringComparison.Ordinal)
            || token.Length <= "--".Length + "-pattern".Length)
        {
            return false;
        }

        string requestedPackageName = token["--".Length..^"-pattern".Length];
        packageName = requestedPackageName;
        patternValue = ReadValue(args, ref index, token);
        return true;
    }

    private static bool TryParseDatasetLocationInput(
        string input,
        out DatasetLocation? source,
        out string? error)
    {
        string trimmedInput = input.Trim();
        if (Path.IsPathRooted(trimmedInput))
        {
            source = DatasetLocation.Local(trimmedInput);
            error = null;
            return true;
        }

        if (Uri.TryCreate(trimmedInput, UriKind.Absolute, out Uri? absoluteUri))
        {
            if (!string.Equals(absoluteUri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(absoluteUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                source = null;
                error = $"The value '{input}' is not a supported local path or http/https URL.";
                return false;
            }

            source = DatasetLocation.Remote(absoluteUri);
            error = null;
            return true;
        }

        source = DatasetLocation.Local(trimmedInput);
        error = null;
        return true;
    }
}
