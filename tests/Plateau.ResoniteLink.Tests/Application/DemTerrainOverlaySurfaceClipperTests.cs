using Plateau.ResoniteLink.Application.Importing;
using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Tests.Application;

public sealed class DemTerrainOverlaySurfaceClipperTests
{
    [Fact]
    public void ClipGeneratedSurfaceToOverlaysPreservesSourceWinding()
    {
        LocalCityGmlResonitePlanBuilder.ParsedSurface surface = new(
            PolygonId: "dem-surface",
            Semantic: LocalCityGmlResonitePlanBuilder.ParsedSurfaceSemantic.Ground,
            ExteriorRing: new LocalCityGmlResonitePlanBuilder.ParsedRing(
                "ring-1",
                [
                    new LocalCityGmlResonitePlanBuilder.GeodeticPoint(35.0000, 139.0000, 10.0),
                    new LocalCityGmlResonitePlanBuilder.GeodeticPoint(35.0100, 139.0000, 20.0),
                    new LocalCityGmlResonitePlanBuilder.GeodeticPoint(35.0100, 139.0200, 30.0),
                ],
                UVs: null),
            InteriorRings: [],
            BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
            TexturePath: LocalCityGmlResonitePlanBuilder.DefaultDemTerrainTexturePath);
        TerrainTextureOverlay overlay = new(
            TexturePath: $"{LocalCityGmlResonitePlanBuilder.DefaultDemTerrainTexturePath}/53394525",
            PackageName: "dem",
            UrlTemplate: "https://tiles.example/{z}/{x}/{y}.png",
            ZoomLevel: 18,
            GeographicBounds: new GeographicRectangle(
                MinLatitude: 35.0000,
                MaxLatitude: 35.0100,
                MinLongitude: 139.0040,
                MaxLongitude: 139.0120),
            MaxTextureSize: LocalCityGmlResonitePlanBuilder.DefaultDemTerrainTextureMaxSize);

        (LocalCityGmlResonitePlanBuilder.ParsedSurface clippedSurface, _) = Assert.Single(
            DemTerrainOverlaySurfaceClipper.ClipGeneratedSurfaceToOverlays(surface, [overlay]));

        double sourceSignedArea = ComputeSignedArea(surface.ExteriorRing.Vertices);
        double clippedSignedArea = ComputeSignedArea(clippedSurface.ExteriorRing.Vertices);

        Assert.True(Math.Abs(sourceSignedArea) > 1e-12);
        Assert.True(Math.Abs(clippedSignedArea) > 1e-12);
        Assert.Equal(Math.Sign(sourceSignedArea), Math.Sign(clippedSignedArea));
    }

    private static double ComputeSignedArea(LocalCityGmlResonitePlanBuilder.GeodeticPoint[] vertices)
    {
        double signedArea = 0.0;
        for (int index = 0; index < vertices.Length; index++)
        {
            LocalCityGmlResonitePlanBuilder.GeodeticPoint current = vertices[index];
            LocalCityGmlResonitePlanBuilder.GeodeticPoint next = vertices[(index + 1) % vertices.Length];
            signedArea += (current.Longitude * next.Latitude) - (next.Longitude * current.Latitude);
        }

        return signedArea * 0.5;
    }
}
