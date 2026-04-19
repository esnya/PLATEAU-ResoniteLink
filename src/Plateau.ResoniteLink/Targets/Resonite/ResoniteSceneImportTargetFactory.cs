using System.Diagnostics.CodeAnalysis;

using Plateau.ResoniteLink.Application.Importing;
using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Targets.Resonite;

public static class ResoniteSceneImportTargetFactory
{
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "ResoniteLiveSceneImportTarget owns the client session lifetime.")]
    public static ISceneImportTarget Create(
        Uri endpoint,
        int connectionCount,
        bool enableSendMetrics,
        PlateauImportMemoryProfile memoryProfile,
        bool enableMeshBake,
        string? terrainTileCacheRoot,
        bool disableTerrainTileCache,
        HttpClient terrainTextureAssetHttpClient,
        Action<string>? progressReporter = null)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(terrainTextureAssetHttpClient);

        ResoniteLinkSendDiagnostics diagnostics = enableSendMetrics
            ? ResoniteLinkSendDiagnostics.CreateEnabled(progressReporter)
            : ResoniteLinkSendDiagnostics.Disabled;

        return new ResoniteLiveSceneImportTarget(
            new ResoniteLiveSceneImportTargetOptions(
                endpoint,
                connectionCount,
                diagnostics != ResoniteLinkSendDiagnostics.Disabled,
                memoryProfile,
                enableMeshBake,
                terrainTileCacheRoot,
                disableTerrainTileCache,
                progressReporter),
            new ResoniteLiveSceneImportDependencies(
                Transport.ResoniteLink.ResoniteLinkTransportSessionFactory.Create(
                    endpoint,
                    connectionCount,
                    diagnostics,
                    progressReporter),
                new TerrainTextureAssetGenerator(
                    terrainTextureAssetHttpClient,
                    terrainTileCacheRoot,
                    disableTerrainTileCache),
                new Execution.ResoniteSceneBootstrapInterpreter(new Execution.ResoniteSceneSlotLocator()),
                new Execution.ResoniteGeometryAssetAssembler(),
                new Execution.ResoniteMaterialPlanning(),
                new Execution.ResoniteBatchEmissionPlanner(),
                new Execution.PlannedBatchEmissionInterpreter(),
                new Execution.ResoniteSlotCreator(),
                new ResoniteBufferedCityObjectBakerFactory()));
    }
}
