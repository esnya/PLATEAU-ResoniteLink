using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

using System.CommandLine;
using System.CommandLine.Parsing;

using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Cli;

internal interface ICliRootCommandFactory
{
    RootCommand Create();
}

internal interface ICliCommandProvider
{
    Command Create();
}

internal sealed class CliCommandFactory(IEnumerable<ICliCommandProvider> commandProviders) : ICliRootCommandFactory
{
    public RootCommand Create()
    {
        RootCommand root = new("Import PLATEAU datasets into Resonite-oriented scene targets.");
        foreach (ICliCommandProvider commandProvider in commandProviders)
        {
            root.Subcommands.Add(commandProvider.Create());
        }

        return root;
    }
}

internal sealed class ImportCliCommand(IImportCommandHandler handler) : ICliCommandProvider
{
    public Command Create()
    {
        ImportCommandSymbols symbols = new();
        Command command = new("import", "Import a PLATEAU dataset into a live or diagnostic target.");
        symbols.AddTo(command);
        command.Validators.Add(symbols.Validate);
        command.SetAction(
            async (parseResult, cancellationToken) =>
            {
                CommandResult result = parseResult.CommandResult;
                return await handler.ExecuteAsync(
                    symbols.Request.Bind(result),
                    symbols.Run.Bind(result),
                    symbols.Sink.Bind(result),
                    symbols.SceneBuild.Bind(result),
                    symbols.Diagnostics.Bind(result),
                    cancellationToken);
            });
        return command;
    }
}

internal sealed class SearchCliCommand(ISearchCommandHandler handler) : ICliCommandProvider
{
    public Command Create()
    {
        SearchCommandSymbols symbols = new();
        Command command = new("search", "Search a CityGML source for mesh-code coverage.");
        symbols.AddTo(command);
        command.SetAction(
            async (parseResult, cancellationToken) =>
            {
                return await handler.ExecuteAsync(
                    symbols.Bind(parseResult.CommandResult),
                    cancellationToken);
            });
        return command;
    }
}

internal sealed class StatsCliCommand(IStatsCommandHandler handler) : ICliCommandProvider
{
    public Command Create()
    {
        StatsCommandSymbols symbols = new();
        Command command = new("stats", "Inspect summary statistics for a CityGML source.");
        symbols.AddTo(command);
        command.SetAction(
            async (parseResult, cancellationToken) =>
            {
                return await handler.ExecuteAsync(
                    symbols.Bind(parseResult.CommandResult),
                    cancellationToken);
            });
        return command;
    }
}

internal sealed class ImportCommandSymbols
{
    public ImportRequestSymbols Request { get; } = new();
    public ImportRunSymbols Run { get; } = new();
    public ImportSinkSymbols Sink { get; } = new();
    public ResoniteSceneBuildSymbols SceneBuild { get; } = new();
    public CliDiagnosticsSymbols Diagnostics { get; } = new();

    public void AddTo(Command command)
    {
        Request.AddTo(command);
        Run.AddTo(command);
        Sink.AddTo(command);
        SceneBuild.AddTo(command);
        Diagnostics.AddTo(command);
    }

    public void Validate(CommandResult result)
    {
        Sink.Validate(result);
    }
}

internal sealed class ImportRequestSymbols
{
    private static readonly string[] DefaultPackageNames = CliDefaultOptions.PackageNames;

    public Option<string> Dataset { get; } = new("--dataset")
    {
        Description = "PLATEAU dataset identifier.",
        HelpName = "dataset",
        Required = true,
    };

    public Option<string> MeshCode { get; } = new("--mesh-code")
    {
        Description = "PLATEAU mesh-code or regex to construct in Resonite.",
        HelpName = "mesh-code",
        Required = true,
    };

    public Option<string[]> Packages { get; } = CliPackageOptions.CreatePackagesOption(
        "PLATEAU package name. Repeat the option to specify multiple packages.",
        DefaultPackageNames);

    public Option<string[]> ExcludeLod { get; } = new("--exclude-lod")
    {
        Arity = ArgumentArity.OneOrMore,
        Description = "LOD level to exclude globally. Repeat the option to specify multiple LOD levels.",
        HelpName = "lod",
    };

