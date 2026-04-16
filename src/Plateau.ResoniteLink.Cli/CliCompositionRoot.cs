using System.Diagnostics.CodeAnalysis;
using System.Globalization;

using Plateau.ResoniteLink.Application.Importing;
using Plateau.ResoniteLink.Application.Logging;

namespace Plateau.ResoniteLink.Cli;

public static class CliCompositionRoot
{
    private static readonly HttpClient SharedDatasetResolverHttpClient = new();
    private static readonly HttpClient SharedTerrainTextureAssetHttpClient = new();

    public static CliApplication CreateDefaultApplication()
    {
        return new CliApplication(
            Console.Out,
            Console.Error,
            CreateImportService);
    }

    [SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "PlateauImportService owns the scene builder lifetime and disposes it after each execution.")]
    private static PlateauImportService CreateImportService(BuildCommandOptions options)
    {
        Action<string> reporter = static message =>
        {
        };
        PlateauLogLevel minimumLogLevel = options.VerboseLogging
            ? PlateauLogLevel.Debug
            : PlateauLogLevel.Info;
        reporter = message =>
        {
            string timestamp = DateTimeOffset.Now.ToString("yyyy-MM-ddTHH:mm:ss.fffzzz", CultureInfo.InvariantCulture);
            WriteLogLine(Console.Out, timestamp, message, minimumLogLevel);
        };
        if (options.ResoniteLinkConnectionCount > 1)
        {
            reporter(
                PlateauLog.Warning(
                    "live",
                    $"--resonitelink-connections={options.ResoniteLinkConnectionCount} is experimental. "
                    + "Use the default value 1 for reliable live sends."));
        }
        ResoniteLinkSendDiagnostics diagnostics = options.EnableSendMetrics
            ? ResoniteLinkSendDiagnostics.CreateEnabled(reporter)
            : ResoniteLinkSendDiagnostics.Disabled;

        return new PlateauImportService(
            new ResoniteLinkSceneBuilder(
                options.ResoniteLinkUri!,
                options.ResoniteLinkConnectionCount,
                diagnostics,
                CreateSceneBuilderDependencies(),
                options.EnableMeshBake,
                progressReporter: reporter),
            CreateDatasetSourceResolver(),
            CreateDocumentReader(),
            CreateConstructionSourceFactory(),
            progressReporter: reporter);
    }

    private static CkanPlateauDatasetSourceResolver CreateDatasetSourceResolver()
    {
        return new CkanPlateauDatasetSourceResolver(SharedDatasetResolverHttpClient);
    }

    private static IResoniteConstructionSourceFactory CreateConstructionSourceFactory()
    {
        return PlateauImportApplicationComposition.CreateConstructionSourceFactory();
    }

    private static LocalCityGmlDocumentReader CreateDocumentReader()
    {
        return new LocalCityGmlDocumentReader();
    }

    private static ResoniteLinkSceneBuilderDependencies CreateSceneBuilderDependencies()
    {
        return new ResoniteLinkSceneBuilderDependencies(
            static () => new ResoniteLinkClient(),
            new TerrainTextureAssetGenerator(SharedTerrainTextureAssetHttpClient));
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
