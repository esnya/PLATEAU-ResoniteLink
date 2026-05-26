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

    private static Slot CreateSlot(string id, string name, ResoniteFloat3 position)
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
}
