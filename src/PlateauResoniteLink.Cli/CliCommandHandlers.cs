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
using PlateauResoniteLink.Diagnostics;
using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Cli;

internal sealed record CliConsoleWriters(
    TextWriter StandardOutput,
    TextWriter StandardError);

internal interface IImportCommandHandler
{
    Task<int> ExecuteAsync(
        PlateauImportRequest request,
        ImportRunCliOptions runOptions,
        ImportSinkCliOptions sinkOptions,
        ResoniteSceneBuildCliOptions sceneBuildOptions,
        CliDiagnosticsOptions diagnosticsOptions,
        CancellationToken cancellationToken);
}

internal interface ISearchCommandHandler
{
    Task<int> ExecuteAsync(
        SearchCommandOptions options,
        CancellationToken cancellationToken);
}

internal interface IStatsCommandHandler
{
    Task<int> ExecuteAsync(
        StatsCommandOptions options,
        CancellationToken cancellationToken);
}

internal sealed class DefaultImportCommandHandler(
    CliConsoleWriters consoleWriters,
    IImportServiceFactory importServiceFactory) : IImportCommandHandler
{
    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "The CLI entrypoint converts non-cancellation operational failures into a concise error message and exit code.")]
    public async Task<int> ExecuteAsync(
        PlateauImportRequest request,
        ImportRunCliOptions runOptions,
        ImportSinkCliOptions sinkOptions,
        ResoniteSceneBuildCliOptions sceneBuildOptions,
        CliDiagnosticsOptions diagnosticsOptions,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(runOptions);
        ArgumentNullException.ThrowIfNull(sinkOptions);
        ArgumentNullException.ThrowIfNull(sceneBuildOptions);
        ArgumentNullException.ThrowIfNull(diagnosticsOptions);

        try
        {
            string diagnosticsRunId = Guid.NewGuid().ToString("N");
            using IDisposable diagnosticsRun = PlateauDiagnostics.BeginRun(diagnosticsRunId);
            using CliProgressEventListener progressListener = new(
                consoleWriters.StandardError,
                diagnosticsOptions.VerboseLogging,
                diagnosticsRunId);
            PlateauImportService importService = importServiceFactory.Create(
                sinkOptions,
                sceneBuildOptions);

            ImportExecutionResult result = await importService.ExecuteAsync(
                request,
                runOptions.WorkRoot,
                cancellationToken);

            bool canonicalDumpMode = sinkOptions is CanonicalSceneDumpSinkCliOptions;
            await consoleWriters.StandardOutput.WriteLineAsync(canonicalDumpMode
                ? "Canonical scene dump completed."
                : "Resonite import completed.");
            if (sinkOptions is CanonicalSceneDumpSinkCliOptions canonicalDump)
            {
                await consoleWriters.StandardOutput.WriteLineAsync($"Dump: {Path.GetFullPath(canonicalDump.OutputPath)}");
            }

            await consoleWriters.StandardOutput.WriteLineAsync($"World: {result.Metadata.SceneName}");

            if (!canonicalDumpMode)
            {
                foreach (string destination in result.Destinations)
                {
                    await consoleWriters.StandardOutput.WriteLineAsync($"Resonite location: {destination}");
                }
            }

            await WriteDataSourceUsagesAsync(result.DataSourceUsages, cancellationToken);

            return 0;
        }
        catch (PlateauImportValidationException exception)
        {
            foreach (string error in exception.Errors)
            {
                await consoleWriters.StandardError.WriteLineAsync(error);
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
                await consoleWriters.StandardError.WriteLineAsync($"Import failed: {innerExceptions.Length} errors occurred.");
                for (int index = 0; index < innerExceptions.Length; index++)
                {
                    await consoleWriters.StandardError.WriteLineAsync($"[{index + 1}] {innerExceptions[index].Message}");
                }

                return;
            }

            if (innerExceptions.Length == 1)
            {
                await consoleWriters.StandardError.WriteLineAsync($"Import failed: {innerExceptions[0].Message}");
                return;
            }
        }

        await consoleWriters.StandardError.WriteLineAsync($"Import failed: {exception.Message}");
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

        await consoleWriters.StandardOutput.WriteLineAsync("Data sources:");
        foreach (IGrouping<ImportDataSourceCategory, ImportDataSourceUsage> categoryGroup in usagesByCategory)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await consoleWriters.StandardOutput.WriteLineAsync($"  {FormatDataSourceCategory(categoryGroup.Key)}:");
            foreach (ImportDataSourceUsage usage in categoryGroup.OrderBy(static usage => usage.Description, StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                await consoleWriters.StandardOutput.WriteLineAsync($"    {usage.Description} ({usage.UsedCount.ToString(CultureInfo.InvariantCulture)})");
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
}

internal sealed class DefaultSearchCommandHandler(
    CliConsoleWriters consoleWriters,
    DatasetInspectionService datasetInspectionService) : ISearchCommandHandler
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "The CLI entrypoint converts non-cancellation operational failures into a concise error message and exit code.")]
    public async Task<int> ExecuteAsync(
        SearchCommandOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);

        try
        {
            DatasetSearchResult result = await datasetInspectionService.SearchAsync(
                options.CityGmlSourcePath,
                options.MeshCode,
                options.PackageNames,
                cancellationToken);
            await WriteSearchResultAsync(result, options.OutputFormat, cancellationToken);
            return 0;
        }
        catch (PlateauImportValidationException exception)
        {
            await CliFailureFormatting.WriteValidationErrorsAsync(consoleWriters.StandardError, exception);
            return 1;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            await consoleWriters.StandardError.WriteLineAsync($"Search failed: {exception.Message}");
            return 1;
        }
    }

    private async Task WriteSearchResultAsync(
        DatasetSearchResult result,
        CliOutputFormat outputFormat,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (outputFormat == CliOutputFormat.Json)
        {
            await consoleWriters.StandardOutput.WriteLineAsync(JsonSerializer.Serialize(result, JsonOptions));
            return;
        }

        await consoleWriters.StandardOutput.WriteLineAsync($"Selected mesh-codes: {CliResultFormatting.FormatCsv(result.SelectedMeshCodes)}");
        await consoleWriters.StandardOutput.WriteLineAsync($"Matched CityGML source files: {result.SourceFiles.Count}");

        foreach (DatasetSearchEntry entry in result.SourceFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await consoleWriters.StandardOutput.WriteLineAsync(
                $"{entry.RelativePath} | package={entry.PackageName} | matched={entry.MatchedMeshCode} | requiresMeshCodeBoundsFilter={entry.RequiresMeshCodeBoundsFilter.ToString(CultureInfo.InvariantCulture).ToLowerInvariant()}");
        }
    }
}

