using System.Diagnostics.CodeAnalysis;
using System.Globalization;

using Plateau.ResoniteLink.Application.Importing;
using Plateau.ResoniteLink.Application.Logging;

namespace Plateau.ResoniteLink.Cli;

public sealed class CliApplication
{
    private readonly TextWriter standardError;
    private readonly TextWriter standardOutput;
    private readonly Func<BuildCommandOptions, PlateauImportService>? importServiceFactory;
    private readonly PlateauImportService? importService;

    public CliApplication(
        TextWriter standardOutput,
        TextWriter standardError,
        PlateauImportService importService)
    {
        ArgumentNullException.ThrowIfNull(standardOutput);
        ArgumentNullException.ThrowIfNull(standardError);
        ArgumentNullException.ThrowIfNull(importService);

        this.standardOutput = standardOutput;
        this.standardError = standardError;
        this.importService = importService;
    }

    public CliApplication(
        TextWriter standardOutput,
        TextWriter standardError,
        Func<BuildCommandOptions, PlateauImportService> importServiceFactory)
    {
        ArgumentNullException.ThrowIfNull(standardOutput);
        ArgumentNullException.ThrowIfNull(standardError);
        ArgumentNullException.ThrowIfNull(importServiceFactory);

        this.standardOutput = standardOutput;
        this.standardError = standardError;
        this.importServiceFactory = importServiceFactory;
    }

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "The CLI entrypoint converts non-cancellation operational failures into a concise error message and exit code.")]
    public async Task<int> RunAsync(string[] args, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(args);

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
            BuildCommandOptions options = parseResult.Options
                ?? throw new InvalidOperationException("Successful CLI parsing must produce build options.");
            PlateauImportService effectiveImportService =
                importService ?? importServiceFactory!(options);

            ImportExecutionResult result = await effectiveImportService.ExecuteAsync(
                options.Request,
                options.WorkRoot,
                cancellationToken);

            await standardOutput.WriteLineAsync(
                options.ExecutionMode is BuildExecutionMode.DryRun
                    ? "Dry run completed."
                    : "Resonite import completed.");
            await standardOutput.WriteLineAsync($"World: {result.Metadata.WorldName}");

            if (options.ExecutionMode is BuildExecutionMode.DryRun)
            {
                await standardOutput.WriteLineAsync("Live Resonite session was not used.");
            }
            else
            {
                foreach (string destination in result.Destinations)
                {
                    await standardOutput.WriteLineAsync($"Resonite location: {destination}");
                }
            }

            return 0;
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

    [SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "PlateauImportService owns the scene builder lifetime and disposes it after each execution.")]
    internal static PlateauImportService CreateImportService(
        BuildCommandOptions options,
        IPlateauDatasetSourceResolver datasetSourceResolver)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(datasetSourceResolver);

        Action<string> reporter = static message =>
        {
            string timestamp = DateTimeOffset.Now.ToString("yyyy-MM-ddTHH:mm:ss.fffzzz", CultureInfo.InvariantCulture);
            WriteLogLine(Console.Out, timestamp, message);
        };
        ResoniteLinkSendDiagnostics diagnostics = options.EnableSendMetrics
            ? ResoniteLinkSendDiagnostics.CreateEnabled(reporter)
            : ResoniteLinkSendDiagnostics.Disabled;
        IResoniteSceneBuilder sceneBuilder = options.ExecutionMode switch
        {
            BuildExecutionMode.DryRun => new DryRunResoniteSceneBuilder(reporter),
            BuildExecutionMode.Live => new ResoniteLinkSceneBuilder(
                options.ResoniteLinkUri
                    ?? throw new InvalidOperationException("Live mode requires a ResoniteLink endpoint."),
                options.ResoniteLinkConnectionCount,
                diagnostics,
                progressReporter: reporter),
            _ => throw new InvalidOperationException($"Unsupported build execution mode '{options.ExecutionMode}'."),
        };

        return PlateauImportService.CreateOwned(
            sceneBuilder,
            datasetSourceResolver: datasetSourceResolver,
            progressReporter: reporter,
            constructionSourceFactory: null);
    }

    private static void WriteLogLine(TextWriter writer, string timestamp, string message)
    {
        string normalizedMessage = PlateauLog.NormalizeLegacyMessage(message);

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
