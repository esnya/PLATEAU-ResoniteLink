using System;

using PlateauResoniteLink.Targets.Resonite.Execution;

namespace PlateauResoniteLink.Targets.Resonite;

internal interface IResoniteSharedSlotIndexFactory
{
    ResoniteSharedSlotIndex Create(
        ResoniteSceneSetupState setupState,
        LiveSendRunPlan runPlan);
}

internal sealed class ResoniteSharedSlotIndexFactory(
    IResoniteSlotCreator slotCreator) : IResoniteSharedSlotIndexFactory
{
    private readonly IResoniteSlotCreator slotCreator =
        slotCreator ?? throw new ArgumentNullException(nameof(slotCreator));

    public ResoniteSharedSlotIndex Create(
        ResoniteSceneSetupState setupState,
        LiveSendRunPlan runPlan)
    {
        ArgumentNullException.ThrowIfNull(runPlan);

        ResoniteSharedSlotIndex placement = new(
            setupState.DatasetRootSlot,
            setupState.DatasetAssetsRootSlot,
            runPlan.RequestLocalOrigin,
            runPlan.SourceFileSlotNamesByRelativePath,
            setupState.SceneAnchor,
            slotCreator.CreateAsync);
        placement.IndexSetupHierarchy(setupState);
        return placement;
    }
}
