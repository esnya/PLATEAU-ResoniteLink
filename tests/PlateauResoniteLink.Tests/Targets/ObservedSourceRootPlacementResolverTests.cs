
using System;

using PlateauResoniteLink.Targets.Resonite;

using ResoniteLink;

namespace PlateauResoniteLink.Tests.Targets;

public sealed class ObservedSourceRootPlacementResolverTests
{
    [Fact]
    public void TryResolveUsesExactSourceFileRootNameBeforeMeshCodeProjection()
    {
        ObservedSourceRootPlacement? placement = ObservedSourceRootPlacementResolver.TryResolve(
            "plateau_tokyo23ku_bldg_53394525",
            "53394525",
            [
                CreateSourceRoot("sibling-root", "plateau_tokyo23ku_bldg_53394526", new ResoniteFloat3(20.0, 1.0, 30.0)),
                CreateSourceRoot("exact-root", "plateau_tokyo23ku_bldg_53394525", new ResoniteFloat3(2.0, 3.0, 4.0)),
            ]);

        Assert.Equal(new ResoniteFloat3(2.0, 3.0, 4.0), placement?.Position);
        Assert.Equal("53394525", placement?.ReferenceMeshCode);
        Assert.Equal("exact-root", placement?.SlotId);
    }

    [Fact]
    public void TryResolveRejectsDuplicateExactSourceFileRootsWithDifferentPlacements()
    {
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => ObservedSourceRootPlacementResolver.TryResolve(
                "plateau_tokyo23ku_bldg_53394525",
                "53394525",
                [
                    CreateSourceRoot("first-root", "plateau_tokyo23ku_bldg_53394525", new ResoniteFloat3(0.0, 0.0, 0.0)),
                    CreateSourceRoot("second-root", "plateau_tokyo23ku_bldg_53394525", new ResoniteFloat3(1.0, 0.0, 0.0)),
                ]));

        Assert.Contains(
            "multiple observed source roots named 'plateau_tokyo23ku_bldg_53394525'",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void TryResolveProjectsFromAnyPositionedSiblingSourceRoot()
    {
        ObservedSourceRootPlacement? placement = ObservedSourceRootPlacementResolver.TryResolve(
            "plateau_tokyo23ku_bldg_53394527",
            "53394527",
            [
                CreateSourceRoot("sibling-root", "plateau_tokyo23ku_bldg_53394525", new ResoniteFloat3(20.0, 1.0, 30.0)),
            ]);
        ResoniteFloat3 expected = ResonitePlacementPolicy.ResolveMeshRootPosition(
            ResonitePlacementPolicy.ResolveParentOriginFromMeshRootPosition("53394525", new ResoniteFloat3(20.0, 1.0, 30.0)),
            "53394527",
            observedRootHeight: 1.0);

        Assert.Equal(expected, placement?.Position);
        Assert.Equal("53394525", placement?.ReferenceMeshCode);
        Assert.Equal("sibling-root", placement?.SlotId);
    }

    [Fact]
    public void TryResolveProjectsSiblingSourceRootsThroughRecoveredParentOrigin()
    {
        ResoniteLocalOrigin parentOrigin = new(35.6875, 139.69375, 0.0);
        ResoniteFloat3 firstRootPosition = ResonitePlacementPolicy.ResolveMeshRootPosition(parentOrigin, "53394525");
        ResoniteFloat3 secondRootPosition = ResonitePlacementPolicy.ResolveMeshRootPosition(parentOrigin, "53394526");

        ObservedSourceRootPlacement? placement = ObservedSourceRootPlacementResolver.TryResolve(
            "plateau_tokyo23ku_bldg_53394527",
            "53394527",
            [
                CreateSourceRoot("first-sibling-root", "plateau_tokyo23ku_bldg_53394525", firstRootPosition),
                CreateSourceRoot("second-sibling-root", "plateau_tokyo23ku_bldg_53394526", secondRootPosition),
            ]);
        ResoniteFloat3 expected = ResonitePlacementPolicy.ResolveMeshRootPosition(parentOrigin, "53394527");

        Assert.NotNull(placement);
        Assert.Equal(expected.X, placement.Value.Position.X, 3);
        Assert.Equal(expected.Z, placement.Value.Position.Z, 3);
    }

    [Fact]
    public void TryResolveRejectsObservedSourceRootWithoutPosition()
    {
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => ObservedSourceRootPlacementResolver.TryResolve(
                "plateau_tokyo23ku_bldg_53394525",
                "53394525",
                ObservedDatasetSourceRootSelector.SelectDirectChildren(
                    [CreateSlotWithoutPosition("source-root", "plateau_tokyo23ku_bldg_53394525")],
                    [])));

        Assert.Contains("did not expose a Position", exception.Message, StringComparison.Ordinal);
    }

    private static ObservedDatasetSourceRoot CreateSourceRoot(string id, string name, ResoniteFloat3 position)
    {
        string? concreteMeshCode = ResoniteSourceMeshCodeAnchor.TryGetConcreteMeshCode(name, out string meshCode)
            ? meshCode
            : null;
        return new ObservedDatasetSourceRoot(id, name, position, concreteMeshCode);
    }

    private static Slot CreateSlotWithoutPosition(string id, string name)
    {
        return new Slot
        {
            ID = id,
            Name = new Field_string { Value = name },
        };
    }
}
