using PlateauResoniteLink.Targets.Resonite;
using PlateauResoniteLink.Targets.Resonite.Execution;

using ResoniteLink;

namespace PlateauResoniteLink.Tests.Targets;

public sealed class SourceRootPlacementResolverTests
{
    [Fact]
    public void ResolveUsesObservedRootBeforeSceneAnchorFallback()
    {
        SourceRootPlacement placement = SourceRootPlacementResolver.Resolve(
            "plateau_tokyo23ku_bldg_53394525",
            "53394525",
            new ResoniteLocalOrigin(35.0, 139.0, 0.0),
            new SceneAnchor(
                new ResoniteSlotLocator("dataset-root"),
                "53394526",
                new ResoniteFloat3(0.0, 0.0, 0.0),
                ReferenceSourceFileRoot: null),
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
    public void ResolveUsesDatasetRootAnchorWhenNoObservedRootExists()
    {
        SourceRootPlacement placement = SourceRootPlacementResolver.Resolve(
            "plateau_tokyo23ku_bldg_53394525",
            "53394525",
            new ResoniteLocalOrigin(35.0, 139.0, 0.0),
            new SceneAnchor(
                new ResoniteSlotLocator("dataset-root"),
                "53394524",
                new ResoniteFloat3(0.0, 0.0, 0.0),
                ReferenceSourceFileRoot: null),
            []);

        Assert.Equal(
            ResonitePlacementPolicy.ComputeMeshCodeOffset("53394524", "53394525"),
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
