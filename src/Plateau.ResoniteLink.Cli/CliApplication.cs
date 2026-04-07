using System.Diagnostics.CodeAnalysis;
using System.Globalization;

using Plateau.ResoniteLink.Application.Importing;

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
        this.standardOutput = standardOutput;
        this.standardError = standardError;
        this.importService = importService;
    }

    public CliApplication(
        TextWriter standardOutput,
        TextWriter standardError,
        Func<BuildCommandOptions, PlateauImportService> importServiceFactory)
    {
        this.standardOutput = standardOutput;
        this.standardError = standardError;
        this.importServiceFactory = importServiceFactory;
    }

    public static CliApplication CreateDefault()
    {
        return new CliApplication(
            Console.Out,
            Console.Error,
            CreateImportService);
    }

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
            PlateauImportService effectiveImportService =
                importService ?? importServiceFactory!(parseResult.Options!);

            ImportExecutionResult result = await effectiveImportService.ExecuteAsync(
                parseResult.Options!.Request,
                parseResult.Options.WorkRoot,
                cancellationToken);

            await standardOutput.WriteLineAsync("Resonite import completed.");
            await standardOutput.WriteLineAsync($"World: {result.Metadata.WorldName}");

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
    }

    [SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "PlateauImportService owns the scene builder lifetime and disposes it after each execution.")]
    private static PlateauImportService CreateImportService(BuildCommandOptions options)
    {
        Action<string> reporter = static message =>
        {
            string timestamp = DateTimeOffset.Now.ToString("yyyy-MM-ddTHH:mm:ss.fffzzz", CultureInfo.InvariantCulture);
            Console.Out.WriteLine($"[{timestamp}] {message}");
        };
        ResoniteLinkSendDiagnostics diagnostics = options.EnableSendMetrics
            ? ResoniteLinkSendDiagnostics.CreateEnabled(reporter)
            : ResoniteLinkSendDiagnostics.Disabled;

        return new PlateauImportService(
            new ResoniteLinkSceneBuilder(
                options.ResoniteLinkUri!,
                options.ResoniteLinkConnectionCount,
                diagnostics,
                progressReporter: reporter),
            progressReporter: reporter);
    }
}
