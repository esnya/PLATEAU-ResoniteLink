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
    ResoniteLiveSendRunStarterFactory runStarterFactory,
    IResoniteLiveSendRunExecutorFactory runExecutorFactory) : IResoniteLiveSceneImportFactory
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
        ResoniteLiveSendRunStarter runStarter = runStarterFactory.Create(terrainTextureAssetHttpClient, options);
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

        ResoniteLiveSendRunStarter runStarter = runStarterFactory.Create(terrainTextureAssetGenerator);
        ResoniteLiveSceneImportDependencies dependencies = CreateDependencies(clientSession, diagnostics, runStarter);
        return new ResoniteLiveSceneImportTarget(options, dependencies);
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
