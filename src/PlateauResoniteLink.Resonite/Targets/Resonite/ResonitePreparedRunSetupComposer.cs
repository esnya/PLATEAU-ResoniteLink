using System;
using System.Threading.Tasks;

using PlateauResoniteLink.Resonite.Targets.Resonite.Execution;
using PlateauResoniteLink.Core.Application.Importing.Contracts;

namespace PlateauResoniteLink.Resonite.Targets.Resonite;

internal sealed class ResonitePreparedRunSetupComposer(
    IResoniteSlotCreator slotCreator)
{
    public LiveSendPreparedRunSetup Compose(
        LiveSendRunPlan runPlan,
        ResoniteSceneSetupState setupState)
    {
        ArgumentNullException.ThrowIfNull(runPlan);

        LiveSendProgressSink progress = new();
        CommonMaterialAssetCache materials = CreateMaterialCache(setupState);
        ResoniteSharedSlotIndex placement = new(
            setupState.DatasetRootSlot,
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
}
