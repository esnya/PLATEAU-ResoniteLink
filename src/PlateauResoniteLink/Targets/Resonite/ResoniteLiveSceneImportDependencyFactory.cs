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
    IResoniteLiveSceneImportExecutorFactory executorFactory,
    IResoniteLiveSendResourceReleaser resourceReleaser)
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

        return new ResoniteLiveSceneImportDependencies(
            clientSessionFactory.Create(options, diagnostics),
            diagnostics,
            executorFactory.Create(options, terrainTextureAssetHttpClient),
            resourceReleaser);
    }
}