    public Option<string[]> ExcludeLodForPackage { get; } = new("--exclude-lod-for-package")
    {
        Arity = ArgumentArity.OneOrMore,
        Description = "Package-specific LOD exclusion as package:lod or package:none. Repeat the option to specify multiple rules.",
        HelpName = "package:lod",
    };

    public Option<bool> IncludeMarking { get; } = new("--include-marking")
    {
        Arity = ArgumentArity.ExactlyOne,
        DefaultValueFactory = _ => true,
        Description = "Include generated road markings even when marked for exclusion.",
        HelpName = "true|false",
    };

    public Option<DatasetLocation> CityGmlSource { get; } = new("--citygml-source")
    {
        Arity = ArgumentArity.ExactlyOne,
        CustomParser = CliOptionParsers.ParseDatasetLocation,
        Description = "Local dataset/archive path or absolute direct .zip/.7z CityGML archive URL.",
        HelpName = "path-or-url",
        Required = true,
    };

    public Option<DatasetLocation?> GeoTiffSource { get; } = new("--geotiff-source")
    {
        Arity = ArgumentArity.ExactlyOne,
        CustomParser = CliOptionParsers.ParseOptionalDatasetLocation,
        Description = "Local GeoTIFF file/archive path or absolute .tif/.tiff/.zip/.7z GeoTIFF URL.",
        HelpName = "path-or-url",
    };

    public Option<TerrainMeshMode> TerrainMesh { get; } = new("--terrain-mesh")
    {
        CustomParser = CliOptionParsers.ParseTerrainMeshMode,
        DefaultValueFactory = _ => TerrainMeshMode.Static,
        Description = "Terrain mesh style: static, grid, or dynamic.",
        HelpName = "mode",
    };

    public Option<double> TerrainGridMetersPerVertex { get; } = new("--terrain-grid-meters-per-vertex")
    {
        CustomParser = CliOptionParsers.ParsePositiveDouble,
        DefaultValueFactory = _ => 2.0,
        Description = "Terrain grid sampling spacing in meters.",
        HelpName = "meters",
    };

    public Option<int> TerrainGridMaxResolution { get; } = new("--terrain-grid-max-resolution")
    {
        CustomParser = CliOptionParsers.ParseTerrainGridMaxResolution,
        DefaultValueFactory = _ => 1024,
        Description = "Maximum terrain grid resolution per DEM chunk.",
        HelpName = "pixels",
    };

    public Option<bool> ExcludeGsiTerrainTiles { get; } = new("--exclude-gsi-terrain-tiles")
    {
        Description = "Exclude GSI seamless photo tiles from DEM terrain texture sources.",
    };

    public ImportRequestSymbols()
    {
        ExcludeLod.Validators.Add(CliOptionParsers.ValidateNonNegativeLodLevels);
        ExcludeLodForPackage.Validators.Add(CliOptionParsers.ValidatePackageSpecificLodExclusions);
    }

    public IReadOnlyList<(string PackageName, Option<string> Option)> PackagePatternOptions { get; } =
        CreatePackagePatternOptions();

    public void AddTo(Command command)
    {
        command.Options.Add(Dataset);
        command.Options.Add(MeshCode);
        command.Options.Add(Packages);
        command.Options.Add(ExcludeLod);
        command.Options.Add(ExcludeLodForPackage);
        command.Options.Add(IncludeMarking);
        foreach ((_, Option<string> option) in PackagePatternOptions)
        {
            command.Options.Add(option);
        }

        command.Options.Add(CityGmlSource);
        command.Options.Add(GeoTiffSource);
        command.Options.Add(TerrainMesh);
        command.Options.Add(TerrainGridMetersPerVertex);
        command.Options.Add(TerrainGridMaxResolution);
        command.Options.Add(ExcludeGsiTerrainTiles);
    }

