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
                CreateSlot("sibling-root", "plateau_tokyo23ku_bldg_53394526", new ResoniteFloat3(20.0, 1.0, 30.0)),
                CreateSlot("exact-root", "plateau_tokyo23ku_bldg_53394525", new ResoniteFloat3(2.0, 3.0, 4.0)),
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
                    CreateSlot("first-root", "plateau_tokyo23ku_bldg_53394525", new ResoniteFloat3(0.0, 0.0, 0.0)),
                    CreateSlot("second-root", "plateau_tokyo23ku_bldg_53394525", new ResoniteFloat3(1.0, 0.0, 0.0)),
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
                CreateSlot("sibling-root", "plateau_tokyo23ku_bldg_53394525", new ResoniteFloat3(20.0, 1.0, 30.0)),
            ]);
        ResoniteFloat3 expected = ResonitePlacementPolicy.Add(
            new ResoniteFloat3(20.0, 1.0, 30.0),
            ResonitePlacementPolicy.ComputeMeshCodeOffset("53394525", "53394527"));

        Assert.Equal(expected, placement?.Position);
        Assert.Equal("53394525", placement?.ReferenceMeshCode);
        Assert.Equal("sibling-root", placement?.SlotId);
    }

    [Fact]
    public void TryResolveRejectsObservedSourceRootWithoutPosition()
    {
        bool created = ObservedSourceRootSlot.TryCreate(
            CreateRawSlotWithoutPosition("source-root", "plateau_tokyo23ku_bldg_53394525"),
            out _);

        Assert.False(created);
    }

    private static ObservedSourceRootSlot CreateSlot(string id, string name, ResoniteFloat3 position)
    {
        Assert.True(ObservedSourceRootSlot.TryCreate(CreateRawSlot(id, name, position), out ObservedSourceRootSlot sourceRoot));
        return sourceRoot;
    }

    private static Slot CreateRawSlot(string id, string name, ResoniteFloat3 position)
    {
        return new Slot
        {
            ID = id,
            Name = new Field_string { Value = name },
            Position = new Field_float3
            {
                Value = new float3
                {
                    x = (float)position.X,
                    y = (float)position.Y,
                    z = (float)position.Z,
                },
            },
        };
    }

    private static Slot CreateRawSlotWithoutPosition(string id, string name)
    {
        return new Slot
        {
            ID = id,
            Name = new Field_string { Value = name },
        };
    }
}
