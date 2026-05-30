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
