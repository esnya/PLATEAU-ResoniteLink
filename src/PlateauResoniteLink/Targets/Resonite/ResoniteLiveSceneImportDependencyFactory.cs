using System;
using System.Net.Http;

using PlateauResoniteLink.Transport.ResoniteLink;

namespace PlateauResoniteLink.Targets.Resonite;

internal sealed class ResoniteLiveSceneImportDependencyFactory(
    IResoniteClientSessionFactory clientSessionFactory,
    ResoniteLiveSendRunStarterFactory runStarterFactory)
{
    public ResoniteLiveSceneImportDependencies Create(
        ResoniteLiveSceneImportTargetOptions options,
        HttpClient terrainTextureAssetHttpClient)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(terrainTextureAssetHttpClient);
        ResoniteLinkSendDiagnostics diagnostics = options.EnableSendMetrics
            ? ResoniteLinkSendDiagnostics.CreateEnabled(options.ProgressReporter)
            : ResoniteLinkSendDiagnostics.Disabled;

        ILiveSendClientSession clientSession = clientSessionFactory.Create(options, diagnostics);
        ResoniteLiveSendRunStarter runStarter = runStarterFactory.Create(terrainTextureAssetHttpClient, options);

        return Create(options, clientSession, diagnostics, runStarter);
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

    private static ResoniteLiveSceneImportDependencies Create(
        ResoniteLiveSceneImportTargetOptions options,
        ILiveSendClientSession clientSession,
        ResoniteLinkSendDiagnostics diagnostics,
        IResoniteLiveSendRunStarter runStarter)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(clientSession);
        ArgumentNullException.ThrowIfNull(diagnostics);
        ArgumentNullException.ThrowIfNull(runStarter);

        return new ResoniteLiveSceneImportDependencies(
            clientSession,
            diagnostics,
            new ResoniteLiveSendRunExecutor(runStarter));
    }
}
