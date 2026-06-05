using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using PlateauResoniteLink.Application.Importing;
using PlateauResoniteLink.Application.Logging;

namespace PlateauResoniteLink.Cli;

public sealed class CliApplication
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private readonly TextWriter standardError;
    private readonly TextWriter standardOutput;
    private readonly IImportServiceFactory importServiceFactory;
    private readonly DatasetInspectionService datasetInspectionService;

    internal CliApplication(
        TextWriter standardOutput,
        TextWriter standardError,
        IImportServiceFactory importServiceFactory,
        DatasetInspectionService datasetInspectionService)
    {
        this.standardOutput = standardOutput;
        this.standardError = standardError;
        this.importServiceFactory = importServiceFactory;
        this.datasetInspectionService = datasetInspectionService;
    }

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "The CLI entrypoint converts non-cancellation operational failures into a concise error message and exit code.")]
    public async Task<int> RunAsync(string[] args, CancellationToken cancellationToken = default)
    {
        CliParseResult parseResult = CliArgumentsParser.Parse(args);

        if (parseResult.ShowHelp)
        {
            await standardOutput.WriteLineAsync(CliArgumentsParser.HelpText);
            return 0;
        }

        if (parseResult.Error is not null)
        {
            await standardError.WriteLineAsync(parseResult.Error);
            await standardError.WriteLineAsync();
            await standardError.WriteLineAsync(CliArgumentsParser.HelpText);
            return 1;
        }

        try
        {
            switch (parseResult.Command)
            {
                case ImportCommandOptions options:
                    {
                        Action<string> reporter = CreateReporter(options.VerboseLogging);
                        PlateauImportService effectiveImportService = importServiceFactory.Create(options, reporter);

                        ImportExecutionResult result = await effectiveImportService.ExecuteAsync(
                            options.Request,
                            options.WorkRoot,
                            cancellationToken);

                        if (options.CanonicalSceneDumpPath is null)
                        {
                            await standardOutput.WriteLineAsync("Resonite import completed.");
                        }
                        else
                        {
                            await standardOutput.WriteLineAsync("Canonical scene dump completed.");
                            await standardOutput.WriteLineAsync($"Dump: {Path.GetFullPath(options.CanonicalSceneDumpPath)}");
                        }

                        await standardOutput.WriteLineAsync($"World: {result.Metadata.SceneName}");

                        if (options.CanonicalSceneDumpPath is null)
                        {
                            foreach (string destination in result.Destinations)
                            {
                                await standardOutput.WriteLineAsync($"Resonite location: {destination}");
                            }
                        }

                        await WriteDataSourceUsagesAsync(result.DataSourceUsages, cancellationToken);

                        return 0;
                    }
                case SearchCommandOptions options:
                    {
                        DatasetSearchResult result = await datasetInspectionService.SearchAsync(
                            options.CityGmlSourcePath,
                            options.MeshCode,
                            options.PackageNames,
                            cancellationToken);
                        await WriteSearchResultAsync(result, options.OutputFormat, cancellationToken);
                        return 0;
                    }
                case StatsCommandOptions options:
                    {
                        DatasetStatsResult result = await datasetInspectionService.GetStatsAsync(
                            options.CityGmlSourcePath,
                            options.PackageNames,
                            cancellationToken);
                        await WriteStatsResultAsync(result, options.OutputFormat, cancellationToken);
                        return 0;
                    }
                default:
                    throw new InvalidOperationException("CLI parse succeeded without a supported command payload.");
            }
        }
        catch (PlateauImportValidationException exception)
        {
            foreach (string error in exception.Errors)
            {
                await standardError.WriteLineAsync(error);
            }

            return 1;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            await WriteImportFailureAsync(exception);
            return 1;
        }
    }

    private async Task WriteImportFailureAsync(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        if (exception is AggregateException aggregateException)
        {
            Exception[] innerExceptions = aggregateException
                .Flatten()
                .InnerExceptions
                .ToArray();
            if (innerExceptions.Length > 1)
            {
                await standardError.WriteLineAsync($"Import failed: {innerExceptions.Length} errors occurred.");
                for (int index = 0; index < innerExceptions.Length; index++)
                {
                    await standardError.WriteLineAsync($"[{index + 1}] {innerExceptions[index].Message}");
                }

                return;
            }

            if (innerExceptions.Length == 1)
            {
                await standardError.WriteLineAsync($"Import failed: {innerExceptions[0].Message}");
                return;
            }
        }

        await standardError.WriteLineAsync($"Import failed: {exception.Message}");
    }

    private async Task WriteSearchResultAsync(
        DatasetSearchResult result,
        CliOutputFormat outputFormat,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (outputFormat == CliOutputFormat.Json)
        {
            await standardOutput.WriteLineAsync(JsonSerializer.Serialize(result, JsonOptions));
            return;
        }

        await standardOutput.WriteLineAsync($"Selected mesh-codes: {FormatCsv(result.SelectedMeshCodes)}");
        await standardOutput.WriteLineAsync($"Matched CityGML source files: {result.SourceFiles.Count}");

        foreach (DatasetSearchEntry entry in result.SourceFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await standardOutput.WriteLineAsync(
                $"{entry.RelativePath} | package={entry.PackageName} | matched={entry.MatchedMeshCode} | requiresMeshCodeBoundsFilter={entry.RequiresMeshCodeBoundsFilter.ToString(CultureInfo.InvariantCulture).ToLowerInvariant()}");
        }
    }

    private async Task WriteStatsResultAsync(
        DatasetStatsResult result,
        CliOutputFormat outputFormat,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (outputFormat == CliOutputFormat.Json)
        {
            await standardOutput.WriteLineAsync(JsonSerializer.Serialize(result, JsonOptions));
            return;
        }

        await standardOutput.WriteLineAsync($"Recognized CityGML source files: {result.RecognizedSourceFileCount}");
        await standardOutput.WriteLineAsync($"Package counts: {FormatCounts(result.PackageCounts)}");
        await standardOutput.WriteLineAsync($"Mesh-code counts: {FormatCounts(result.MeshCodeCounts)}");
        await standardOutput.WriteLineAsync($"LOD coverage counts: {FormatCounts(result.LodCoverageCounts)}");
        await standardOutput.WriteLineAsync($"Files without detected LOD: {result.FilesWithoutDetectedLod}");
        await standardOutput.WriteLineAsync(
            $"Renderer texture VRAM: {FormatBytes(result.ArchiveVramEstimate.RendererTextureVram.RendererTotalBytes)} "
            + $"(BC1={FormatBytes(result.ArchiveVramEstimate.RendererTextureVram.Bc1Bytes)}, "
            + $"BC3={FormatBytes(result.ArchiveVramEstimate.RendererTextureVram.Bc3Bytes)}, "
            + $"RGBA32 payload upper bound={FormatBytes(result.ArchiveVramEstimate.RendererTextureVram.Rgba32PayloadBytes)})");
        await standardOutput.WriteLineAsync(
            $"Renderer geometry VRAM: {FormatBytes(result.ArchiveVramEstimate.RendererGeometryVram.RendererBytesMin)}"
            + $"..{FormatBytes(result.ArchiveVramEstimate.RendererGeometryVram.RendererBytesMax)} "
            + $"(positions={result.ArchiveVramEstimate.RendererGeometryVram.PositionCount.ToString(CultureInfo.InvariantCulture)}, "
            + $"triangles={result.ArchiveVramEstimate.RendererGeometryVram.TriangleCount.ToString(CultureInfo.InvariantCulture)})");
        await standardOutput.WriteLineAsync(
            $"Renderer total VRAM: {FormatBytes(result.ArchiveVramEstimate.RendererTotalBytesMin)}"
            + $"..{FormatBytes(result.ArchiveVramEstimate.RendererTotalBytesMax)}");
    }

    private static string FormatCsv(IReadOnlyList<string> values)
    {
        return values.Count == 0 ? "(none)" : string.Join(", ", values);
    }

    private static string FormatCounts<TKey>(IReadOnlyDictionary<TKey, int> counts)
        where TKey : notnull
    {
        return counts.Count == 0
            ? "(none)"
            : string.Join(
                ", ",
                counts.Select(
                    static pair => $"{pair.Key}={pair.Value.ToString(CultureInfo.InvariantCulture)}"));
    }

    private static string FormatBytes(long bytes)
    {
        const double mib = 1024.0 * 1024.0;
        return $"{(bytes / mib).ToString("0.##", CultureInfo.InvariantCulture)} MiB";
    }

    private async Task WriteDataSourceUsagesAsync(
        IReadOnlyList<ImportDataSourceUsage>? dataSourceUsages,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (dataSourceUsages is not { Count: > 0 })
        {
            return;
        }

        IEnumerable<IGrouping<ImportDataSourceCategory, ImportDataSourceUsage>> usagesByCategory = dataSourceUsages
            .GroupBy(static usage => usage.Category)
            .OrderBy(static group => group.Key);

        await standardOutput.WriteLineAsync("Data sources:");
        foreach (IGrouping<ImportDataSourceCategory, ImportDataSourceUsage> categoryGroup in usagesByCategory)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await standardOutput.WriteLineAsync($"  {FormatDataSourceCategory(categoryGroup.Key)}:");
            foreach (ImportDataSourceUsage usage in categoryGroup.OrderBy(static usage => usage.Description, StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                await standardOutput.WriteLineAsync($"    {usage.Description} ({usage.UsedCount.ToString(CultureInfo.InvariantCulture)})");
            }
        }
    }

    private static string FormatDataSourceCategory(ImportDataSourceCategory category)
    {
        return category switch
        {
            ImportDataSourceCategory.CityGmlSourceFile => "CityGML source files",
            ImportDataSourceCategory.DemTextureSource => "DEM texture sources",
            _ => category.ToString(),
        };
    }

    private Action<string> CreateReporter(PlateauLogLevel minimumLogLevel)
    {
        return message =>
        {
            string timestamp = DateTimeOffset.Now.ToString("yyyy-MM-ddTHH:mm:ss.fffzzz", CultureInfo.InvariantCulture);
            WriteLogLine(standardOutput, timestamp, message, minimumLogLevel);
        };
    }

    private Action<string> CreateReporter(bool verboseLogging)
    {
        return CreateReporter(verboseLogging ? PlateauLogLevel.Debug : PlateauLogLevel.Info);
    }

    private static void WriteLogLine(
        TextWriter writer,
        string timestamp,
        string message,
        PlateauLogLevel minimumLogLevel)
    {
        PlateauLogEntry entry = PlateauLogEntry.TryParse(message, out PlateauLogEntry parsedEntry)
            ? parsedEntry
            : new PlateauLogEntry("app", PlateauLogLevel.Info, message);

        if (entry.Level < minimumLogLevel)
        {
            return;
        }

        if (ReferenceEquals(writer, Console.Out)
            && !Console.IsOutputRedirected
            && PlateauLogEntry.TryParse(message, out _))
        {
            ConsoleColor originalForeground = Console.ForegroundColor;
            Console.Write($"[{timestamp}] ");
            Console.ForegroundColor = GetLogLevelColor(entry.Level);
            Console.Write($"[{entry.Scope}][{entry.LevelToken}]");
            Console.ForegroundColor = originalForeground;
            Console.Write(' ');
            Console.WriteLine(entry.Message);
            return;
        }

        writer.WriteLine($"[{timestamp}] {entry}");
    }

    private static ConsoleColor GetLogLevelColor(PlateauLogLevel level)
    {
        return level switch
        {
            PlateauLogLevel.Debug => ConsoleColor.DarkGray,
            PlateauLogLevel.Info => Console.ForegroundColor,
            PlateauLogLevel.Warning => ConsoleColor.Yellow,
            PlateauLogLevel.Error => ConsoleColor.Red,
            _ => Console.ForegroundColor,
        };
    }
}
