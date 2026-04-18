using System.Diagnostics.CodeAnalysis;

using Plateau.ResoniteLink.Application.Importing;

namespace Plateau.ResoniteLink.Targets.Resonite;

public static class ResoniteSceneImportTargetFactory
{
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "ResoniteLinkSceneBuilder owns the client session lifetime.")]
    public static ISceneImportTarget Create(
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
                Transport.ResoniteLink.ResoniteLinkTransportSessionFactory.Create(
                    endpoint,
                    connectionCount,
                    diagnostics,
                    progressReporter),
                new TerrainTextureAssetGenerator(terrainTextureAssetHttpClient)),
            enableMeshBake,
            progressReporter);
    }
}
