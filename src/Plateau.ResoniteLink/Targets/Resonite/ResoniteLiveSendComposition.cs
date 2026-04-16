using Plateau.ResoniteLink.Application.Importing;
using Plateau.ResoniteLink.Cli;

namespace Plateau.ResoniteLink.Targets.Resonite;

public static class ResoniteLiveSendComposition
{
    public static IResoniteSceneBuilder CreateSceneBuilder(
        Uri endpoint,
        int connectionCount,
        bool enableSendMetrics,
        bool enableMeshBake,
        HttpClient terrainTextureAssetHttpClient,
        Action<string>? progressReporter = null)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(terrainTextureAssetHttpClient);

        ResoniteLinkSendDiagnostics diagnostics = enableSendMetrics
            ? ResoniteLinkSendDiagnostics.CreateEnabled(progressReporter)
            : ResoniteLinkSendDiagnostics.Disabled;

        return new ResoniteLinkSceneBuilder(
            endpoint,
            connectionCount,
            diagnostics,
            new ResoniteLinkSceneBuilderDependencies(
                static () => new ResoniteLinkClient(),
                new TerrainTextureAssetGenerator(terrainTextureAssetHttpClient)),
            enableMeshBake,
            progressReporter);
    }
}
