using System;
using System.Net.Http;

using PlateauResoniteLink.Transport.ResoniteLink;

namespace PlateauResoniteLink.Targets.Resonite;

internal interface IResoniteLiveSceneImportDependencyFactory
{
    ResoniteLiveSceneImportSession CreateSession(ResoniteLiveSceneImportTargetOptions options);

    ResoniteLiveSceneImportExecutionServices CreateExecutionServices(
        ResoniteLiveSceneImportTargetOptions options,
        HttpClient terrainTextureAssetHttpClient);
}

internal sealed class ResoniteLiveSceneImportDependencyFactory(
    IResoniteClientSessionFactory clientSessionFactory,
    ITerrainTextureAssetGeneratorFactory terrainTextureAssetGeneratorFactory,
    IResoniteQueuedCityObjectSenderFactory queuedCityObjectSenderFactory,
    IResoniteLiveSendRunFinalizer runFinalizer,
    IResoniteLiveSendExecutionResultFactory executionResultFactory,
    IResoniteLiveSendRunResourceReleaser runResourceReleaser,
    IResoniteLiveSendExecutionGateFactory executionGateFactory,
    IResoniteLiveSendRunStarter runStarter,
    IResoniteImportedObjectUnitStreamQueueWriter objectUnitStreamQueueWriter)
    : IResoniteLiveSceneImportDependencyFactory
{
    public ResoniteLiveSceneImportSession CreateSession(ResoniteLiveSceneImportTargetOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ResoniteLinkSendDiagnostics diagnostics = CreateDiagnostics(options);

        return new ResoniteLiveSceneImportSession(
            clientSessionFactory.Create(options, diagnostics),
            diagnostics);
    }

    public ResoniteLiveSceneImportExecutionServices CreateExecutionServices(
        ResoniteLiveSceneImportTargetOptions options,
        HttpClient terrainTextureAssetHttpClient)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(terrainTextureAssetHttpClient);
        ITerrainTextureAssetGenerator terrainTextureAssetGenerator = terrainTextureAssetGeneratorFactory.Create(
            terrainTextureAssetHttpClient,
            options);

        return new ResoniteLiveSceneImportExecutionServices(
            executionGateFactory.Create(),
            runStarter,
            objectUnitStreamQueueWriter,
            runFinalizer,
            executionResultFactory,
            runResourceReleaser,
            queuedCityObjectSenderFactory.Create(terrainTextureAssetGenerator));
    }

    private static ResoniteLinkSendDiagnostics CreateDiagnostics(ResoniteLiveSceneImportTargetOptions options)
    {
        return options.EnableSendMetrics
            ? ResoniteLinkSendDiagnostics.CreateEnabled(options.ProgressReporter)
            : ResoniteLinkSendDiagnostics.Disabled;
    }
}
