using System;
using System.Net.Http;

using PlateauResoniteLink.Transport.ResoniteLink;

namespace PlateauResoniteLink.Targets.Resonite;

internal interface IResoniteLiveSceneImportFactory
{
    ResoniteLiveSceneImportTarget CreateTarget(
        ResoniteLiveSceneImportTargetOptions options,
        HttpClient terrainTextureAssetHttpClient);

    ResoniteLiveSceneImportTarget CreateTarget(
        ResoniteLiveSceneImportTargetOptions options,
        ILiveSendClientSession clientSession,
        ResoniteLinkSendDiagnostics diagnostics,
        ITerrainTextureAssetGenerator terrainTextureAssetGenerator);
}

internal sealed class ResoniteLiveSceneImportFactory(
    IResoniteClientSessionFactory clientSessionFactory,
    ResoniteLiveSendRunSetupPreparer runSetupPreparer,
    NonDemSourceFileBakeEmitterFactory sourceFileBakeEmitterFactory,
    ResonitePreparedCityObjectImporter preparedCityObjectImporter) : IResoniteLiveSceneImportFactory
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
        ILiveSendClientSession clientSession = clientSessionFactory.Create(options, diagnostics);
        ITerrainTextureAssetGenerator terrainTextureAssetGenerator = new TerrainTextureAssetGenerator(
            terrainTextureAssetHttpClient,
            options.TerrainTileCacheRoot,
            options.DisableTerrainTileCache);
        ResoniteLiveSendRunStarter runStarter = CreateRunStarter(terrainTextureAssetGenerator);
        ResoniteLiveSceneImportDependencies dependencies = CreateDependencies(clientSession, diagnostics, runStarter);
        return new ResoniteLiveSceneImportTarget(options, dependencies);
    }

    public ResoniteLiveSceneImportTarget CreateTarget(
        ResoniteLiveSceneImportTargetOptions options,
        ILiveSendClientSession clientSession,
        ResoniteLinkSendDiagnostics diagnostics,
        ITerrainTextureAssetGenerator terrainTextureAssetGenerator)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(clientSession);
        ArgumentNullException.ThrowIfNull(diagnostics);
        ArgumentNullException.ThrowIfNull(terrainTextureAssetGenerator);

        ResoniteLiveSendRunStarter runStarter = CreateRunStarter(terrainTextureAssetGenerator);
        ResoniteLiveSceneImportDependencies dependencies = CreateDependencies(clientSession, diagnostics, runStarter);
        return new ResoniteLiveSceneImportTarget(options, dependencies);
    }

    private ResoniteLiveSendRunStarter CreateRunStarter(ITerrainTextureAssetGenerator terrainTextureAssetGenerator)
    {
        ArgumentNullException.ThrowIfNull(terrainTextureAssetGenerator);

        return new ResoniteLiveSendRunStarter(
            runSetupPreparer,
            sourceFileBakeEmitterFactory,
            new ResoniteLiveSendWorkerLauncher(CreateQueuedCityObjectWorker(terrainTextureAssetGenerator)));
    }

    private ResoniteQueuedCityObjectWorker CreateQueuedCityObjectWorker(
        ITerrainTextureAssetGenerator terrainTextureAssetGenerator)
    {
        ResoniteQueuedTexturePreparer texturePreparer = new(terrainTextureAssetGenerator);
        ResoniteQueuedCityObjectPreparation cityObjectPreparation = new(texturePreparer);
        ResoniteQueuedCityObjectSender queuedCityObjectSender = new(
            cityObjectPreparation,
            preparedCityObjectImporter);
        return new ResoniteQueuedCityObjectWorker(queuedCityObjectSender);
    }

    private static ResoniteLiveSceneImportDependencies CreateDependencies(
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
            new ResoniteLiveSendRunExecutor(runStarter));
    }
}
