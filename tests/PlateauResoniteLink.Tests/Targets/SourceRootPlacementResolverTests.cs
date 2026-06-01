using PlateauResoniteLink.Targets.Resonite;

using ResoniteLink;

namespace PlateauResoniteLink.Tests.Targets;

public sealed class SourceRootPlacementResolverTests
{
    [Fact]
    public void ResolveUsesObservedRootBeforeRequestOriginFallback()
    {
        SourceRootPlacement placement = SourceRootPlacementResolver.Resolve(
            "plateau_tokyo23ku_bldg_53394525",
            "53394525",
            new ResoniteLocalOrigin(35.0, 139.0, 0.0),
            [
                CreateSlot(
                    "source-root",
                    "plateau_tokyo23ku_bldg_53394525",
                    new ResoniteFloat3(4.0, 5.0, 6.0)),
            ]);

        Assert.Equal(new ResoniteFloat3(4.0, 5.0, 6.0), placement.RootPosition);
        Assert.Equal(placement.RootPosition, placement.LocalPositionReferenceRoot);
    }

    [Fact]
    public void ResolveUsesExactObservedRootWithoutConcreteMeshCode()
    {
        SourceRootPlacement placement = SourceRootPlacementResolver.Resolve(
            "custom_buildings",
            "53394525",
            new ResoniteLocalOrigin(35.0, 139.0, 0.0),
            [
                CreateSlot(
                    "source-root",
                    "custom_buildings",
                    new ResoniteFloat3(4.0, 5.0, 6.0)),
            ]);

        Assert.Equal(new ResoniteFloat3(4.0, 5.0, 6.0), placement.RootPosition);
        Assert.Equal(placement.RootPosition, placement.LocalPositionReferenceRoot);
    }

    [Fact]
    public void ResolveUsesRequestOriginWhenNoObservedSourceRootExists()
    {
        ResoniteLocalOrigin requestLocalOrigin = new(35.0, 139.0, 0.0);

        SourceRootPlacement placement = SourceRootPlacementResolver.Resolve(
            "plateau_tokyo23ku_bldg_53394525",
            "53394525",
            requestLocalOrigin,
            []);

        Assert.Equal(
            ResonitePlacementPolicy.ResolveMeshRootPosition(requestLocalOrigin, "53394525"),
            placement.RootPosition);
    }

    [Fact]
    public void ObservedSourceRootSlotRejectsSourceRootWithoutPositionBeforePlacementResolution()
    {
        bool created = ObservedSourceRootSlot.TryCreate(
            CreateRawSlotWithoutPosition("source-root", "plateau_tokyo23ku_bldg_53394525"),
            out _);

        Assert.False(created);
    }

    [Fact]
    public void ObservedSourceRootSlotAcceptsSourceRootWithoutConcreteMeshCode()
    {
        bool created = ObservedSourceRootSlot.TryCreate(
            CreateRawSlot("source-root", "custom_buildings", new ResoniteFloat3(1.0, 2.0, 3.0)),
            out ObservedSourceRootSlot sourceRoot);

        Assert.True(created);
        Assert.False(sourceRoot.TryGetConcreteMeshCode(out _));
        Assert.Equal("custom_buildings", sourceRoot.SlotName);
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
