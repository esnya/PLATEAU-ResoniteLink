using PlateauResoniteLink.Application.Importing;
using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Tests.Profiles;

public sealed class CityGmlMeshCodeBoundsFilterTests
{
    [Fact]
    public void ResolveActualMeshCodeUsesConcreteDemMeshCodeFromDisplayName()
    {
        string actualMeshCode = CityGmlMeshCodeBoundsFilter.ResolveActualMeshCode(
            "dem",
            "parent relief 53394525",
            "dem-parent",
            fallbackActualMeshCode: "533945",
            sharedAcrossMeshCodes: true);

        Assert.Equal("53394525", actualMeshCode);
    }

    [Fact]
    public void ResolveActualMeshCodeKeepsFallbackForNonDemSharedFile()
    {
        string actualMeshCode = CityGmlMeshCodeBoundsFilter.ResolveActualMeshCode(
            "bldg",
            "building 53394525",
            "bldg-1",
            fallbackActualMeshCode: "533945",
            sharedAcrossMeshCodes: true);

        Assert.Equal("533945", actualMeshCode);
    }

    [Fact]
    public void IntersectsRequestedMeshCodeBoundsUsesActualMeshCodeForSharedObjects()
    {
        ParsedSurface surfaceOutsideRequest = CreateSurfaceAtMeshCenter("53394600");

        bool intersects = CityGmlMeshCodeBoundsFilter.IntersectsRequestedMeshCodeBounds(
            actualMeshCode: "53394525",
            sharedAcrossMeshCodes: true,
            coordinateReferenceSystem: CoordinateReferenceSystem.Parse("EPSG:4326"),
            requestedMeshCodeBounds: [MeshCodeBounds.TryParse("53394525")!],
            surfaces: [surfaceOutsideRequest]);

        Assert.True(intersects);
    }

    [Fact]
    public void IntersectsRequestedMeshCodeBoundsUsesSurfaceBoundsForNonSharedObjects()
    {
        ParsedSurface surfaceOutsideRequest = CreateSurfaceAtMeshCenter("53394600");

        bool intersects = CityGmlMeshCodeBoundsFilter.IntersectsRequestedMeshCodeBounds(
            actualMeshCode: "53394525",
            sharedAcrossMeshCodes: false,
            coordinateReferenceSystem: CoordinateReferenceSystem.Parse("EPSG:4326"),
            requestedMeshCodeBounds: [MeshCodeBounds.TryParse("53394525")!],
            surfaces: [surfaceOutsideRequest]);

        Assert.False(intersects);
    }

    [Fact]
    public void IntersectsRequestedMeshCodeBoundsSkipsFilteringForLocalCartesianReferenceSystem()
    {
        ParsedSurface surfaceOutsideRequest = CreateSurfaceAtMeshCenter("53394600");

        bool intersects = CityGmlMeshCodeBoundsFilter.IntersectsRequestedMeshCodeBounds(
            actualMeshCode: "53394525",
            sharedAcrossMeshCodes: false,
            coordinateReferenceSystem: CoordinateReferenceSystem.Parse((string?)null),
            requestedMeshCodeBounds: [MeshCodeBounds.TryParse("53394525")!],
            surfaces: [surfaceOutsideRequest]);

        Assert.True(intersects);
    }

    private static ParsedSurface CreateSurfaceAtMeshCenter(string meshCode)
    {
        Assert.True(PlateauMeshCode.TryGetBounds(
            meshCode,
            out (double SouthLatitude, double NorthLatitude, double WestLongitude, double EastLongitude) bounds));

        double latitude = (bounds.SouthLatitude + bounds.NorthLatitude) / 2.0;
        double longitude = (bounds.WestLongitude + bounds.EastLongitude) / 2.0;
        GeodeticPoint point = new(latitude, longitude, 0.0);

        return new ParsedSurface(ParsedSurfaceSemantic.Ground,
            new ParsedRing([point, point, point], UVs: null),
            InteriorRings: [],
            new ColorRgba(1.0, 1.0, 1.0, 1.0),
            TexturePayload: null);
    }
}
