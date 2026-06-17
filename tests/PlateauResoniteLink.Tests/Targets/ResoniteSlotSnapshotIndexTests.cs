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
                children:
                [
                    CreateSlot(
                        "existing-root",
                        "plateau_tokyo23ku_bldg_53394525",
                        "dataset-root",
                        new ResoniteFloat3(4.0, 5.0, 6.0)),
                    CreateSlot("assets-root", "Assets", "dataset-root"),
                    CreateSlot("operator-note", "Operator Notes", "dataset-root"),
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

        ObservedDatasetSourceRoot observedRoot = Assert.Single(index.GetObservedDatasetSourceRoots());
        Assert.Equal("existing-root", observedRoot.SlotId);
        Assert.Equal("plateau_tokyo23ku_bldg_53394525", observedRoot.SlotName);
        Assert.Equal("53394525", observedRoot.ConcreteMeshCode);
        Assert.Equal(new ResoniteFloat3(4.0, 5.0, 6.0), observedRoot.Position);
    }

    [Fact]
    public void IndexSetupHierarchyKeepsExpectedSourceRootWithoutMeshCodeAndIgnoresOtherChildren()
    {
        CreatedSlot datasetRoot = new(new ResoniteSlotLocator("dataset-root"), "PLATEAU tokyo23ku");
        CreatedSlot assetsRoot = new(new ResoniteSlotLocator("assets-root"), "Assets");
        ResoniteSlotSnapshotIndex index = new(datasetRoot, ["sample"]);

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
                children:
                [
                    CreateSlot("sample-root", "sample", "dataset-root", new ResoniteFloat3(7.0, 8.0, 9.0)),
                    CreateSlot("operator-note", "Operator Notes", "dataset-root"),
                ]),
            CommonMaterialCatalog.Create().Map(static member => new ResoniteCommonMaterialAsset(
                member,
                SceneImportContractMapper.ToInternal(member.CreateBinding([0])),
                default)),
            []));

        ObservedDatasetSourceRoot observedRoot = Assert.Single(index.GetObservedDatasetSourceRoots());
        Assert.Equal("sample-root", observedRoot.SlotId);
        Assert.Equal("sample", observedRoot.SlotName);
        Assert.Null(observedRoot.ConcreteMeshCode);
        Assert.Equal(new ResoniteFloat3(7.0, 8.0, 9.0), observedRoot.Position);
    }

    private static Slot CreateSlot(
        string id,
        string name,
        string? parentId = null,
        ResoniteFloat3? position = null,
        Slot[]? children = null)
    {
        return new Slot
        {
            ID = id,
            Name = new Field_string { Value = name },
            Parent = parentId is null ? null : new Reference { TargetID = parentId },
            Position = position is null ? null : new Field_float3
            {
                Value = new float3
                {
                    x = (float)position.X,
                    y = (float)position.Y,
                    z = (float)position.Z,
                },
            },
            Children = children?.ToList(),
        };
    }
}