    public PlateauImportRequest Bind(CommandResult result)
    {
        bool hasPackageExcludeLodsOption = result.GetResult(ExcludeLodForPackage) is not null;
        IReadOnlyDictionary<string, IReadOnlySet<int>>? packageExcludeLods = hasPackageExcludeLodsOption
            ? CliOptionParsers.BindPackageSpecificLodExclusions(result.GetValue(ExcludeLodForPackage) ?? [])
            : null;
        if (!hasPackageExcludeLodsOption)
        {
            packageExcludeLods = new Dictionary<string, IReadOnlySet<int>>(StringComparer.OrdinalIgnoreCase)
            {
                ["tran"] = new HashSet<int> { 1 },
            };
        }

        return new PlateauImportRequest(
            Dataset: result.GetValue(Dataset)!,
            MeshCode: result.GetValue(MeshCode)!,
            CityGmlSource: result.GetValue(CityGmlSource)!,
            DemTextureSource: result.GetValue(GeoTiffSource),
            PackageNames: CliOptionValues.GetOptionalCollectionValue(result, Packages, DefaultPackageNames),
            GlobalExcludeLodLevels: BindExcludeLods(result),
            ExcludeLodLevelsByPackage: packageExcludeLods,
            PackagePatterns: ReadPackagePatterns(result),
            IncludeMarkingAlways: CliOptionValues.GetOptionalValue(result, IncludeMarking, true),
            TerrainMeshMode: CliOptionValues.GetOptionalValue(result, TerrainMesh, TerrainMeshMode.Static),
            TerrainGridMetersPerVertex: CliOptionValues.GetOptionalValue(result, TerrainGridMetersPerVertex, 2.0),
            TerrainGridMaxResolution: CliOptionValues.GetOptionalValue(result, TerrainGridMaxResolution, 1024),
            ExcludeGsiTerrainTiles: result.GetValue(ExcludeGsiTerrainTiles));
    }

    private HashSet<int>? BindExcludeLods(CommandResult result)
    {
        return result.GetResult(ExcludeLod) is null
            ? null
            : CliOptionParsers.BindLodLevels(result.GetValue(ExcludeLod) ?? []);
    }

    private Dictionary<string, string>? ReadPackagePatterns(CommandResult result)
    {
        Dictionary<string, string>? packagePatterns = null;
        foreach ((string packageName, Option<string> option) in PackagePatternOptions)
        {
            string? pattern = result.GetValue(option);
            if (pattern is null)
            {
                continue;
            }

            packagePatterns ??= new(StringComparer.OrdinalIgnoreCase);
            packagePatterns[packageName] = pattern;
        }

        return packagePatterns;
    }

    private static (string PackageName, Option<string> Option)[] CreatePackagePatternOptions()
    {
        List<(string PackageName, Option<string> Option)> options = [];
        foreach (string packageName in PlateauPackageCatalog.SupportedPackageNames)
        {
            options.Add((packageName, new Option<string>($"--{packageName}-pattern")
            {
                Description = $"Pattern filter for the {packageName} package.",
                HelpName = "pattern",
            }));
        }

        foreach ((string alias, string packageName) in PlateauPackageCatalog.PackageAliases.OrderBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            options.Add((packageName, new Option<string>($"--{alias}-pattern")
            {
                Description = $"Pattern filter for the {alias} package alias ({packageName}).",
                HelpName = "pattern",
            }));
        }

        return [.. options];
    }
}

internal sealed class ImportRunSymbols
{
    public Option<string> WorkRoot { get; } = new("--work-root")
    {
        DefaultValueFactory = _ => "local",
        Description = "Parent directory for dataset-local archive storage and live temporary files.",
        HelpName = "path",
    };

    public void AddTo(Command command)
    {
        command.Options.Add(WorkRoot);
    }

    public ImportRunCliOptions Bind(CommandResult result)
    {
        return new ImportRunCliOptions(CliOptionValues.GetOptionalValue(result, WorkRoot, "local"));
    }
}

internal sealed class ImportSinkSymbols
{
    public Option<string> CanonicalSceneDump { get; } = new("--canonical-scene-dump")
    {
        Description = "Apply the import to a fake ResoniteLink sink and write the canonical final scene JSON.",
        HelpName = "path",
    };

    public ResoniteLiveTransportSymbols LiveTransport { get; } = new();
    public TerrainTileCacheSymbols TerrainTileCache { get; } = new();

    public void AddTo(Command command)
    {
        command.Options.Add(CanonicalSceneDump);
        LiveTransport.AddTo(command);
        TerrainTileCache.AddTo(command);
    }