internal sealed class DefaultStatsCommandHandler(
    CliConsoleWriters consoleWriters,
    DatasetInspectionService datasetInspectionService) : IStatsCommandHandler
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "The CLI entrypoint converts non-cancellation operational failures into a concise error message and exit code.")]
    public async Task<int> ExecuteAsync(
        StatsCommandOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);

        try
        {
            DatasetStatsResult result = await datasetInspectionService.GetStatsAsync(
                options.CityGmlSourcePath,
                options.PackageNames,
                cancellationToken);
            await WriteStatsResultAsync(result, options.OutputFormat, cancellationToken);
            return 0;
        }
        catch (PlateauImportValidationException exception)
        {
            await CliFailureFormatting.WriteValidationErrorsAsync(consoleWriters.StandardError, exception);
            return 1;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            await consoleWriters.StandardError.WriteLineAsync($"Stats failed: {exception.Message}");
            return 1;
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
            await consoleWriters.StandardOutput.WriteLineAsync(JsonSerializer.Serialize(result, JsonOptions));
            return;
        }

        await consoleWriters.StandardOutput.WriteLineAsync($"Recognized CityGML source files: {result.RecognizedSourceFileCount}");
        await consoleWriters.StandardOutput.WriteLineAsync($"Package counts: {CliResultFormatting.FormatCounts(result.PackageCounts)}");
        await consoleWriters.StandardOutput.WriteLineAsync($"Mesh-code counts: {CliResultFormatting.FormatCounts(result.MeshCodeCounts)}");
        await consoleWriters.StandardOutput.WriteLineAsync($"LOD coverage counts: {CliResultFormatting.FormatCounts(result.LodCoverageCounts)}");
        await consoleWriters.StandardOutput.WriteLineAsync($"Files without detected LOD: {result.FilesWithoutDetectedLod}");
        await consoleWriters.StandardOutput.WriteLineAsync(
            $"Renderer texture VRAM: {CliResultFormatting.FormatBytes(result.ArchiveVramEstimate.RendererTextureVram.RendererTotalBytes)} "
            + $"(BC1={CliResultFormatting.FormatBytes(result.ArchiveVramEstimate.RendererTextureVram.Bc1Bytes)}, "
            + $"BC3={CliResultFormatting.FormatBytes(result.ArchiveVramEstimate.RendererTextureVram.Bc3Bytes)}, "
            + $"RGBA32 payload upper bound={CliResultFormatting.FormatBytes(result.ArchiveVramEstimate.RendererTextureVram.Rgba32PayloadBytes)})");
        await consoleWriters.StandardOutput.WriteLineAsync(
            $"Renderer geometry VRAM: {CliResultFormatting.FormatBytes(result.ArchiveVramEstimate.RendererGeometryVram.RendererBytesMin)}"
            + $"..{CliResultFormatting.FormatBytes(result.ArchiveVramEstimate.RendererGeometryVram.RendererBytesMax)} "
            + $"(positions={result.ArchiveVramEstimate.RendererGeometryVram.PositionCount.ToString(CultureInfo.InvariantCulture)}, "
            + $"triangles={result.ArchiveVramEstimate.RendererGeometryVram.TriangleCount.ToString(CultureInfo.InvariantCulture)})");
        await consoleWriters.StandardOutput.WriteLineAsync(
            $"Renderer total VRAM: {CliResultFormatting.FormatBytes(result.ArchiveVramEstimate.RendererTotalBytesMin)}"
            + $"..{CliResultFormatting.FormatBytes(result.ArchiveVramEstimate.RendererTotalBytesMax)}");
    }
}

internal static class CliResultFormatting
{
    public static string FormatCsv(IReadOnlyList<string> values)
    {
        return values.Count == 0 ? "(none)" : string.Join(", ", values);
    }

    public static string FormatCounts<TKey>(IReadOnlyDictionary<TKey, int> counts)
        where TKey : notnull
    {
        return counts.Count == 0
            ? "(none)"
            : string.Join(
                ", ",
                counts.Select(
                    static pair => $"{pair.Key}={pair.Value.ToString(CultureInfo.InvariantCulture)}"));
    }

    public static string FormatBytes(long bytes)
    {
        const double mib = 1024.0 * 1024.0;
        return $"{(bytes / mib).ToString("0.##", CultureInfo.InvariantCulture)} MiB";
    }
}

internal static class CliFailureFormatting
{
    public static async Task WriteValidationErrorsAsync(TextWriter standardError, PlateauImportValidationException exception)
    {
        ArgumentNullException.ThrowIfNull(standardError);
        ArgumentNullException.ThrowIfNull(exception);

        foreach (string error in exception.Errors)
        {
            await standardError.WriteLineAsync(error);
        }
    }
}
