using System;
using System.Collections.Generic;
using System.Linq;

using PlateauResoniteLink.Application.Importing;
using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Tests.Profiles;

public sealed class DemTerrainOverlaySurfaceClipperTests
{
    [Fact]
    public void ClipGeneratedSurfaceToOverlaysPreservesSourceWinding()
    {
        BootstrapParsedSurface surface = new(
            PolygonId: "dem-surface",
            Semantic: BootstrapParsedSurfaceSemantic.Ground,
            ExteriorRing: new BootstrapParsedRing(
                "ring-1",
                [
                    new GeodeticPoint(35.0000, 139.0000, 10.0),
                    new GeodeticPoint(35.0100, 139.0000, 20.0),
                    new GeodeticPoint(35.0100, 139.0200, 30.0),
                ],
                UVs: null),
            InteriorRings: [],
            BaseColor: new ColorRgba(1.0, 1.0, 1.0, 1.0),
            TexturePayload: null,
            UsesGeneratedDemTexture: true);
        TerrainTextureOverlay overlay = new(
            PackageName: "dem",
            UrlTemplate: "https://tiles.example/{z}/{x}/{y}.png",
            ZoomLevel: 18,
            GeographicBounds: new GeographicRectangle(
                MinLatitude: 35.0000,
                MaxLatitude: 35.0100,
                MinLongitude: 139.0040,
                MaxLongitude: 139.0120),
            MaxTextureSize: DemTerrainTextureDefaults.MaxTextureSize);

        (BootstrapParsedSurface clippedSurface, _) = Assert.Single(
            DemTerrainOverlaySurfaceClipper.ClipGeneratedSurfaceToOverlays(surface, [overlay]));

        double sourceSignedArea = ComputeSignedArea(surface.ExteriorRing.Vertices);
        double clippedSignedArea = ComputeSignedArea(clippedSurface.ExteriorRing.Vertices);

        Assert.True(Math.Abs(sourceSignedArea) > 1e-12);
        Assert.True(Math.Abs(clippedSignedArea) > 1e-12);
        Assert.Equal(Math.Sign(sourceSignedArea), Math.Sign(clippedSignedArea));
    }

    [Fact]
    public void ClipGeneratedSurfaceToOverlaysPreservesAreaAcrossBoundarySplit()
    {
        BootstrapParsedSurface surface = new(
            PolygonId: "dem-surface-area",
            Semantic: BootstrapParsedSurfaceSemantic.Ground,
            ExteriorRing: new BootstrapParsedRing(
                "ring-area",
                [
                    new GeodeticPoint(35.0000, 139.0000, 10.0),
                    new GeodeticPoint(35.0100, 139.0000, 20.0),
                    new GeodeticPoint(35.0100, 139.0200, 30.0),
                ],
                UVs: null),
            InteriorRings: [],
            BaseColor: new ColorRgba(1.0, 1.0, 1.0, 1.0),
            TexturePayload: null,
            UsesGeneratedDemTexture: true);
        TerrainTextureOverlay[] overlays =
        [
            new(
                PackageName: "dem",
                UrlTemplate: "https://tiles.example/{z}/{x}/{y}.png",
                ZoomLevel: 18,
                GeographicBounds: new GeographicRectangle(35.0000, 35.0100, 139.0000, 139.0100),
                MaxTextureSize: DemTerrainTextureDefaults.MaxTextureSize),
            new(
                PackageName: "dem",
                UrlTemplate: "https://tiles.example/{z}/{x}/{y}.png",
                ZoomLevel: 18,
                GeographicBounds: new GeographicRectangle(35.0000, 35.0100, 139.0100, 139.0200),
                MaxTextureSize: DemTerrainTextureDefaults.MaxTextureSize),
        ];

        IReadOnlyList<(BootstrapParsedSurface Surface, TerrainTextureOverlay Overlay)> clipped =
            DemTerrainOverlaySurfaceClipper.ClipGeneratedSurfaceToOverlays(surface, overlays);

        Assert.Equal(2, clipped.Count);
        double sourceArea = ComputeApproximateArea(surface.ExteriorRing.Vertices);
        double clippedArea = clipped.Sum(static entry => ComputeApproximateArea(entry.Surface.ExteriorRing.Vertices));
        Assert.InRange(clippedArea / sourceArea, 0.999995, 1.000005);
    }