    public void Validate(CommandResult result)
    {
        string? canonicalSceneDumpPath = result.GetValue(CanonicalSceneDump);
        if (canonicalSceneDumpPath is not null && string.IsNullOrWhiteSpace(canonicalSceneDumpPath))
        {
            result.AddError("Specify a non-empty --canonical-scene-dump path.");
            return;
        }

        bool hasLiveEndpoint = LiveTransport.Validate(result);
        if (HasParseErrors(result))
        {
            return;
        }

        if (canonicalSceneDumpPath is not null && hasLiveEndpoint)
        {
            result.AddError("Do not specify --resonitelink-port or --resonitelink-url when --canonical-scene-dump is used.");
            return;
        }

        if (canonicalSceneDumpPath is null && !hasLiveEndpoint)
        {
            result.AddError("Specify either --resonitelink-port, --resonitelink-url, or --canonical-scene-dump.");
        }
    }

    private static bool HasParseErrors(CommandResult result)
    {
        if (result.Errors.Any())
        {
            return true;
        }

        foreach (SymbolResult child in result.Children)
        {
            if (child.Errors.Any())
            {
                return true;
            }
        }

        return false;
    }

    public ImportSinkCliOptions Bind(CommandResult result)
    {
        string? canonicalSceneDumpPath = result.GetValue(CanonicalSceneDump);
        return canonicalSceneDumpPath is not null
            ? new CanonicalSceneDumpSinkCliOptions(canonicalSceneDumpPath)
            : new LiveResoniteSinkCliOptions(
                LiveTransport.BindRequired(result),
                TerrainTileCache.Bind(result));
    }
}

internal sealed class ResoniteLiveTransportSymbols
{
    public Option<int?> ResoniteLinkPort { get; } = new("--resonitelink-port")
    {
        Arity = ArgumentArity.ExactlyOne,
        CustomParser = CliOptionParsers.ParseTcpPort,
        Description = "Connect to ws://localhost:<port>/ and import live into Resonite.",
        HelpName = "port",
    };

    public Option<Uri?> ResoniteLinkUrl { get; } = new("--resonitelink-url")
    {
        Arity = ArgumentArity.ExactlyOne,
        CustomParser = CliOptionParsers.ParseResoniteLinkUrl,
        Description = "Absolute ws:// or wss:// endpoint for live ResoniteLink imports.",
        HelpName = "url",
    };

    public Option<int> ResoniteLinkConnections { get; } = new("--resonitelink-connections")
    {
        CustomParser = CliOptionParsers.ParsePositiveInt,
        DefaultValueFactory = _ => CliDefaultOptions.ResoniteLinkConnectionCount,
        Description = "Parallel ResoniteLink connection count for live sends.",
        HelpName = "count",
    };

    public Option<bool> SendMetrics { get; } = new("--send-metrics")
    {
        Description = "Enable opt-in live send metrics and CLI summary output.",
    };

    public void AddTo(Command command)
    {
        command.Options.Add(ResoniteLinkPort);
        command.Options.Add(ResoniteLinkUrl);
        command.Options.Add(ResoniteLinkConnections);
        command.Options.Add(SendMetrics);
    }

    public bool Validate(CommandResult result)
    {
        bool hasPort = result.GetResult(ResoniteLinkPort) is not null;
        bool hasEndpoint = result.GetResult(ResoniteLinkUrl) is not null;
        if (hasPort && hasEndpoint)
        {
            result.AddError("Specify either --resonitelink-port or --resonitelink-url, not both.");
            return true;
        }

        if (hasPort)
        {
            string? value = GetSingleOptionTokenValue(result, ResoniteLinkPort);
            if (value is not null
                && (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedPort)
                    || parsedPort is < 1 or > 65535))
            {
                result.AddError($"The value '{value}' is not a valid TCP port.");
            }
        }

