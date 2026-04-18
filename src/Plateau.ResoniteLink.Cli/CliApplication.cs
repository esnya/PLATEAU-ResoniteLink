using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;

using Plateau.ResoniteLink.Application.Importing;
using Plateau.ResoniteLink.Application.Logging;

namespace Plateau.ResoniteLink.Cli;

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
        IImportServiceFactory importServiceFactory)
        : this(
            standardOutput,
            standardError,
            importServiceFactory,
            new DatasetInspectionService())
    {
    }

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
                case BuildCommandOptions options:
                    {
                        Action<string> reporter = CreateReporter(options.VerboseLogging);
                        PlateauImportService effectiveImportService = importServiceFactory.Create(options, reporter);

                        ImportExecutionResult result = await effectiveImportService.ExecuteAsync(
                            options.Request,
                            options.WorkRoot,
                            cancellationToken);

                        await standardOutput.WriteLineAsync("Resonite import completed.");
                        await standardOutput.WriteLineAsync($"World: {result.Metadata.SceneName}");

                        foreach (string destination in result.Destinations)
                        {
                            await standardOutput.WriteLineAsync($"Resonite location: {destination}");
                        }

                        return 0;
                    }
                case SearchCommandOptions options:
                    {
                        DatasetSearchResult result = await datasetInspectionService.SearchAsync(
                            options.LocalSourcePath,
                            options.MeshCode,
                            options.PackageNames,
                            cancellationToken);
                        await WriteSearchResultAsync(result, options.OutputFormat, cancellationToken);
                        return 0;
                    }
                case StatsCommandOptions options:
                    {
                        DatasetStatsResult result = await datasetInspectionService.GetStatsAsync(
                            options.LocalSourcePath,
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
            await standardError.WriteLineAsync($"Import failed: {exception.Message}");
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
            await standardOutput.WriteLineAsync(JsonSerializer.Serialize(result, JsonOptions));
            return;
        }

        await standardOutput.WriteLineAsync($"Requested mesh codes: {FormatCsv(result.RequestedMeshCodes)}");
        await standardOutput.WriteLineAsync($"Matched source files: {result.SourceFiles.Count}");

        foreach (DatasetSearchEntry entry in result.SourceFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await standardOutput.WriteLineAsync(
                $"{entry.RelativePath} | package={entry.PackageName} | matched={entry.MatchedMeshCode} | requiresMeshAreaFilter={entry.RequiresMeshAreaFilter.ToString(CultureInfo.InvariantCulture).ToLowerInvariant()}");
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

        await standardOutput.WriteLineAsync($"Recognized source files: {result.RecognizedSourceFileCount}");
        await standardOutput.WriteLineAsync($"Package counts: {FormatCounts(result.PackageCounts)}");
        await standardOutput.WriteLineAsync($"Mesh code counts: {FormatCounts(result.MeshCodeCounts)}");
        await standardOutput.WriteLineAsync($"LOD coverage counts: {FormatCounts(result.LodCoverageCounts)}");
        await standardOutput.WriteLineAsync($"Files without detected LOD: {result.FilesWithoutDetectedLod}");
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
        string normalizedMessage = PlateauLog.NormalizeLegacyMessage(message, PlateauLog.InferLegacyDefaultLevel(message));

        if (PlateauLogEntry.TryParse(normalizedMessage, out PlateauLogEntry filteredEntry)
            && filteredEntry.Level < minimumLogLevel)
        {
            return;
        }

        if (ReferenceEquals(writer, Console.Out)
            && !Console.IsOutputRedirected
            && PlateauLogEntry.TryParse(normalizedMessage, out PlateauLogEntry entry))
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

        writer.WriteLine($"[{timestamp}] {normalizedMessage}");
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
