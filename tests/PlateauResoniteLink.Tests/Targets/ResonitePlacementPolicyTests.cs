using System;
using System.Collections.Generic;

namespace PlateauResoniteLink.Tests.Targets;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores", Justification = "Test names describe contract cases.")]
public sealed class ResonitePlacementPolicyTests
{
    private static readonly string[] DuplicateStemPaths = ["udx/bldg/a/sample.gml", "udx/dem/b/sample.gml"];

    [Fact]
    public void ResolveRequiredSourceFileRootMeshCode_PrefersConcreteMeshCodeFromSourceFileSlot()
    {
        string resolved = PlateauResoniteLink.Targets.Resonite.ResonitePlacementPolicy.ResolveRequiredSourceFileRootMeshCode(
            "plateau_tokyo23ku_bldg_53394525",
            "533945");

        Assert.Equal("53394525", resolved);
    }

    [Fact]
    public void ResolveCityObjectLocalPosition_UsesRequestRelativeHorizontalOffsetAndObservedVerticalOffset()
    {
        PlateauResoniteLink.Domain.Importing.ResoniteLocalOrigin requestOrigin = RequireMeshCodeCenter("53394535");
        PlateauResoniteLink.Domain.Importing.ResoniteLocalOrigin rootOrigin = RequireMeshCodeCenter("53394525");
        PlateauResoniteLink.Domain.Importing.ResoniteFloat3 originalPosition = new(25.0, 15.0, -10.0);
        PlateauResoniteLink.Domain.Importing.ResoniteFloat3 observedRootPosition = new(999.0, 5.0, 888.0);
        PlateauResoniteLink.Domain.Importing.ResoniteFloat3 requestRelativeRootPosition = new(
            PlateauResoniteLink.Targets.Resonite.ResonitePlacementPolicy.ComputeOriginOffset(requestOrigin, rootOrigin).X,
            observedRootPosition.Y,
            PlateauResoniteLink.Targets.Resonite.ResonitePlacementPolicy.ComputeOriginOffset(requestOrigin, rootOrigin).Z);

        PlateauResoniteLink.Domain.Importing.ResoniteFloat3 expected = PlateauResoniteLink.Targets.Resonite.ResonitePlacementPolicy.Subtract(
            originalPosition,
            requestRelativeRootPosition);
        PlateauResoniteLink.Domain.Importing.ResoniteFloat3 actual = PlateauResoniteLink.Targets.Resonite.ResonitePlacementPolicy.ResolveCityObjectLocalPosition(
            requestOrigin,
            "53394525",
            observedRootPosition,
            originalPosition);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ResolveMeshRootPosition_UsesRequestRelativeHorizontalOffsetAndObservedVerticalOffset()
    {
        PlateauResoniteLink.Domain.Importing.ResoniteLocalOrigin requestOrigin = RequireMeshCodeCenter("53394535");
        PlateauResoniteLink.Domain.Importing.ResoniteLocalOrigin rootOrigin = RequireMeshCodeCenter("53394525");
        PlateauResoniteLink.Domain.Importing.ResoniteFloat3 resolved = PlateauResoniteLink.Targets.Resonite.ResonitePlacementPolicy.ResolveMeshRootPosition(
            requestOrigin,
            "53394525",
            observedRootHeight: 5.0);
        PlateauResoniteLink.Domain.Importing.ResoniteFloat3 expectedOffset =
            PlateauResoniteLink.Targets.Resonite.ResonitePlacementPolicy.ComputeOriginOffset(requestOrigin, rootOrigin);

        Assert.Equal(expectedOffset.X, resolved.X, 6);
        Assert.Equal(5.0, resolved.Y, 6);
        Assert.Equal(expectedOffset.Z, resolved.Z, 6);
    }

    [Fact]
    public void FormatLodSlotName_UsesLod0ForNullLod()
    {
        string slotName = PlateauResoniteLink.Targets.Resonite.ResonitePlacementPolicy.FormatLodSlotName(null);

        Assert.Equal("LOD0", slotName);
    }

    [Fact]
    public void CreateCityGmlSlotNamesByRelativePath_AddsStableHashForDuplicateFileStem()
    {
        IReadOnlyDictionary<string, string> slotNames =
            PlateauResoniteLink.Targets.Resonite.ResonitePlacementPolicy.CreateCityGmlSlotNamesByRelativePath(DuplicateStemPaths);

        Assert.Equal(2, slotNames.Count);
        Assert.All(slotNames.Values, static value => Assert.StartsWith("sample_", value, StringComparison.Ordinal));
        Assert.NotEqual(slotNames["udx/bldg/a/sample.gml"], slotNames["udx/dem/b/sample.gml"]);
    }

    private static PlateauResoniteLink.Domain.Importing.ResoniteLocalOrigin RequireMeshCodeCenter(string meshCode)
    {
        if (PlateauResoniteLink.Domain.Importing.PlateauMeshCode.TryGetGeodeticCenter(
            meshCode,
            out PlateauResoniteLink.Domain.Importing.GeodeticCoordinate center))
        {
            return new PlateauResoniteLink.Domain.Importing.ResoniteLocalOrigin(
                center.Latitude,
                center.Longitude,
                center.Altitude);
        }

        throw new InvalidOperationException($"Failed to resolve a mesh-code center for '{meshCode}'.");
    }
}