        if (hasEndpoint)
        {
            string? value = GetSingleOptionTokenValue(result, ResoniteLinkUrl);
            if (value is null)
            {
                return true;
            }

            if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? endpoint))
            {
                result.AddError($"The value '{value}' is not a valid absolute URL.");
            }
            else if (!string.Equals(endpoint.Scheme, "ws", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(endpoint.Scheme, "wss", StringComparison.OrdinalIgnoreCase))
            {
                result.AddError("The --resonitelink-url value must use the ws or wss scheme.");
            }
        }

        if (result.GetResult(ResoniteLinkConnections) is not null)
        {
            string? value = GetSingleOptionTokenValue(result, ResoniteLinkConnections);
            if (value is not null
                && (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedConnectionCount)
                    || parsedConnectionCount < 1))
            {
                result.AddError($"The value '{value}' is not a valid ResoniteLink connection count.");
            }
        }

        return hasPort || hasEndpoint;
    }

    public ResoniteLiveTransportCliOptions BindRequired(CommandResult result)
    {
        return BindOptional(result)
            ?? throw new InvalidOperationException("Live ResoniteLink transport options were not specified.");
    }

    public ResoniteLiveTransportCliOptions? BindOptional(CommandResult result)
    {
        int? port = result.GetValue(ResoniteLinkPort);
        Uri? endpoint = result.GetValue(ResoniteLinkUrl);
        if (port is not null && endpoint is not null)
        {
            result.AddError("Specify either --resonitelink-port or --resonitelink-url, not both.");
            return null;
        }

        endpoint ??= port is null
            ? null
            : new Uri($"ws://localhost:{port.Value.ToString(CultureInfo.InvariantCulture)}/", UriKind.Absolute);
        return endpoint is null
            ? null
            : new ResoniteLiveTransportCliOptions(
                endpoint,
                CliOptionValues.GetOptionalValue(
                    result,
                    ResoniteLinkConnections,
                    CliDefaultOptions.ResoniteLinkConnectionCount),
                result.GetValue(SendMetrics));
    }

    private static string? GetSingleOptionTokenValue<T>(CommandResult result, Option<T> option)
    {
        OptionResult? optionResult = result.GetResult(option);
        if (optionResult is null)
        {
            return null;
        }

        if (optionResult.Tokens.Count != 1)
        {
            optionResult.AddError($"Specify exactly one value for {option.Name}.");
            return null;
        }

        return optionResult.Tokens[0].Value;
    }
}

internal sealed class TerrainTileCacheSymbols
{
    public Option<string> TerrainTileCacheRoot { get; } = new("--terrain-tile-cache-root")
    {
        Description = "Override the persistent terrain tile cache root.",
        HelpName = "path",
    };

    public Option<bool> DisableTerrainTileCache { get; } = new("--disable-terrain-tile-cache")
    {
        Description = "Disable persistent terrain tile caching across runs.",
    };

    public void AddTo(Command command)
    {
        command.Options.Add(TerrainTileCacheRoot);
        command.Options.Add(DisableTerrainTileCache);
    }

    public TerrainTileCacheCliOptions Bind(CommandResult result)
    {
        return new TerrainTileCacheCliOptions(
            result.GetValue(TerrainTileCacheRoot),
            result.GetValue(DisableTerrainTileCache));
    }
}

internal sealed class ResoniteSceneBuildSymbols
{
    public Option<PlateauImportMemoryProfile> MemoryProfile { get; } = new("--memory-profile")
    {
        CustomParser = CliOptionParsers.ParseMemoryProfile,
        DefaultValueFactory = _ => PlateauImportMemoryProfile.Large,
        Description = "Texture/import memory budget profile: small or large.",
        HelpName = "profile",
    };

    public Option<bool> NoMeshBake { get; } = new("--no-mesh-bake")
    {
        Description = "Disable fixed-cell mesh baking for eligible LOD1 building city objects.",
    };

    public Option<bool> DistanceCulling { get; } = new("--distance-culling")
    {
        Description = "Enable opt-in live runtime distance culling components.",
    };

    public void AddTo(Command command)
    {
        command.Options.Add(MemoryProfile);
        command.Options.Add(NoMeshBake);
        command.Options.Add(DistanceCulling);
    }

    public ResoniteSceneBuildCliOptions Bind(CommandResult result)
    {
        return new ResoniteSceneBuildCliOptions(
            CliOptionValues.GetOptionalValue(result, MemoryProfile, PlateauImportMemoryProfile.Large),
            EnableMeshBake: !result.GetValue(NoMeshBake),
            EnableDistanceCulling: result.GetValue(DistanceCulling));
    }
}

internal sealed class CliDiagnosticsSymbols
{
    public Option<bool> Verbose { get; } = new("--verbose")
    {
        Description = "Include debug-level progress logs.",
    };

    public void AddTo(Command command)
    {
        command.Options.Add(Verbose);
    }

    public CliDiagnosticsOptions Bind(CommandResult result)
    {
        return new CliDiagnosticsOptions(result.GetValue(Verbose));
    }
}

