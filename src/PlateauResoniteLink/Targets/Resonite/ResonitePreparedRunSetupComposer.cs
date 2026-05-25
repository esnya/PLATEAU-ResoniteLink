using System;
using System.Threading.Tasks;

using PlateauResoniteLink.Application.Importing;
using PlateauResoniteLink.Targets.Resonite.Execution;

namespace PlateauResoniteLink.Targets.Resonite;

internal interface IResonitePreparedRunSetupComposer
{
    LiveSendPreparedRunSetup Compose(
        LiveSendRunPlan runPlan,
        ResoniteSceneSetupState setupState);
}

internal sealed class ResonitePreparedRunSetupComposer(
    IResoniteSlotCreator slotCreator) : IResonitePreparedRunSetupComposer
{
    public LiveSendPreparedRunSetup Compose(
        LiveSendRunPlan runPlan,
        ResoniteSceneSetupState setupState)
    {
        ArgumentNullException.ThrowIfNull(runPlan);
        ValidateSetupState(setupState);

        LiveSendProgressSink progress = new();
        CommonMaterialAssetCache materials = CreateMaterialCache(setupState);
        ResoniteSharedSlotIndex placement = new(
            setupState.DatasetRootSlot,
            setupState.DatasetAssetsRootSlot,
            runPlan.RequestLocalOrigin,
            runPlan.SourceFileSlotNamesByRelativePath,
            setupState.SceneAnchor,
            slotCreator.CreateAsync);
        placement.IndexSetupHierarchy(setupState);

        return new LiveSendPreparedRunSetup(
            runPlan,
            setupState,
            progress,
            materials,
            placement);
    }

    private static CommonMaterialAssetCache CreateMaterialCache(
        ResoniteSceneSetupState setupState)
    {
        CommonMaterialAssetCache materials = new();
        foreach (CommonMaterialCatalogMember<ResoniteCommonMaterialAsset> materialAsset in setupState.CommonMaterialAssets.EnumerateMembers())
        {
            materials.CommonMaterialAssets.Set(materialAsset.Item);
        }

        foreach (string family in setupState.CommonMaterialFamilies)
        {
            materials.CommonMaterialFamilyWarmupTasks[family] = Task.CompletedTask;
        }

        return materials;
    }

    private static void ValidateSetupState(ResoniteSceneSetupState setupState)
    {
        if (string.IsNullOrWhiteSpace(setupState.DatasetRootSlot.Locator.Value)
            || string.IsNullOrWhiteSpace(setupState.DatasetAssetsRootSlot.Locator.Value)
            || string.IsNullOrWhiteSpace(setupState.CommonAssetsRootSlot.Locator.Value)
            || string.IsNullOrWhiteSpace(setupState.SceneAnchor.LocationSlot.Value)
            || setupState.CommonMaterialAssets is null
            || setupState.CommonMaterialFamilies is null)
        {
            throw new ArgumentException(
                "Setup state must be created by the scene setup interpreter before composing a prepared run setup.",
                nameof(setupState));
        }
    }
}