    [Fact]
    public void ClipGeneratedSurfaceToOverlaysPreservesClockwiseSourceWinding()
    {
        BootstrapParsedSurface surface = new(
            PolygonId: "dem-surface-clockwise",
            Semantic: BootstrapParsedSurfaceSemantic.Ground,
            ExteriorRing: new BootstrapParsedRing(
                "ring-clockwise",
                [
                    new GeodeticPoint(35.0100, 139.0200, 30.0),
                    new GeodeticPoint(35.0100, 139.0000, 20.0),
                    new GeodeticPoint(35.0000, 139.0000, 10.0),
                ],
                UVs: null),
            InteriorRings: [],
            BaseColor: new ColorRgba(1.0, 1.0, 1.0, 1.0),
            TexturePayload: null,
            UsesGeneratedDemTexture: true);
        TerrainTextureOverlay overlay = new(
            PackageName: "dem",
            UrlTemplate: "https://tiles.example/{z}/{x}/{y}.png",
            ZoomLevel: 18,
            GeographicBounds: new GeographicRectangle(
                MinLatitude: 35.0000,
                MaxLatitude: 35.0100,
                MinLongitude: 139.0040,
                MaxLongitude: 139.0120),
            MaxTextureSize: DemTerrainTextureDefaults.MaxTextureSize);

        (BootstrapParsedSurface clippedSurface, _) = Assert.Single(
            DemTerrainOverlaySurfaceClipper.ClipGeneratedSurfaceToOverlays(surface, [overlay]));

        Assert.Equal(
            Math.Sign(ComputeSignedArea(surface.ExteriorRing.Vertices)),
            Math.Sign(ComputeSignedArea(clippedSurface.ExteriorRing.Vertices)));
    }

    [Fact]
    public void ClipGeneratedSurfaceToOverlaysRespectsOverlayBoundaryForCentimeterBoundaryOverlap()
    {
        const double boundaryLongitude = 139.0100;
        BootstrapParsedSurface surface = new(
            PolygonId: "dem-boundary-overlap",
            Semantic: BootstrapParsedSurfaceSemantic.Ground,
            ExteriorRing: new BootstrapParsedRing(
                "ring-overlap",
                [
                    new GeodeticPoint(35.0000, 139.0000, 10.0),
                    new GeodeticPoint(35.0100, 139.0000, 20.0),
                    new GeodeticPoint(35.0100, boundaryLongitude + 0.0000005, 30.0),
                ],
                UVs: null),
            InteriorRings: [],
            BaseColor: new ColorRgba(1.0, 1.0, 1.0, 1.0),
            TexturePayload: null,
            UsesGeneratedDemTexture: true);
        TerrainTextureOverlay[] overlays =
        [
            new(
                PackageName: "dem",
                UrlTemplate: "https://tiles.example/{z}/{x}/{y}.png",
                ZoomLevel: 18,
                GeographicBounds: new GeographicRectangle(35.0000, 35.0100, 139.0000, boundaryLongitude),
                MaxTextureSize: DemTerrainTextureDefaults.MaxTextureSize),
            new(
                PackageName: "dem",
                UrlTemplate: "https://tiles.example/{z}/{x}/{y}.png",
                ZoomLevel: 18,
                GeographicBounds: new GeographicRectangle(35.0000, 35.0100, boundaryLongitude, 139.0200),
                MaxTextureSize: DemTerrainTextureDefaults.MaxTextureSize),
        ];

        IReadOnlyList<(BootstrapParsedSurface Surface, TerrainTextureOverlay Overlay)> clipped =
            DemTerrainOverlaySurfaceClipper.ClipGeneratedSurfaceToOverlays(surface, overlays);

        Assert.Equal(2, clipped.Count);
        GeographicRectangle firstBounds = GetSurfaceBounds(clipped[0].Surface);
        GeographicRectangle secondBounds = GetSurfaceBounds(clipped[1].Surface);
        Assert.InRange(firstBounds.MaxLongitude, boundaryLongitude - 1e-9, boundaryLongitude + 1e-9);
        Assert.InRange(secondBounds.MinLongitude, boundaryLongitude - 1e-9, boundaryLongitude + 1e-9);
        Assert.InRange(firstBounds.MinLongitude, 139.0000 - 1e-9, 139.0000 + 1e-9);
        Assert.InRange(secondBounds.MaxLongitude, boundaryLongitude, 139.0200);
    }