internal sealed class SearchCommandSymbols
{
    public Option<string> CityGmlSource { get; } = CliInspectionOptions.CreateCityGmlSourceOption();
    public Option<string> MeshCode { get; } = new("--mesh-code")
    {
        Description = "PLATEAU mesh-code or regex to search within the CityGML source.",
        HelpName = "mesh-code",
        Required = true,
    };
    public Option<string[]> Packages { get; } = CliInspectionOptions.CreatePackagesOption();
    public Option<CliOutputFormat> Format { get; } = CliInspectionOptions.CreateFormatOption();

    public void AddTo(Command command)
    {
        command.Options.Add(CityGmlSource);
        command.Options.Add(MeshCode);
        command.Options.Add(Packages);
        command.Options.Add(Format);
    }

    public SearchCommandOptions Bind(CommandResult result)
    {
        return new SearchCommandOptions(
            result.GetValue(CityGmlSource)!,
            result.GetValue(MeshCode)!,
            CliOptionValues.GetSpecifiedCollectionValuesOrNull(result, Packages),
            CliOptionValues.GetOptionalValue(result, Format, CliOutputFormat.Text));
    }
}

internal sealed class StatsCommandSymbols
{
    public Option<string> CityGmlSource { get; } = CliInspectionOptions.CreateCityGmlSourceOption();
    public Option<string[]> Packages { get; } = CliInspectionOptions.CreatePackagesOption();
    public Option<CliOutputFormat> Format { get; } = CliInspectionOptions.CreateFormatOption();

    public void AddTo(Command command)
    {
        command.Options.Add(CityGmlSource);
        command.Options.Add(Packages);
        command.Options.Add(Format);
    }

    public StatsCommandOptions Bind(CommandResult result)
    {
        return new StatsCommandOptions(
            result.GetValue(CityGmlSource)!,
            CliOptionValues.GetSpecifiedCollectionValuesOrNull(result, Packages),
            CliOptionValues.GetOptionalValue(result, Format, CliOutputFormat.Text));
    }
}

internal static class CliInspectionOptions
{
    public static Option<string> CreateCityGmlSourceOption()
    {
        return new Option<string>("--citygml-source")
        {
            Description = "Local dataset directory or .zip/.7z archive to inspect.",
            HelpName = "path",
            Required = true,
        };
    }

    public static Option<string[]> CreatePackagesOption()
    {
        return CliPackageOptions.CreatePackagesOption(
            "Restrict inspection to a PLATEAU package. Repeat the option to specify multiple packages.");
    }

    public static Option<CliOutputFormat> CreateFormatOption()
    {
        return new Option<CliOutputFormat>("--format")
        {
            CustomParser = CliOptionParsers.ParseOutputFormat,
            DefaultValueFactory = _ => CliOutputFormat.Text,
            Description = "Output format: text or json.",
            HelpName = "format",
        };
    }
}

internal static class CliPackageOptions
{
    public static Option<string[]> CreatePackagesOption(string description, string[]? defaultValue = null)
    {
        Option<string[]> option = new("--packages")
        {
            Arity = ArgumentArity.OneOrMore,
            Description = description,
            HelpName = "package",
        };
        option.Validators.Add(CliOptionParsers.ValidatePackageNames);
        if (defaultValue is not null)
        {
            option.DefaultValueFactory = _ => defaultValue;
        }

        return option;
    }
}

internal static class CliOptionValues
{
    public static T GetOptionalValue<T>(CommandResult result, Option<T> option, T defaultValue)
    {
        return result.GetResult(option) is null ? defaultValue : result.GetValue(option)!;
    }

    public static IReadOnlyList<string> GetOptionalCollectionValue(
        CommandResult result,
        Option<string[]> option,
        IReadOnlyList<string> defaultValue)
    {
        return result.GetResult(option) is null
            ? defaultValue
            : CliDelimitedTokenValues.Split(result.GetValue(option) ?? []);
    }

    public static IReadOnlyList<string>? GetSpecifiedCollectionValuesOrNull(
        CommandResult result,
        Option<string[]> option)
    {
        return result.GetResult(option) is null
            ? null
            : CliDelimitedTokenValues.Split(result.GetValue(option) ?? []);
    }
}

internal static class CliOptionParsers
{
    public static void ValidatePackageNames(OptionResult result)
    {
        if (result.Tokens.Count == 0)
        {
            return;
        }

        if (CliDelimitedTokenValues.Split(result.Tokens.Select(static token => token.Value)).Length == 0)
        {
            result.AddError("Specify at least one package name.");
        }
    }

