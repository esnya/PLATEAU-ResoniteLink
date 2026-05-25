using System;
using System.Net.Http;

using PlateauResoniteLink.Transport.ResoniteLink;

namespace PlateauResoniteLink.Targets.Resonite;

internal interface IResoniteLiveSceneImportDependencyFactory
{
    ResoniteLiveSceneImportDependencies Create(
        ResoniteLiveSceneImportTargetOptions options,
        HttpClient terrainTextureAssetHttpClient);
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

        return new ResoniteLiveSceneImportDependencies(
            clientSession,
            diagnostics,
            startRequestFactory,
            runExecutorFactory.Create(runStarter),
            resourceReleaser);
    }
}