    [Fact]
    public void ClipGeneratedSurfaceToOverlaysInterpolatesNeutralUvs()
    {
        BootstrapParsedSurface surface = new(
            PolygonId: "dem-surface-uv",
            Semantic: BootstrapParsedSurfaceSemantic.Ground,
            ExteriorRing: new BootstrapParsedRing(
                "ring-uv",
                [
                    new GeodeticPoint(35.0000, 139.0000, 10.0),
                    new GeodeticPoint(35.0100, 139.0000, 20.0),
                    new GeodeticPoint(35.0100, 139.0200, 30.0),
                ],
                UVs:
                [
                    new Float2(0.0, 0.0),
                    new Float2(0.0, 1.0),
                    new Float2(1.0, 1.0),
                ]),
            InteriorRings: [],
            BaseColor: new ColorRgba(1.0, 1.0, 1.0, 1.0),
            TexturePayload: null,
            UsesGeneratedDemTexture: true);
        TerrainTextureOverlay overlay = new(
            PackageName: "dem",
            UrlTemplate: "https://tiles.example/{z}/{x}/{y}.png",
            ZoomLevel: 18,
            GeographicBounds: new GeographicRectangle(
                MinLatitude: 35.0000,
                MaxLatitude: 35.0100,
                MinLongitude: 139.0040,
                MaxLongitude: 139.0120),
            MaxTextureSize: DemTerrainTextureDefaults.MaxTextureSize);

        (BootstrapParsedSurface clippedSurface, _) = Assert.Single(
            DemTerrainOverlaySurfaceClipper.ClipGeneratedSurfaceToOverlays(surface, [overlay]));

        Float2[] uvs = Assert.IsAssignableFrom<IReadOnlyList<Float2>>(clippedSurface.ExteriorRing.UVs).ToArray();
        Assert.Equal(clippedSurface.ExteriorRing.Vertices.Length, uvs.Length);
        Assert.Contains(uvs, static uv => Math.Abs(uv.X - 0.2) < 1e-6 && Math.Abs(uv.Y - 0.2) < 1e-6);
        Assert.Contains(uvs, static uv => Math.Abs(uv.X - 0.6) < 1e-6 && Math.Abs(uv.Y - 1.0) < 1e-6);
        Assert.Contains(uvs, static uv => Math.Abs(uv.X - 0.6) < 1e-6 && Math.Abs(uv.Y - 0.6) < 1e-6);
    }