    public static DatasetLocation ParseDatasetLocation(ArgumentResult result)
    {
        return TryParseDatasetLocation(result, out DatasetLocation? location)
            ? location!
            : null!;
    }

    public static DatasetLocation? ParseOptionalDatasetLocation(ArgumentResult result)
    {
        return TryParseDatasetLocation(result, out DatasetLocation? location)
            ? location
            : null;
    }

    public static void ValidateNonNegativeLodLevels(OptionResult result)
    {
        foreach (string lodValue in CliDelimitedTokenValues.Split(result.Tokens.Select(static token => token.Value)))
        {
            if (!int.TryParse(lodValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out int lod) || lod < 0)
            {
                result.AddError($"Invalid LOD level '{lodValue}'. Must be a non-negative integer.");
                return;
            }
        }
    }

    public static void ValidatePackageSpecificLodExclusions(OptionResult result)
    {
        foreach (string pair in CliDelimitedTokenValues.Split(result.Tokens.Select(static token => token.Value)))
        {
            if (!TryParsePackageSpecificLodExclusionPair(
                pair,
                out _,
                out _,
                out string? error))
            {
                result.AddError(error!);
                return;
            }
        }
    }

    public static HashSet<int> BindLodLevels(IReadOnlyList<string> tokens)
    {
        HashSet<int> lodLevels = [];
        foreach (string lodValue in CliDelimitedTokenValues.Split(tokens))
        {
            if (!int.TryParse(lodValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out int lod))
            {
                throw new InvalidOperationException($"Invalid LOD level '{lodValue}'.");
            }

            lodLevels.Add(lod);
        }

        return lodLevels;
    }

    public static IReadOnlyDictionary<string, IReadOnlySet<int>> BindPackageSpecificLodExclusions(
        IReadOnlyList<string> pairs)
    {
        Dictionary<string, HashSet<int>> map = new(StringComparer.OrdinalIgnoreCase);

        foreach (string pair in CliDelimitedTokenValues.Split(pairs))
        {
            if (!TryParsePackageSpecificLodExclusionPair(
                pair,
                out string? packageName,
                out int? lod,
                out string? error))
            {
                throw new InvalidOperationException(error);
            }

            if (!map.TryGetValue(packageName!, out HashSet<int>? lodSet))
            {
                lodSet = [];
                map[packageName!] = lodSet;
            }

            if (lod is null)
            {
                lodSet.Clear();
                continue;
            }

            lodSet.Add(lod.Value);
        }

        return map.ToDictionary(
            static pair => pair.Key,
            static pair => (IReadOnlySet<int>)pair.Value,
            StringComparer.OrdinalIgnoreCase);
    }

    private static bool TryParsePackageSpecificLodExclusionPair(
        string pair,
        out string? packageName,
        out int? lod,
        out string? error)
    {
        string[] parts = pair.Split(':', StringSplitOptions.TrimEntries);
        if (parts.Length != 2)
        {
            packageName = null;
            lod = null;
            error = $"Invalid package:lod format '{pair}'. Expected 'package:lod'.";
            return false;
        }

        packageName = parts[0];
        string lodStr = parts[1];
        if (string.IsNullOrWhiteSpace(lodStr)
            || string.Equals(lodStr, "none", StringComparison.OrdinalIgnoreCase))
        {
            lod = null;
            error = null;
            return true;
        }

        if (!int.TryParse(lodStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedLod)
            || parsedLod < 0)
        {
            lod = null;
            error = $"Invalid LOD level '{lodStr}' for package '{packageName}'. Must be a non-negative integer or 'none'.";
            return false;
        }

        lod = parsedLod;
        error = null;
        return true;
    }

    public static TerrainMeshMode ParseTerrainMeshMode(ArgumentResult result)
    {
        string? value = GetTokenValue(result);
        if (string.Equals(value, "static", StringComparison.OrdinalIgnoreCase))
        {
            return TerrainMeshMode.Static;
        }

        if (string.Equals(value, "grid", StringComparison.OrdinalIgnoreCase))
        {
            return TerrainMeshMode.Grid;
        }

        if (string.Equals(value, "dynamic", StringComparison.OrdinalIgnoreCase))
        {
            return TerrainMeshMode.Dynamic;
        }

        result.AddError($"Unsupported terrain mesh '{value}'. Use 'static', 'grid', or 'dynamic'.");
        return default;
    }

