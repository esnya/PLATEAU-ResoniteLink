using System;

using PlateauResoniteLink.Targets.Resonite;
using PlateauResoniteLink.Targets.Resonite.Diagnostics;
using PlateauResoniteLink.Targets.Resonite.Execution;
using PlateauResoniteLink.Transport.ResoniteLink;

namespace PlateauResoniteLink.Cli;

internal interface ICanonicalDumpLiveSceneImportTargetFactory
{
    ResoniteLiveSceneImportTarget Create(
        SceneSinkRecordingClient recordingClient,
        ImportCommandOptions options,
        Action<string>? progressReporter);
}

internal sealed class DefaultCanonicalDumpLiveSceneImportTargetFactory(
    IResoniteDatasetLicenseWriter datasetLicenseWriter,
    IResonitePreparedCityObjectImporter preparedCityObjectImporter,
    IResoniteLiveSendStartRequestFactory startRequestFactory,
    IResoniteLiveSendRunPlanInitializer runPlanInitializer,
    IResoniteLiveSendConnectionInitializer connectionInitializer,
    IResoniteLiveSendSetupInitializer setupInitializer,
    IResoniteLiveSendRunActivatorFactory runActivatorFactory,
    IResoniteLiveSendContextFactory contextFactory) : ICanonicalDumpLiveSceneImportTargetFactory
{
    private readonly IResoniteLiveSendContextFactory contextFactory =
        contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));

    public ResoniteLiveSceneImportTarget Create(
        SceneSinkRecordingClient recordingClient,
        ImportCommandOptions options,
        Action<string>? progressReporter)
    {
        ArgumentNullException.ThrowIfNull(recordingClient);
        ArgumentNullException.ThrowIfNull(options);

        ResoniteLiveSceneImportTargetOptions targetOptions = new(
            new Uri("ws://localhost:1/"),
            ConnectionCount: 1,
            EnableSendMetrics: false,
            CliResoniteTargetOptions.MapMemoryProfile(options.MemoryProfile, nameof(options.MemoryProfile)),
            options.EnableMeshBake,
            TerrainTileCacheRoot: null,
            DisableTerrainTileCache: true,
            progressReporter);

        ResoniteLiveSendQueue queue = CreateQueue();
        ResoniteLiveSendWorkerLauncher workerLauncher = CreateWorkerLauncher();
        ResoniteLiveSendResourceReleaser resourceReleaser = new();
        return new ResoniteLiveSceneImportTarget(
            targetOptions,
            new ResoniteLiveSceneImportDependencies(
                new SingleRecordingClientSession(recordingClient),
                ResoniteLinkSendDiagnostics.Disabled,
                new ResoniteLiveSceneImportExecutor(
                    startRequestFactory,
                    new ResoniteLiveSendRunStarter(
                        runPlanInitializer,
                        connectionInitializer,
                        setupInitializer,
                        runActivatorFactory.Create(workerLauncher)),
                    contextFactory,
                    resourceReleaser,
                    queue),
                resourceReleaser));
    }

    private static ResoniteLiveSendQueue CreateQueue()
    {
        IResoniteQueuedCityObjectEnqueuer queuedCityObjectEnqueuer = new ResoniteQueuedCityObjectEnqueuer();
        return new ResoniteLiveSendQueue(
            queuedCityObjectEnqueuer,
            new ResoniteLiveSendFinalizer(queuedCityObjectEnqueuer));
    }

    private ResoniteLiveSendWorkerLauncher CreateWorkerLauncher()
    {
        ResoniteQueuedCityObjectWorker queuedCityObjectWorker = new(
            new ResoniteQueuedCityObjectLaneProcessor(
                new ResoniteQueuedCityObjectSender(
                    new ResoniteQueuedCityObjectPreparer(
                        new ResoniteQueuedGeometryPreparer(),
                        new ResoniteQueuedTexturePreparer(
                            new DeterministicTerrainTextureAssetGenerator(),
                            datasetLicenseWriter)),
                    new ResoniteQueuedSendFailurePolicy(),
                    preparedCityObjectImporter)));
        return new ResoniteLiveSendWorkerLauncher(queuedCityObjectWorker);
    }
}
