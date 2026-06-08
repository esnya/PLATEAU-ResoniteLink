using System;
using System.Net.Http;

using PlateauResoniteLink.Targets.Resonite.Execution;
using PlateauResoniteLink.Transport.ResoniteLink;

namespace PlateauResoniteLink.Targets.Resonite;

internal sealed class ResoniteLiveSceneImportFactory(
    Func<ResoniteLiveSceneImportTargetOptions, ResoniteLinkSendDiagnostics, ILiveSendClientSession> createClientSession,
    IResoniteLiveSendRunSetupPreparer runSetupPreparer,
    EnsureResoniteGsiFallbackLicense ensureGsiFallbackLicense,
    ResoniteTextureImageLoader textureImageLoader,
    ResonitePreparedCityObjectImporter preparedCityObjectImporter,
    CreateTerrainTextureGenerator createTerrainTextureGenerator,
    IResoniteLiveSendRunExecutorFactory runExecutorFactory)
{
    public ResoniteLiveSceneImportTarget CreateTarget(
        ResoniteLiveSceneImportTargetOptions options,
        HttpClient terrainTextureAssetHttpClient)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(terrainTextureAssetHttpClient);

        Microsoft.Extensions.Logging.ILogger diagnosticsLogger =
            options.LoggerFactory.CreateLogger("PlateauResoniteLink.ResoniteLink");
        ResoniteLinkSendDiagnostics diagnostics = options.EnableSendMetrics
            ? ResoniteLinkSendDiagnostics.CreateEnabled(diagnosticsLogger)
            : ResoniteLinkSendDiagnostics.Disabled;
        ILiveSendClientSession clientSession = createClientSession(options, diagnostics);
        GenerateTerrainTexture generateTerrainTexture =
            createTerrainTextureGenerator(options, terrainTextureAssetHttpClient);
        ResoniteLiveSendRunStarter runStarter = CreateRunStarter(generateTerrainTexture);
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

        ResoniteLiveSendRunStarter runStarter = CreateRunStarter(generateTerrainTexture);
        return CreateTarget(options, clientSession, diagnostics, runStarter);
    }

    private ResoniteLiveSendRunStarter CreateRunStarter(GenerateTerrainTexture generateTerrainTexture)
    {
        ArgumentNullException.ThrowIfNull(generateTerrainTexture);

        return new ResoniteLiveSendRunStarter(
            runSetupPreparer,
            textureImageLoader,
            CreateQueuedCityObjectWorker(generateTerrainTexture));
    }

    private ResoniteQueuedCityObjectWorker CreateQueuedCityObjectWorker(
        GenerateTerrainTexture generateTerrainTexture)
    {
        ResoniteQueuedCityObjectPreparation cityObjectPreparation = new(
            generateTerrainTexture,
            ensureGsiFallbackLicense);
        return new ResoniteQueuedCityObjectWorker(
            cityObjectPreparation,
            preparedCityObjectImporter);
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
            runExecutorFactory.Create(runStarter));
    }
}