    [Fact]
    public void ClipGeneratedSurfaceToOverlaysPreservesDisconnectedConcaveIntersectionComponents()
    {
        BootstrapParsedSurface surface = new(
            PolygonId: "dem-surface-disconnected-concave",
            Semantic: BootstrapParsedSurfaceSemantic.Ground,
            ExteriorRing: new BootstrapParsedRing(
                "ring-disconnected",
                [
                    new GeodeticPoint(35.0000, 139.0000, 10.0),
                    new GeodeticPoint(35.0000, 139.0100, 10.0),
                    new GeodeticPoint(35.0001, 139.0100, 10.0),
                    new GeodeticPoint(35.0001, 139.0020, 10.0),
                    new GeodeticPoint(35.0009, 139.0020, 10.0),
                    new GeodeticPoint(35.0009, 139.0100, 10.0),
                    new GeodeticPoint(35.0010, 139.0100, 10.0),
                    new GeodeticPoint(35.0010, 139.0000, 10.0),
                ],
                UVs: null),
            InteriorRings: [],
            BaseColor: new ColorRgba(1.0, 1.0, 1.0, 1.0),
            TexturePayload: null,
            UsesGeneratedDemTexture: true);
        TerrainTextureOverlay overlay = new(
            PackageName: "dem",
            UrlTemplate: "https://tiles.example/{z}/{x}/{y}.png",
            ZoomLevel: 18,
            GeographicBounds: new GeographicRectangle(
                MinLatitude: 35.0000,
                MaxLatitude: 35.0010,
                MinLongitude: 139.0020,
                MaxLongitude: 139.0080),
            MaxTextureSize: DemTerrainTextureDefaults.MaxTextureSize);

        IReadOnlyList<(BootstrapParsedSurface Surface, TerrainTextureOverlay Overlay)> clipped = DemTerrainOverlaySurfaceClipper
            .ClipGeneratedSurfaceToOverlays(surface, [overlay]);

        Assert.Equal(2, clipped.Count);

        GeographicRectangle[] bounds = clipped
            .Select(static entry => GetSurfaceBounds(entry.Surface))
            .OrderBy(static bound => bound.MinLatitude)
            .ToArray();
        Assert.InRange(bounds[0].MaxLatitude, 35.0000, 35.0001);
        Assert.InRange(bounds[0].MinLatitude, 35.0000, 35.0001);
        Assert.InRange(bounds[1].MinLatitude, 35.0009, 35.0010);
        Assert.InRange(bounds[1].MaxLatitude, 35.0009, 35.0010);
        Assert.True(bounds[0].MaxLatitude < bounds[1].MinLatitude);
    }

    private static double ComputeSignedArea(GeodeticPoint[] vertices)
    {
        double signedArea = 0.0;
        for (int index = 0; index < vertices.Length; index++)
        {
            GeodeticPoint current = vertices[index];
            GeodeticPoint next = vertices[(index + 1) % vertices.Length];
            signedArea += (current.Longitude * next.Latitude) - (next.Longitude * current.Latitude);
        }

        return signedArea * 0.5;
    }

    private static double ComputeApproximateArea(GeodeticPoint[] vertices)
    {
        if (vertices.Length < 3)
        {
            return 0.0;
        }

        double referenceLatitudeRadians = vertices.Average(static point => point.Latitude) * (Math.PI / 180.0);
        double metersPerLatitudeDegree = 111_320.0;
        double metersPerLongitudeDegree = metersPerLatitudeDegree * Math.Cos(referenceLatitudeRadians);
        double signedArea = 0.0;
        for (int index = 0; index < vertices.Length; index++)
        {
            GeodeticPoint current = vertices[index];
            GeodeticPoint next = vertices[(index + 1) % vertices.Length];
            double currentX = current.Longitude * metersPerLongitudeDegree;
            double currentY = current.Latitude * metersPerLatitudeDegree;
            double nextX = next.Longitude * metersPerLongitudeDegree;
            double nextY = next.Latitude * metersPerLatitudeDegree;
            signedArea += (currentX * nextY) - (nextX * currentY);
        }

        return Math.Abs(signedArea) * 0.5;
    }

    private static GeographicRectangle GetSurfaceBounds(BootstrapParsedSurface surface)
    {
        return new GeographicRectangle(
            MinLatitude: surface.ExteriorRing.Vertices.Min(static point => point.Latitude),
            MaxLatitude: surface.ExteriorRing.Vertices.Max(static point => point.Latitude),
            MinLongitude: surface.ExteriorRing.Vertices.Min(static point => point.Longitude),
            MaxLongitude: surface.ExteriorRing.Vertices.Max(static point => point.Longitude));
    }
}
