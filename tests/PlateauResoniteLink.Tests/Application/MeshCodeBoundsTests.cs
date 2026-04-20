using PlateauResoniteLink.Application.Importing;
using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Tests.Application;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores", Justification = "Test names describe contract cases.")]
public sealed class MeshCodeBoundsTests
{
    [Fact]
    public void TryParse_ReturnsNullForInvalidMeshCode()
    {
        Assert.Null(MeshCodeBounds.TryParse("invalid"));
    }

    [Fact]
    public void CreateManyFromRequestedMeshCodes_DeduplicatesAndSkipsInvalidEntries()
    {
        MeshCodeBounds[] bounds = MeshCodeBounds.CreateManyFromRequestedMeshCodes(["53394525", "invalid", "53394525"]);

        MeshCodeBounds actual = Assert.Single(bounds);
        MeshCodeBounds expected = MeshCodeBounds.TryParse("53394525")!;
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void TryMerge_ReturnsNullForEmptyInput()
    {
        Assert.Null(MeshCodeBounds.TryMerge([]));
    }

    [Fact]
    public void TryMerge_ExpandsToCoverAllInputAreas()
    {
        MeshCodeBounds first = MeshCodeBounds.TryParse("53394525")!;
        MeshCodeBounds second = MeshCodeBounds.TryParse("53394526")!;

        MeshCodeBounds merged = MeshCodeBounds.TryMerge([first, second])!;

        Assert.Equal(first.SouthLatitude, merged.SouthLatitude);
        Assert.Equal(second.NorthLatitude, merged.NorthLatitude);
        Assert.Equal(first.WestLongitude, merged.WestLongitude);
        Assert.Equal(second.EastLongitude, merged.EastLongitude);
    }

    [Fact]
    public void GetCenter_ReturnsOriginAtMidpointWithZeroAltitude()
    {
        MeshCodeBounds bounds = MeshCodeBounds.TryParse("53394525")!;

        ResoniteLocalOrigin center = bounds.GetCenter();

        Assert.Equal((bounds.SouthLatitude + bounds.NorthLatitude) / 2.0, center.Latitude, 12);
        Assert.Equal((bounds.WestLongitude + bounds.EastLongitude) / 2.0, center.Longitude, 12);
        Assert.Equal(0.0, center.Altitude, 12);
    }
}
