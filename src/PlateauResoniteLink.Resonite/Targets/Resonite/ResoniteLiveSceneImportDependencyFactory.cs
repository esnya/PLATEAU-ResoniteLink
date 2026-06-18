using System;
using System.Net.Http;


using PlateauResoniteLink.Resonite.Transport.ResoniteLink;

using PlateauResoniteLink.Core;

namespace PlateauResoniteLink.Resonite.Targets.Resonite;

internal sealed class ResoniteLiveSceneImportDependencyFactory(
    IResoniteClientSessionFactory clientSessionFactory,
    ResoniteLiveSendRunStarterFactory runStarterFactory,
    IResoniteLiveSendRunExecutorFactory runExecutorFactory)
{
    public ResoniteLiveSceneImportDependencies Create(
        ResoniteLiveSceneImportTargetOptions options,
        HttpClient terrainTextureAssetHttpClient,
        ITerrainTextureAssetGeneratorFactory terrainTextureAssetGeneratorFactory)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(terrainTextureAssetHttpClient);
        ArgumentNullException.ThrowIfNull(terrainTextureAssetGeneratorFactory);
        ResoniteLinkSendDiagnostics diagnostics = options.EnableSendMetrics
            ? ResoniteLinkSendDiagnostics.CreateEnabled()
            : ResoniteLinkSendDiagnostics.Disabled;

        ILiveSendClientSession clientSession = clientSessionFactory.Create(options, diagnostics);
        ITerrainTextureAssetGenerator terrainTextureAssetGenerator =
            terrainTextureAssetGeneratorFactory.Create(
                terrainTextureAssetHttpClient,
                new TerrainTextureAssetGeneratorOptions(
                    options.TerrainTileCacheRoot,
                    options.DisableTerrainTileCache));

        return Create(options, clientSession, diagnostics, terrainTextureAssetGenerator);
    }

    public ResoniteLiveSceneImportDependencies Create(
        ResoniteLiveSceneImportTargetOptions options,
        ILiveSendClientSession clientSession,
        ResoniteLinkSendDiagnostics diagnostics,
        ITerrainTextureAssetGenerator terrainTextureAssetGenerator)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(clientSession);
        ArgumentNullException.ThrowIfNull(diagnostics);
        ArgumentNullException.ThrowIfNull(terrainTextureAssetGenerator);

        ResoniteLiveSendRunStarter runStarter = runStarterFactory.Create(terrainTextureAssetGenerator);
        return Create(options, clientSession, diagnostics, runStarter);
    }

    private ResoniteLiveSceneImportDependencies Create(
        ResoniteLiveSceneImportTargetOptions options,
        ILiveSendClientSession clientSession,
        ResoniteLinkSendDiagnostics diagnostics,
        ResoniteLiveSendRunStarter runStarter)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(clientSession);
        ArgumentNullException.ThrowIfNull(diagnostics);
        ArgumentNullException.ThrowIfNull(runStarter);

        return new ResoniteLiveSceneImportDependencies(
            clientSession,
            diagnostics,
            runExecutorFactory.Create(runStarter));
    }
}
