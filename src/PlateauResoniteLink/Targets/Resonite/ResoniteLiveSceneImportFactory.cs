using System;
using System.Net.Http;

using PlateauResoniteLink.Transport.ResoniteLink;

namespace PlateauResoniteLink.Targets.Resonite;

internal sealed class ResoniteLiveSceneImportFactory(
    Func<ResoniteLiveSceneImportTargetOptions, ResoniteLinkSendDiagnostics, ILiveSendClientSession> createClientSession,
    CreateTerrainTextureGenerator createTerrainTextureGenerator,
    CreateResoniteLiveSendRunStarter createRunStarter,
    CreateResoniteLiveSendRunExecutor createRunExecutor,
    ReleaseLiveSendRunResources releaseResources)
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
        GenerateTerrainTexture generateTerrainTexture = createTerrainTextureGenerator(
            terrainTextureAssetHttpClient,
            options.TerrainTileCacheRoot,
            options.DisableTerrainTileCache);
        ResoniteLiveSendRunStarter runStarter = createRunStarter(generateTerrainTexture);
        return CreateTarget(options, clientSession, diagnostics, runStarter);
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

        ResoniteLiveSendRunStarter runStarter = createRunStarter(generateTerrainTexture);
        return CreateTarget(options, clientSession, diagnostics, runStarter);
    }

    private ResoniteLiveSceneImportTarget CreateTarget(
        ResoniteLiveSceneImportTargetOptions options,
        ILiveSendClientSession clientSession,
        ResoniteLinkSendDiagnostics diagnostics,
        ResoniteLiveSendRunStarter runStarter)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(clientSession);
        ArgumentNullException.ThrowIfNull(diagnostics);
        ArgumentNullException.ThrowIfNull(runStarter);

        return new ResoniteLiveSceneImportTarget(
            options,
            clientSession,
            diagnostics,
            createRunExecutor(runStarter),
            releaseResources);
    }
}
