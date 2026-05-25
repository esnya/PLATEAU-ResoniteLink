using System;
using System.Net.Http;

using PlateauResoniteLink.Transport.ResoniteLink;

namespace PlateauResoniteLink.Targets.Resonite;

internal interface IResoniteLiveSceneImportDependencyFactory
{
    ResoniteLiveSceneImportDependencies Create(
        ResoniteLiveSceneImportTargetOptions options,
        HttpClient terrainTextureAssetHttpClient);

    ResoniteLiveSceneImportDependencies Create(
        ResoniteLiveSceneImportTargetOptions options,
        ILiveSendClientSession clientSession,
        ITerrainTextureAssetGenerator terrainTextureAssetGenerator);
}

internal sealed class ResoniteLiveSceneImportDependencyFactory(
    IResoniteClientSessionFactory clientSessionFactory,
    IResoniteLiveSendRunStarterFactory runStarterFactory,
    IResoniteLiveSendStartRequestFactory startRequestFactory,
    IResoniteLiveSendRunExecutorFactory runExecutorFactory,
    IResoniteLiveSendRunResourceReleaser resourceReleaser)
    : IResoniteLiveSceneImportDependencyFactory
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
        IResoniteLiveSendRunStarter runStarter = runStarterFactory.Create(terrainTextureAssetHttpClient, options);

        return Create(options, clientSession, runStarter);
    }

    public ResoniteLiveSceneImportDependencies Create(
        ResoniteLiveSceneImportTargetOptions options,
        ILiveSendClientSession clientSession,
        ITerrainTextureAssetGenerator terrainTextureAssetGenerator)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(clientSession);
        ArgumentNullException.ThrowIfNull(terrainTextureAssetGenerator);

        IResoniteLiveSendRunStarter runStarter = runStarterFactory.Create(terrainTextureAssetGenerator);
        return Create(options, clientSession, runStarter);
    }

    private ResoniteLiveSceneImportDependencies Create(
        ResoniteLiveSceneImportTargetOptions options,
        ILiveSendClientSession clientSession,
        IResoniteLiveSendRunStarter runStarter)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(clientSession);
        ArgumentNullException.ThrowIfNull(runStarter);

        return new ResoniteLiveSceneImportDependencies(
            clientSession,
            startRequestFactory,
            runExecutorFactory.Create(runStarter),
            resourceReleaser);
    }
}
