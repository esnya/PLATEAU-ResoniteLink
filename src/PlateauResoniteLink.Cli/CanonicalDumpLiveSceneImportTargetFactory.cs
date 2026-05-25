using System;

using PlateauResoniteLink.Domain.Importing;
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
    IResoniteLiveSendConnectionInitializer connectionInitializer,
    IResoniteLiveSendSetupInitializer setupInitializer,
    ILiveSendRunPlanFactory runPlanFactory,
    IResoniteLiveSendRunActivatorFactory runActivatorFactory) : ICanonicalDumpLiveSceneImportTargetFactory
{
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
            options.MemoryProfile switch
            {
                PlateauImportMemoryProfile.Small => ResoniteImportMemoryProfile.Small,
                PlateauImportMemoryProfile.Large => ResoniteImportMemoryProfile.Large,
                _ => throw new ArgumentOutOfRangeException(nameof(options), options.MemoryProfile, "Unsupported memory profile."),
            },
            options.EnableMeshBake,
            TerrainTileCacheRoot: null,
            DisableTerrainTileCache: true,
            progressReporter);

        ResoniteLiveSendQueue queue = CreateQueue();
        ResoniteLiveSendWorkerLauncher workerLauncher = CreateWorkerLauncher();
        return new ResoniteLiveSceneImportTarget(
            targetOptions,
            new ResoniteLiveSceneImportDependencies(
                new SingleRecordingClientSession(recordingClient),
                ResoniteLinkSendDiagnostics.Disabled,
                startRequestFactory,
                new ResoniteLiveSendRunStarter(
                    connectionInitializer,
                    setupInitializer,
                    runPlanFactory,
                    runActivatorFactory.Create(workerLauncher)),
                queue));
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
