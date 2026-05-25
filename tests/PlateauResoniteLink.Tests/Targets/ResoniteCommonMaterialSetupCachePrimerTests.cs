using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using PlateauResoniteLink.Application.Importing;
using PlateauResoniteLink.Targets.Resonite;
using PlateauResoniteLink.Targets.Resonite.Execution;

namespace PlateauResoniteLink.Tests.Targets;

public sealed class ResoniteCommonMaterialSetupCachePrimerTests
{
    [Fact]
    public void PrimeSeedsSetupBatchAssetsFamiliesAndProgress()
    {
        CommonMaterialCatalog<DefaultCommonMaterialMember> catalog = CommonMaterialCatalog.Create();
        CreatedMaterialAsset expectedAsset = new(
            new ResoniteComponentLocator("generic-uv-material"),
            MaterialPropertyBlockComponent: null);
        ResoniteSceneSetupState setupState = CreateSetupState(
            catalog.FilterToDefinitions([catalog.Generic.Uv.Definition])
                .Map(member => new ResoniteCommonMaterialAsset(
                    member,
                    SceneImportContractMapper.ToInternal(member.CreateBinding([0])),
                    expectedAsset)),
            ["generic"]);
        CommonMaterialAssetCache materials = new();
        LiveSendProgressSink progress = new();
        List<string> progressMessages = [];

        new ResoniteCommonMaterialSetupCachePrimer().Prime(
            setupState,
            materials,
            progress,
            progressMessages.Add);

        Assert.True(materials.CommonMaterialAssets.TryGetAsset(catalog.Generic.Uv, out CreatedMaterialAsset actualAsset));
        Assert.Equal(expectedAsset, actualAsset);
        Assert.True(materials.CommonMaterialFamilyWarmupTasks.TryGetValue("generic", out Task? warmupTask));
        Assert.Same(Task.CompletedTask, warmupTask);
        Assert.Equal(1, progress.FirstCommonMaterialPrepLogged);
        Assert.Contains(
            progressMessages,
            static message => message.Contains("Setup batch prepared 1 textureless common materials.", StringComparison.Ordinal));
    }

    [Fact]
    public void PrimeReportsEmptySetupBatchWithoutMarkingFirstCommonMaterialPreparation()
    {
        CommonMaterialCatalog<DefaultCommonMaterialMember> catalog = CommonMaterialCatalog.Create();
        ResoniteSceneSetupState setupState = CreateSetupState(
            catalog.FilterToDefinitions([])
                .Map(member => new ResoniteCommonMaterialAsset(
                    member,
                    SceneImportContractMapper.ToInternal(member.CreateBinding([0])),
                    default)),
            []);
        CommonMaterialAssetCache materials = new();
        LiveSendProgressSink progress = new();
        List<string> progressMessages = [];

        new ResoniteCommonMaterialSetupCachePrimer().Prime(
            setupState,
            materials,
            progress,
            progressMessages.Add);

        Assert.Equal(0, materials.CommonMaterialAssets.Count);
        Assert.Empty(materials.CommonMaterialFamilyWarmupTasks);
        Assert.Equal(0, progress.FirstCommonMaterialPrepLogged);
        Assert.Contains(
            progressMessages,
            static message => message.Contains(
                "Setup created common material slots; no textureless common material components were needed in setup batch.",
                StringComparison.Ordinal));
    }

    private static ResoniteSceneSetupState CreateSetupState(
        CommonMaterialCatalog<ResoniteCommonMaterialAsset> commonMaterialAssets,
        IReadOnlyCollection<string> commonMaterialFamilies)
    {
        CreatedSlot datasetRoot = new(new ResoniteSlotLocator("dataset-root"), "PLATEAU tokyo23ku");
        return new ResoniteSceneSetupState(
            datasetRoot,
            new CreatedSlot(new ResoniteSlotLocator("assets-root"), "Assets"),
            new CreatedSlot(new ResoniteSlotLocator("common-root"), "Common Materials"),
            DatasetRootExisted: false,
            new SceneAnchor(
                datasetRoot.Locator,
                "53394525",
                new ResoniteFloat3(0.0, 0.0, 0.0),
                ReferenceSourceFileRoot: null),
            DatasetRootSnapshot: null,
            commonMaterialAssets,
            commonMaterialFamilies);
    }
}
