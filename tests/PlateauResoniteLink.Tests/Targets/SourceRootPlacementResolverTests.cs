using System;

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
                CreateSourceRoot(
                    "source-root",
                    "plateau_tokyo23ku_bldg_53394525",
                    new ResoniteFloat3(4.0, 5.0, 6.0)),
            ]);

        Assert.Equal(new ResoniteFloat3(4.0, 5.0, 6.0), placement.RootPosition);
        Assert.Equal(placement.RootPosition, placement.LocalPositionReferenceRoot);
    }

    [Fact]
    public void ResolveUsesExpectedSourceRootNameWithoutMeshCodeBeforeRequestOriginFallback()
    {
        SourceRootPlacement placement = SourceRootPlacementResolver.Resolve(
            "sample",
            "53394525",
            new ResoniteLocalOrigin(35.0, 139.0, 0.0),
            ObservedDatasetSourceRootSelector.SelectDirectChildren(
                [
                    CreateSlot("sample-root", "sample", new ResoniteFloat3(4.0, 5.0, 6.0)),
                    CreateSlotWithoutPosition("operator-note", "Operator Notes"),
                ],
                [],
                ["sample"]));

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
    public void ResolveRejectsObservedSourceRootWithoutPosition()
    {
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => SourceRootPlacementResolver.Resolve(
                "plateau_tokyo23ku_bldg_53394525",
                "53394525",
                new ResoniteLocalOrigin(35.0, 139.0, 0.0),
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

    private static Slot CreateSlotWithoutPosition(string id, string name)
    {
        return new Slot
        {
            ID = id,
            Name = new Field_string { Value = name },
        };
    }
}
