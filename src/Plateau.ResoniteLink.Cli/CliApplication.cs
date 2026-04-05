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
                parseResult.Options.OutputRoot,
                cancellationToken);

            await standardOutput.WriteLineAsync("Resonite construction plan generated.");
            await standardOutput.WriteLineAsync($"World: {result.Plan.WorldName}");

            foreach (string destination in result.Destinations)
            {
                await standardOutput.WriteLineAsync($"Destination: {destination}");
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

    private static PlateauImportService CreateImportService(BuildCommandOptions options)
    {
        List<IResoniteSceneBuilder> builders =
        [
            new JsonArtifactResoniteSceneBuilder(),
        ];

        if (options.ResoniteLinkUri is not null)
        {
            builders.Add(new ResoniteLinkSceneBuilder(options.ResoniteLinkUri));
        }

        IResoniteSceneBuilder builder = builders.Count == 1
            ? builders[0]
            : new CompositeResoniteSceneBuilder(builders);

        return new PlateauImportService(builder);
    }
}
