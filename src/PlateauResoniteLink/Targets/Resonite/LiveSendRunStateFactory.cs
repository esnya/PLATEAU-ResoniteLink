using System;
using System.Collections.Concurrent;
using System.Threading;

namespace PlateauResoniteLink.Targets.Resonite;

internal interface ILiveSendRunStateFactory
{
    LiveSendRunState Create(
        LiveSendRunPlan runPlan,
        ResoniteSceneSetupState setupState,
        LiveSendProgressSink progress,
        CommonMaterialAssetCache materials,
        ResoniteSharedSlotIndex placement,
        CancellationToken cancellationToken);
}

internal sealed class LiveSendRunStateFactory(
    IResoniteBufferedCityObjectBakerFactory cityObjectBakerFactory) : ILiveSendRunStateFactory
{
    public LiveSendRunState Create(
        LiveSendRunPlan runPlan,
        ResoniteSceneSetupState setupState,
        LiveSendProgressSink progress,
        CommonMaterialAssetCache materials,
        ResoniteSharedSlotIndex placement,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(runPlan);
        ArgumentNullException.ThrowIfNull(progress);
        ArgumentNullException.ThrowIfNull(materials);
        ArgumentNullException.ThrowIfNull(placement);

        LiveSendExecutionRuntime runtime = new(runPlan.Queue, cancellationToken);
        CompositeCityObjectBaker? cityObjectBaker = cityObjectBakerFactory.Create(
            runPlan.MeshBakeEnabled,
            runPlan.ResourceBudget);
        LiveSendRunContext context = new(
            runPlan,
            setupState.DatasetRootSlot,
            setupState.DatasetAssetsRootSlot,
            setupState.CommonAssetsRootSlot,
            cityObjectBaker);
        return new LiveSendRunState
        {
            Context = context,
            Progress = progress,
            Materials = materials,
            TerrainTextures = new TerrainTextureAssetCache(),
            Placement = placement,
            Runtime = runtime,
            GsiFallbackLicenseGate = new SemaphoreSlim(1, 1),
            DemSourceUseCounts = new ConcurrentDictionary<string, int>(StringComparer.Ordinal),
        };
    }
}
