using System.Linq;

using PlateauResoniteLink.Application.Importing;
using PlateauResoniteLink.Targets.Resonite;
using PlateauResoniteLink.Targets.Resonite.Execution;

using ResoniteLink;

namespace PlateauResoniteLink.Tests.Targets;

public sealed class ResoniteSlotSnapshotIndexTests
{
    [Fact]
    public void IndexSetupHierarchyIndexesObservedChildrenAndFiltersCreatedSourceRoots()
    {
        CreatedSlot datasetRoot = new(new ResoniteSlotLocator("dataset-root"), "PLATEAU tokyo23ku");
        CreatedSlot assetsRoot = new(new ResoniteSlotLocator("assets-root"), "Assets");
        ResoniteSlotSnapshotIndex index = new(datasetRoot);

        index.IndexSetupHierarchy(new ResoniteSceneSetupState(
            datasetRoot,
            assetsRoot,
            new CreatedSlot(new ResoniteSlotLocator("common-root"), "Common Materials"),
            DatasetRootExisted: true,
            new SceneAnchor(
                datasetRoot.Locator,
                "53394525",
                new ResoniteFloat3(0.0, 0.0, 0.0),
                ReferenceSourceFileRoot: null),
            CreateSlot(
                "dataset-root",
                "PLATEAU tokyo23ku",
                null,
                [
                    CreateSlot("existing-root", "plateau_tokyo23ku_bldg_53394525", "dataset-root"),
                    CreateSlot("assets-root", "Assets", "dataset-root"),
                ]),
            CommonMaterialCatalog.Create().Map(static member => new ResoniteCommonMaterialAsset(
                member,
                SceneImportContractMapper.ToInternal(member.CreateBinding([0])),
                default)),
            []));

        CreatedSlot? existingRoot = index.TryGetSharedChildSlot(
            datasetRoot.Locator,
            "plateau_tokyo23ku_bldg_53394525");
        Assert.Equal("existing-root", existingRoot?.Locator.Value);
        Assert.Equal("assets-root", index.TryGetSharedChildSlot(datasetRoot.Locator, "Assets")?.Locator.Value);

        CreatedSlot createdRoot = new(new ResoniteSlotLocator("created-root"), "plateau_tokyo23ku_bldg_53394526");
        index.MarkCreated(createdRoot);
        index.IndexCreatedSharedSlot(datasetRoot.Locator, createdRoot, new ResoniteFloat3(1.0, 2.0, 3.0));

        Slot observedRoot = Assert.Single(index.GetObservedDatasetSourceRoots());
        Assert.Equal("existing-root", observedRoot.ID);
    }

    private static Slot CreateSlot(
        string id,
        string name,
        string? parentId = null,
        Slot[]? children = null)
    {
        return new Slot
        {
            ID = id,
            Name = new Field_string { Value = name },
            Parent = parentId is null ? null : new Reference { TargetID = parentId },
            Children = children?.ToList(),
        };
    }
}
