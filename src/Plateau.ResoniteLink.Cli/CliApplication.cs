using System.Diagnostics.CodeAnalysis;
using System.Globalization;

using Plateau.ResoniteLink.Application.Importing;
using Plateau.ResoniteLink.Application.Logging;

namespace Plateau.ResoniteLink.Cli;

public sealed class CliApplication
{
    private readonly TextWriter standardError;
    private readonly TextWriter standardOutput;
    private readonly IImportServiceFactory importServiceFactory;

    internal CliApplication(
        TextWriter standardOutput,
        TextWriter standardError,
        IImportServiceFactory importServiceFactory)
    {
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
            BuildCommandOptions options = parseResult.Options!;
            Action<string> reporter = CreateReporter(options.VerboseLogging);
            if (options.ResoniteLinkConnectionCount > 1)
            {
                reporter(
                    PlateauLog.Warning(
                        "live",
                        $"--resonitelink-connections={options.ResoniteLinkConnectionCount} is experimental. "
                        + "Use the default value 1 for reliable live sends."));
            }

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