    public static double ParsePositiveDouble(ArgumentResult result)
    {
        string? value = GetTokenValue(result);
        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsedValue)
            && double.IsFinite(parsedValue)
            && parsedValue > 0.0)
        {
            return parsedValue;
        }

        result.AddError($"The value '{value}' is not a valid positive terrain grid meters-per-vertex value.");
        return default;
    }

    public static int ParseTerrainGridMaxResolution(ArgumentResult result)
    {
        string? value = GetTokenValue(result);
        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedValue)
            && parsedValue >= 2)
        {
            return parsedValue;
        }

        result.AddError($"The value '{value}' is not a valid terrain grid max resolution.");
        return default;
    }

    public static int? ParseTcpPort(ArgumentResult result)
    {
        string? value = GetTokenValue(result);
        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedValue)
            && parsedValue is >= 1 and <= 65535)
        {
            return parsedValue;
        }

        result.AddError($"The value '{value}' is not a valid TCP port.");
        return null;
    }

    public static Uri? ParseResoniteLinkUrl(ArgumentResult result)
    {
        string? value = GetTokenValue(result);
        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? endpoint))
        {
            result.AddError($"The value '{value}' is not a valid absolute URL.");
            return null;
        }

        if (!string.Equals(endpoint.Scheme, "ws", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(endpoint.Scheme, "wss", StringComparison.OrdinalIgnoreCase))
        {
            result.AddError("The --resonitelink-url value must use the ws or wss scheme.");
            return null;
        }

        return endpoint;
    }

    public static int ParsePositiveInt(ArgumentResult result)
    {
        string? value = GetTokenValue(result);
        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedValue)
            && parsedValue >= 1)
        {
            return parsedValue;
        }

        result.AddError($"The value '{value}' is not a valid ResoniteLink connection count.");
        return CliDefaultOptions.ResoniteLinkConnectionCount;
    }

    public static PlateauImportMemoryProfile ParseMemoryProfile(ArgumentResult result)
    {
        string? value = GetTokenValue(result);
        if (string.Equals(value, "small", StringComparison.OrdinalIgnoreCase))
        {
            return PlateauImportMemoryProfile.Small;
        }

        if (string.Equals(value, "large", StringComparison.OrdinalIgnoreCase))
        {
            return PlateauImportMemoryProfile.Large;
        }

        result.AddError($"The value '{value}' is not a valid memory profile. Use 'small' or 'large'.");
        return default;
    }

    public static CliOutputFormat ParseOutputFormat(ArgumentResult result)
    {
        string? value = GetTokenValue(result);
        if (string.Equals(value, "text", StringComparison.OrdinalIgnoreCase))
        {
            return CliOutputFormat.Text;
        }

        if (string.Equals(value, "json", StringComparison.OrdinalIgnoreCase))
        {
            return CliOutputFormat.Json;
        }

        result.AddError($"Unsupported output format '{value}'. Use 'text' or 'json'.");
        return default;
    }

    private static bool TryParseDatasetLocation(ArgumentResult result, out DatasetLocation? location)
    {
        string? value = GetTokenValue(result);
        if (string.IsNullOrWhiteSpace(value))
        {
            result.AddError("Specify a non-empty source path or URL.");
            location = null;
            return false;
        }

        string trimmedValue = value.Trim();
        if (Path.IsPathRooted(trimmedValue))
        {
            location = DatasetLocation.Local(trimmedValue);
            return true;
        }

        if (Uri.TryCreate(trimmedValue, UriKind.Absolute, out Uri? absoluteUri))
        {
            if (!string.Equals(absoluteUri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(absoluteUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                result.AddError($"The value '{value}' is not a supported local path or http/https URL.");
                location = null;
                return false;
            }

            location = DatasetLocation.Remote(absoluteUri);
            return true;
        }

        location = DatasetLocation.Local(trimmedValue);
        return true;
    }

    private static string? GetTokenValue(ArgumentResult result)
    {
        return result.Tokens.Count == 0 ? null : result.Tokens[0].Value;
    }
}

internal static class CliDelimitedTokenValues
{
    public static string[] Split(IEnumerable<string> tokens)
    {
        return tokens
            .SelectMany(static token => token.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .ToArray();
    }
}
