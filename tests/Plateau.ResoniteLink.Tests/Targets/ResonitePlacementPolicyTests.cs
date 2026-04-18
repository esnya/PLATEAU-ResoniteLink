namespace Plateau.ResoniteLink.Tests.Targets;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores", Justification = "Test names describe contract cases.")]
public sealed class ResonitePlacementPolicyTests
{
    private static readonly string[] DuplicateStemPaths = ["udx/bldg/a/sample.gml", "udx/dem/b/sample.gml"];

    [Fact]
    public void ResolveRequiredSourceFileRootMeshCode_PrefersConcreteMeshCodeFromSourceFileSlot()
    {
        string resolved = Plateau.ResoniteLink.Targets.Resonite.ResonitePlacementPolicy.ResolveRequiredSourceFileRootMeshCode(
            "plateau_tokyo23ku_bldg_53394525",
            "533945");

        Assert.Equal("53394525", resolved);
    }

    [Fact]
    public void ResolveCityObjectLocalPosition_UsesRequestRelativeHorizontalOffsetAndObservedVerticalOffset()
    {
        Plateau.ResoniteLink.Domain.Importing.ResoniteLocalOrigin requestOrigin = RequireMeshCodeCenter("53394535");
        Plateau.ResoniteLink.Domain.Importing.ResoniteLocalOrigin rootOrigin = RequireMeshCodeCenter("53394525");
        Plateau.ResoniteLink.Domain.Importing.ResoniteFloat3 originalPosition = new(25.0, 15.0, -10.0);
        Plateau.ResoniteLink.Domain.Importing.ResoniteFloat3 observedRootPosition = new(999.0, 5.0, 888.0);
        Plateau.ResoniteLink.Domain.Importing.ResoniteFloat3 requestRelativeRootPosition = new(
            Plateau.ResoniteLink.Targets.Resonite.ResonitePlacementPolicy.ComputeOriginOffset(requestOrigin, rootOrigin).X,
            observedRootPosition.Y,
            Plateau.ResoniteLink.Targets.Resonite.ResonitePlacementPolicy.ComputeOriginOffset(requestOrigin, rootOrigin).Z);

        Plateau.ResoniteLink.Domain.Importing.ResoniteFloat3 expected = Plateau.ResoniteLink.Targets.Resonite.ResonitePlacementPolicy.Subtract(
            originalPosition,
            requestRelativeRootPosition);
        Plateau.ResoniteLink.Domain.Importing.ResoniteFloat3 actual = Plateau.ResoniteLink.Targets.Resonite.ResonitePlacementPolicy.ResolveCityObjectLocalPosition(
            requestOrigin,
            "53394525",
            observedRootPosition,
            originalPosition);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ResolveMeshRootPosition_UsesRequestRelativeHorizontalOffsetAndObservedVerticalOffset()
    {
        Plateau.ResoniteLink.Domain.Importing.ResoniteLocalOrigin requestOrigin = RequireMeshCodeCenter("53394535");
        Plateau.ResoniteLink.Domain.Importing.ResoniteLocalOrigin rootOrigin = RequireMeshCodeCenter("53394525");
        Plateau.ResoniteLink.Domain.Importing.ResoniteFloat3 resolved = Plateau.ResoniteLink.Targets.Resonite.ResonitePlacementPolicy.ResolveMeshRootPosition(
            requestOrigin,
            "53394525",
            observedRootHeight: 5.0);
        Plateau.ResoniteLink.Domain.Importing.ResoniteFloat3 expectedOffset =
            Plateau.ResoniteLink.Targets.Resonite.ResonitePlacementPolicy.ComputeOriginOffset(requestOrigin, rootOrigin);

        Assert.Equal(expectedOffset.X, resolved.X, 6);
        Assert.Equal(5.0, resolved.Y, 6);
        Assert.Equal(expectedOffset.Z, resolved.Z, 6);
    }

    [Fact]
    public void FormatLodSlotName_UsesLod0ForNullLod()
    {
        string slotName = Plateau.ResoniteLink.Targets.Resonite.ResonitePlacementPolicy.FormatLodSlotName(null);

        Assert.Equal("LOD0", slotName);
    }

    [Fact]
    public void CreateCityGmlSlotNamesByRelativePath_AddsStableHashForDuplicateFileStem()
    {
        IReadOnlyDictionary<string, string> slotNames =
            Plateau.ResoniteLink.Targets.Resonite.ResonitePlacementPolicy.CreateCityGmlSlotNamesByRelativePath(DuplicateStemPaths);

        Assert.Equal(2, slotNames.Count);
        Assert.All(slotNames.Values, static value => Assert.StartsWith("sample_", value, StringComparison.Ordinal));
        Assert.NotEqual(slotNames["udx/bldg/a/sample.gml"], slotNames["udx/dem/b/sample.gml"]);
    }

    private static Plateau.ResoniteLink.Domain.Importing.ResoniteLocalOrigin RequireMeshCodeCenter(string meshCode)
    {
        if (Plateau.ResoniteLink.Domain.Importing.PlateauMeshCode.TryGetCenter(
            meshCode,
            out Plateau.ResoniteLink.Domain.Importing.ResoniteLocalOrigin center))
        {
            return center;
        }

        throw new InvalidOperationException($"Failed to resolve a mesh-code center for '{meshCode}'.");
    }
}
