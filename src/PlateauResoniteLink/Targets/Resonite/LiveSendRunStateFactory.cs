using System;
using System.Threading;

namespace PlateauResoniteLink.Targets.Resonite;

internal sealed class LiveSendRunStateFactory(
    IResoniteBufferedCityObjectBakerFactory cityObjectBakerFactory)
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

        CompositeCityObjectBaker? cityObjectBaker = cityObjectBakerFactory.Create(
            runPlan.MeshBakeEnabled,
            runPlan.ResourceBudget,
            runPlan.RequestLocalOrigin);
        LiveSendRunRuntimeComponents runtimeComponents = LiveSendRunRuntimeComponentsFactory.Create(
            runPlan.Queue,
            cancellationToken);
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
            TerrainTextures = runtimeComponents.TerrainTextures,
            Placement = placement,
            Runtime = runtimeComponents.Runtime,
            GsiFallbackLicenseGate = runtimeComponents.GsiFallbackLicenseGate,
            DemSourceUseCounts = runtimeComponents.DemSourceUseCounts,
        };
    }
}
