using PlateauResoniteLink.Application.Importing;
using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Tests.Profiles;

public sealed class DemTerrainGridBoundsFactoryTests
{
    [Fact]
    public void CreateClipsRawBoundsToOverlayIntersection()
    {
        TerrainTextureOverlay overlay = CreateOverlay(new GeographicRectangle(1.0, 3.0, 2.0, 4.0));

        DemTerrainGridBounds bounds = DemTerrainGridBoundsFactory.Create(
            positions:
            [
                new Float3(0.0, 0.0, 0.0),
                new Float3(10.0, 0.0, 0.0),
                new Float3(10.0, 0.0, 10.0),
                new Float3(0.0, 0.0, 10.0),
            ],
            cityObjectGeographicBounds: new GeographicRectangle(0.0, 4.0, 0.0, 5.0),
            referenceLatitude: 2.0,
            referenceLongitude: 3.0,
            referenceAltitude: 0.0,
            demTerrainTextureOverlay: overlay,
            projectLocalPosition: static (latitude, longitude, _) => new Float3(longitude, 0.0, latitude));

        Assert.Equal(2.0, bounds.MinX);
        Assert.Equal(4.0, bounds.MaxX);
        Assert.Equal(1.0, bounds.MinZ);
        Assert.Equal(3.0, bounds.MaxZ);
    }

    [Fact]
    public void CreateFallsBackToRawBoundsWhenOverlayIntersectionIsDegenerate()
    {
        TerrainTextureOverlay overlay = CreateOverlay(new GeographicRectangle(2.0, 2.0, 3.0, 4.0));

        DemTerrainGridBounds bounds = DemTerrainGridBoundsFactory.Create(
            positions:
            [
                new Float3(0.0, 0.0, 0.0),
                new Float3(10.0, 0.0, 0.0),
                new Float3(10.0, 0.0, 10.0),
                new Float3(0.0, 0.0, 10.0),
            ],
            cityObjectGeographicBounds: new GeographicRectangle(0.0, 4.0, 0.0, 5.0),
            referenceLatitude: 2.0,
            referenceLongitude: 3.0,
            referenceAltitude: 0.0,
            demTerrainTextureOverlay: overlay,
            projectLocalPosition: static (latitude, longitude, _) => new Float3(longitude, 0.0, latitude));

        Assert.Equal(0.0, bounds.MinX);
        Assert.Equal(10.0, bounds.MaxX);
        Assert.Equal(0.0, bounds.MinZ);
        Assert.Equal(10.0, bounds.MaxZ);
    }

    private static TerrainTextureOverlay CreateOverlay(GeographicRectangle bounds)
    {
        return new TerrainTextureOverlay(
            "dem",
            "https://example.test/{z}/{x}/{y}.jpg",
            ZoomLevel: 18,
            GeographicBounds: bounds,
            MaxTextureSize: 1024);
    }
}
