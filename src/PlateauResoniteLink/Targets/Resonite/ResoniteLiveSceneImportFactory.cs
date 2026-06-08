using System;
using System.Net.Http;

using PlateauResoniteLink.Transport.ResoniteLink;

namespace PlateauResoniteLink.Targets.Resonite;

internal sealed class ResoniteLiveSceneImportFactory(
    Func<ResoniteLiveSceneImportTargetOptions, ResoniteLinkSendDiagnostics, ILiveSendClientSession> createClientSession,
    IResoniteLiveSendRunSetupPreparer runSetupPreparer,
    NonDemSourceFileBakeEmitterFactory sourceFileBakeEmitterFactory,
    ResonitePreparedCityObjectImporter preparedCityObjectImporter,
    IResoniteLiveSendRunExecutorFactory runExecutorFactory)
{
    public ResoniteLiveSceneImportTarget CreateTarget(
        ResoniteLiveSceneImportTargetOptions options,
        HttpClient terrainTextureAssetHttpClient)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(terrainTextureAssetHttpClient);

        ResoniteLinkSendDiagnostics diagnostics = options.EnableSendMetrics
            ? ResoniteLinkSendDiagnostics.CreateEnabled(options.ProgressReporter)
            : ResoniteLinkSendDiagnostics.Disabled;
        ILiveSendClientSession clientSession = createClientSession(options, diagnostics);
        TerrainTextureAssetGenerator terrainTextureAssetGenerator = new(
            terrainTextureAssetHttpClient,
            options.TerrainTileCacheRoot,
            options.DisableTerrainTileCache);
        ResoniteLiveSendRunStarter runStarter = CreateRunStarter(terrainTextureAssetGenerator.EnsureTextureAsync);
        ResoniteLiveSceneImportDependencies dependencies = CreateDependencies(clientSession, diagnostics, runStarter);
        return new ResoniteLiveSceneImportTarget(options, dependencies);
    }

    public ResoniteLiveSceneImportTarget CreateTarget(
        ResoniteLiveSceneImportTargetOptions options,
        ILiveSendClientSession clientSession,
        ResoniteLinkSendDiagnostics diagnostics,
        GenerateTerrainTexture generateTerrainTexture)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(clientSession);
        ArgumentNullException.ThrowIfNull(diagnostics);
        ArgumentNullException.ThrowIfNull(generateTerrainTexture);

        ResoniteLiveSendRunStarter runStarter = CreateRunStarter(generateTerrainTexture);
        ResoniteLiveSceneImportDependencies dependencies = CreateDependencies(clientSession, diagnostics, runStarter);
        return new ResoniteLiveSceneImportTarget(options, dependencies);
    }

    private ResoniteLiveSendRunStarter CreateRunStarter(GenerateTerrainTexture generateTerrainTexture)
    {
        ArgumentNullException.ThrowIfNull(generateTerrainTexture);

        return new ResoniteLiveSendRunStarter(
            runSetupPreparer,
            sourceFileBakeEmitterFactory,
            CreateQueuedCityObjectWorker(generateTerrainTexture));
    }

    private ResoniteQueuedCityObjectWorker CreateQueuedCityObjectWorker(
        GenerateTerrainTexture generateTerrainTexture)
    {
        ResoniteQueuedCityObjectPreparation cityObjectPreparation = new(generateTerrainTexture);
        return new ResoniteQueuedCityObjectWorker(
            cityObjectPreparation,
            preparedCityObjectImporter);
    }

    private ResoniteLiveSceneImportDependencies CreateDependencies(
        ILiveSendClientSession clientSession,
        ResoniteLinkSendDiagnostics diagnostics,
        ResoniteLiveSendRunStarter runStarter)
    {
        ArgumentNullException.ThrowIfNull(clientSession);
        ArgumentNullException.ThrowIfNull(diagnostics);
        ArgumentNullException.ThrowIfNull(runStarter);

        return new ResoniteLiveSceneImportDependencies(
            clientSession,
            diagnostics,
            runExecutorFactory.Create(runStarter));
    }
}
