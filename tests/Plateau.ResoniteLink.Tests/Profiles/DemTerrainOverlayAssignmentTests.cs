using Plateau.ResoniteLink.Application.Importing;
using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Tests.Application;

public sealed class DemTerrainOverlayAssignmentTests
{
    [Fact]
    public void SplitParsedCityObjectCollapsesCentimeterClassBoundarySliverToDominantClippedOverlay()
    {
        const double boundaryLongitude = 139.0100;
        LocalCityGmlObjectProjection.ParsedSurface surface = CreateGeneratedSurface(
            "dem-sliver",
            [
                new LocalCityGmlObjectProjection.GeodeticPoint(35.0000, 139.0000, 0.0),
                new LocalCityGmlObjectProjection.GeodeticPoint(35.0100, 139.0000, 1.0),
                new LocalCityGmlObjectProjection.GeodeticPoint(35.0100, boundaryLongitude + 0.0000005, 2.0),
            ]);
        LocalCityGmlObjectProjection.ParsedCityObject cityObject = CreateCityObject(surface);
        TerrainTextureOverlay[] overlays =
        [
            CreateOverlay(139.0000, boundaryLongitude),
            CreateOverlay(boundaryLongitude, 139.0200),
        ];

        (LocalCityGmlObjectProjection.ParsedCityObject CityObject, TerrainTextureOverlay? Overlay)[] results =
            DemTerrainOverlayAssignment.SplitParsedCityObject(cityObject, overlays).ToArray();

        (LocalCityGmlObjectProjection.ParsedCityObject splitCityObject, TerrainTextureOverlay? overlay) = Assert.Single(results);
        Assert.NotNull(overlay);
        Assert.Equal(139.0000, overlay.GeographicBounds.MinLongitude, 6);
        Assert.Equal(boundaryLongitude, overlay.GeographicBounds.MaxLongitude, 6);

        LocalCityGmlObjectProjection.ParsedSurface collapsedSurface = Assert.Single(splitCityObject.Surfaces);
        Assert.True(collapsedSurface.UsesGeneratedDemTexture);
        Assert.All(
            collapsedSurface.ExteriorRing.Vertices,
            vertex => Assert.InRange(vertex.Longitude, 139.0000, boundaryLongitude));
        double sourceArea = ComputeApproximateArea(surface.ExteriorRing.Vertices);
        double collapsedArea = ComputeApproximateArea(collapsedSurface.ExteriorRing.Vertices);
        Assert.InRange(collapsedArea / sourceArea, 0.9999, 1.000001);
    }

    [Fact]
    public void SplitParsedCityObjectKeepsMeaningfulBoundarySplit()
    {
        const double boundaryLongitude = 139.0100;
        LocalCityGmlObjectProjection.ParsedSurface surface = CreateGeneratedSurface(
            "dem-wide-split",
            [
                new LocalCityGmlObjectProjection.GeodeticPoint(35.0000, 139.0000, 0.0),
                new LocalCityGmlObjectProjection.GeodeticPoint(35.0100, 139.0000, 1.0),
                new LocalCityGmlObjectProjection.GeodeticPoint(35.0100, 139.0120, 2.0),
            ]);
        LocalCityGmlObjectProjection.ParsedCityObject cityObject = CreateCityObject(surface);
        TerrainTextureOverlay[] overlays =
        [
            CreateOverlay(139.0000, boundaryLongitude),
            CreateOverlay(boundaryLongitude, 139.0200),
        ];

        (LocalCityGmlObjectProjection.ParsedCityObject CityObject, TerrainTextureOverlay? Overlay)[] results =
            DemTerrainOverlayAssignment.SplitParsedCityObject(cityObject, overlays).ToArray();

        Assert.Equal(2, results.Length);
        Assert.All(results, static result => Assert.Single(result.CityObject.Surfaces));
        Assert.Contains(
            results,
            static result => result.Overlay is not null
                && result.Overlay.GeographicBounds.MinLongitude == 139.0000
                && result.Overlay.GeographicBounds.MaxLongitude == boundaryLongitude);
        Assert.Contains(
            results,
            static result => result.Overlay is not null
                && result.Overlay.GeographicBounds.MinLongitude == boundaryLongitude
                && result.Overlay.GeographicBounds.MaxLongitude == 139.0200);
        Assert.All(
            results.Where(static result => result.Overlay is not null),
            result =>
            {
                GeographicRectangle bounds = GetSurfaceBounds(Assert.Single(result.CityObject.Surfaces));
                Assert.True(bounds.MinLongitude >= result.Overlay!.GeographicBounds.MinLongitude - 1e-9);
                Assert.True(bounds.MaxLongitude <= result.Overlay.GeographicBounds.MaxLongitude + 1e-9);
            });
    }

    [Fact]
    public void SplitParsedCityObjectCollapsesMultipleThinBoundarySliversToDominantOverlay()
    {
        const double boundaryOne = 139.0100;
        const double boundaryTwo = 139.0100003;
        const double boundaryThree = 139.0100006;
        LocalCityGmlObjectProjection.ParsedSurface surface = CreateGeneratedSurface(
            "dem-many-slivers",
            [
                new LocalCityGmlObjectProjection.GeodeticPoint(35.0000, 139.0000, 0.0),
                new LocalCityGmlObjectProjection.GeodeticPoint(35.0100, 139.0000, 1.0),
                new LocalCityGmlObjectProjection.GeodeticPoint(35.0100, boundaryThree + 0.0000002, 2.0),
            ]);
        LocalCityGmlObjectProjection.ParsedCityObject cityObject = CreateCityObject(surface);
        TerrainTextureOverlay[] overlays =
        [
            CreateOverlay(139.0000, boundaryOne),
            CreateOverlay(boundaryOne, boundaryTwo),
            CreateOverlay(boundaryTwo, boundaryThree),
            CreateOverlay(boundaryThree, 139.0200),
        ];

        (LocalCityGmlObjectProjection.ParsedCityObject CityObject, TerrainTextureOverlay? Overlay)[] results =
            DemTerrainOverlayAssignment.SplitParsedCityObject(cityObject, overlays).ToArray();

        (LocalCityGmlObjectProjection.ParsedCityObject splitCityObject, TerrainTextureOverlay? overlay) = Assert.Single(results);
        Assert.NotNull(overlay);
        Assert.Equal(139.0000, overlay.GeographicBounds.MinLongitude, 6);
        Assert.Equal(boundaryOne, overlay.GeographicBounds.MaxLongitude, 6);
        LocalCityGmlObjectProjection.ParsedSurface collapsedSurface = Assert.Single(splitCityObject.Surfaces);
        Assert.All(
            collapsedSurface.ExteriorRing.Vertices,
            vertex => Assert.InRange(vertex.Longitude, 139.0000, boundaryOne));
        double sourceArea = ComputeApproximateArea(surface.ExteriorRing.Vertices);
        double collapsedArea = ComputeApproximateArea(collapsedSurface.ExteriorRing.Vertices);
        Assert.InRange(collapsedArea / sourceArea, 0.9999, 1.000001);
    }

    [Fact]
    public void SplitParsedCityObjectRejectsGeneratedSurfaceWhenSurfaceMissesOverlayBounds()
    {
        LocalCityGmlObjectProjection.ParsedSurface surface = CreateGeneratedSurface(
            "dem-nearest-overlay",
            [
                new LocalCityGmlObjectProjection.GeodeticPoint(35.0000, 139.0200002, 0.0),
                new LocalCityGmlObjectProjection.GeodeticPoint(35.0100, 139.0200002, 1.0),
                new LocalCityGmlObjectProjection.GeodeticPoint(35.0100, 139.0200006, 2.0),
            ]);
        LocalCityGmlObjectProjection.ParsedCityObject cityObject = CreateCityObject(surface);
        TerrainTextureOverlay[] overlays =
        [
            CreateOverlay(139.0000, 139.0100),
            CreateOverlay(139.0100, 139.0200),
        ];

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => DemTerrainOverlayAssignment.SplitParsedCityObject(cityObject, overlays).ToArray());

        Assert.Contains("no matching terrain overlay coverage", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SplitParsedCityObjectClipsSingleIntersectingOverlayBeforeAssignment()
    {
        const double overlayEastLongitude = 139.0100;
        LocalCityGmlObjectProjection.ParsedSurface surface = CreateGeneratedSurface(
            "dem-single-overlay-clip",
            [
                new LocalCityGmlObjectProjection.GeodeticPoint(35.0000, 139.0000, 0.0),
                new LocalCityGmlObjectProjection.GeodeticPoint(35.0100, 139.0000, 1.0),
                new LocalCityGmlObjectProjection.GeodeticPoint(35.0100, 139.0120, 2.0),
            ]);
        LocalCityGmlObjectProjection.ParsedCityObject cityObject = CreateCityObject(surface);
        TerrainTextureOverlay[] overlays =
        [
            CreateOverlay(139.0000, overlayEastLongitude),
        ];

        (LocalCityGmlObjectProjection.ParsedCityObject CityObject, TerrainTextureOverlay? Overlay)[] results =
            DemTerrainOverlayAssignment.SplitParsedCityObject(cityObject, overlays).ToArray();

        (LocalCityGmlObjectProjection.ParsedCityObject splitCityObject, TerrainTextureOverlay? overlay) = Assert.Single(results);
        Assert.NotNull(overlay);
        GeographicRectangle bounds = GetSurfaceBounds(Assert.Single(splitCityObject.Surfaces));
        Assert.True(bounds.MinLongitude >= overlay.GeographicBounds.MinLongitude - 1e-9);
        Assert.True(bounds.MaxLongitude <= overlay.GeographicBounds.MaxLongitude + 1e-9);
        Assert.Equal(overlayEastLongitude, bounds.MaxLongitude, 6);
    }

    [Fact]
    public void SplitParsedCityObjectDropsGeneratedSurfacesOutsideRequestedMeshCoverage()
    {
        LocalCityGmlObjectProjection.ParsedSurface coveredSurface = CreateGeneratedSurface(
            "dem-covered",
            [
                new LocalCityGmlObjectProjection.GeodeticPoint(35.0000, 139.0000, 0.0),
                new LocalCityGmlObjectProjection.GeodeticPoint(35.0100, 139.0000, 1.0),
                new LocalCityGmlObjectProjection.GeodeticPoint(35.0100, 139.0120, 2.0),
            ]);
        LocalCityGmlObjectProjection.ParsedSurface uncoveredSurface = CreateGeneratedSurface(
            "dem-uncovered",
            [
                new LocalCityGmlObjectProjection.GeodeticPoint(35.0000, 139.0200002, 0.0),
                new LocalCityGmlObjectProjection.GeodeticPoint(35.0100, 139.0200002, 1.0),
                new LocalCityGmlObjectProjection.GeodeticPoint(35.0100, 139.0200006, 2.0),
            ]);
        LocalCityGmlObjectProjection.ParsedCityObject cityObject = CreateCityObject(coveredSurface) with
        {
            Surfaces = [coveredSurface, uncoveredSurface],
            SharedAcrossMeshCodes = true,
        };
        TerrainTextureOverlay[] overlays =
        [
            CreateOverlay(139.0000, 139.0100),
        ];
        MeshCodeBounds[] requestedMeshAreas =
        [
            new(35.0000, 35.0200, 139.0000, 139.0100),
        ];

        (LocalCityGmlObjectProjection.ParsedCityObject CityObject, TerrainTextureOverlay? Overlay)[] results =
            DemTerrainOverlayAssignment.SplitParsedCityObject(cityObject, overlays, requestedMeshAreas).ToArray();

        (LocalCityGmlObjectProjection.ParsedCityObject splitCityObject, TerrainTextureOverlay? overlay) = Assert.Single(results);
        Assert.NotNull(overlay);
        Assert.DoesNotContain(splitCityObject.Surfaces, static surface => surface.PolygonId == "dem-uncovered");
        Assert.Contains(splitCityObject.Surfaces, static surface => surface.PolygonId.StartsWith("dem-covered", StringComparison.Ordinal));
    }

    [Fact]
    public void SplitParsedCityObjectClipsSharedDemToRequestedMeshEvenWhenNoOverlaysExist()
    {
        LocalCityGmlObjectProjection.ParsedSurface surface = CreateGeneratedSurface(
            "dem-parent",
            [
                new LocalCityGmlObjectProjection.GeodeticPoint(35.0000, 139.0000, 0.0),
                new LocalCityGmlObjectProjection.GeodeticPoint(35.0100, 139.0000, 1.0),
                new LocalCityGmlObjectProjection.GeodeticPoint(35.0100, 139.0200, 2.0),
            ]);
        LocalCityGmlObjectProjection.ParsedCityObject cityObject = CreateCityObject(surface) with
        {
            SharedAcrossMeshCodes = true,
        };
        MeshCodeBounds[] requestedMeshAreas =
        [
            new(35.0000, 35.0200, 139.0000, 139.0100),
        ];

        (LocalCityGmlObjectProjection.ParsedCityObject CityObject, TerrainTextureOverlay? Overlay)[] results =
            DemTerrainOverlayAssignment.SplitParsedCityObject(cityObject, [], requestedMeshAreas).ToArray();

        (LocalCityGmlObjectProjection.ParsedCityObject splitCityObject, TerrainTextureOverlay? overlay) = Assert.Single(results);
        Assert.Null(overlay);
        GeographicRectangle bounds = GetSurfaceBounds(Assert.Single(splitCityObject.Surfaces));
        Assert.True(bounds.MinLongitude >= 139.0000 - 1e-9);
        Assert.True(bounds.MaxLongitude <= 139.0100 + 1e-9);
    }

    [Fact]
    public void SplitParsedCityObjectClipsSharedExplicitTextureDemSurfaceToRequestedMesh()
    {
        LocalCityGmlObjectProjection.ParsedSurface surface = new(
            PolygonId: "dem-explicit",
            Semantic: LocalCityGmlObjectProjection.ParsedSurfaceSemantic.Ground,
            ExteriorRing: new LocalCityGmlObjectProjection.ParsedRing(
                "dem-explicit-ring",
                [
                    new LocalCityGmlObjectProjection.GeodeticPoint(35.0000, 139.0000, 0.0),
                    new LocalCityGmlObjectProjection.GeodeticPoint(35.0100, 139.0000, 1.0),
                    new LocalCityGmlObjectProjection.GeodeticPoint(35.0100, 139.0200, 2.0),
                ],
                [
                    new ResoniteFloat2(0.0, 0.0),
                    new ResoniteFloat2(0.0, 1.0),
                    new ResoniteFloat2(1.0, 1.0),
                ]),
            InteriorRings: [],
            BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
            TexturePayload: new ResoniteTexturePayload(
                Width: 2,
                Height: 2,
                ColorProfile: "sRGB",
                BinaryPayload: [1, 2, 3],
                Identity: "texture.png",
                Format: ResoniteTexturePayloadFormat.EncodedImage),
            UsesGeneratedDemTexture: false);
        LocalCityGmlObjectProjection.ParsedCityObject cityObject = CreateCityObject(surface) with
        {
            SharedAcrossMeshCodes = true,
        };
        MeshCodeBounds[] requestedMeshAreas =
        [
            new(35.0000, 35.0200, 139.0000, 139.0100),
        ];

        (LocalCityGmlObjectProjection.ParsedCityObject CityObject, TerrainTextureOverlay? Overlay)[] results =
            DemTerrainOverlayAssignment.SplitParsedCityObject(cityObject, [], requestedMeshAreas).ToArray();

        (LocalCityGmlObjectProjection.ParsedCityObject splitCityObject, TerrainTextureOverlay? overlay) = Assert.Single(results);
        Assert.Null(overlay);
        LocalCityGmlObjectProjection.ParsedSurface clippedSurface = Assert.Single(splitCityObject.Surfaces);
        GeographicRectangle bounds = GetSurfaceBounds(clippedSurface);
        Assert.True(bounds.MinLongitude >= 139.0000 - 1e-9);
        Assert.True(bounds.MaxLongitude <= 139.0100 + 1e-9);
        Assert.NotNull(clippedSurface.ExteriorRing.UVs);
        Assert.Equal(clippedSurface.ExteriorRing.Vertices.Length, clippedSurface.ExteriorRing.UVs!.Count);
    }

    private static LocalCityGmlObjectProjection.ParsedCityObject CreateCityObject(
        LocalCityGmlObjectProjection.ParsedSurface surface)
    {
        return new LocalCityGmlObjectProjection.ParsedCityObject(
            SlotKey: "dem-object",
            DisplayName: "DEM Object",
            PackageName: "dem",
            ActualMeshCode: "53394525",
            LodLevel: 1,
            Surfaces: [surface],
            ReferenceSystem: LocalCityGmlObjectProjection.CoordinateReferenceSystem.Parse("EPSG:4326"),
            SourceFileRelativePath: "udx/dem/53394525/sample.gml",
            SourceUnitIdentity: "source-unit",
            SourceIdentity: "source",
            SharedAcrossMeshCodes: false);
    }

    private static LocalCityGmlObjectProjection.ParsedSurface CreateGeneratedSurface(
        string polygonId,
        LocalCityGmlObjectProjection.GeodeticPoint[] vertices)
    {
        return new LocalCityGmlObjectProjection.ParsedSurface(
            PolygonId: polygonId,
            Semantic: LocalCityGmlObjectProjection.ParsedSurfaceSemantic.Ground,
            ExteriorRing: new LocalCityGmlObjectProjection.ParsedRing(
                $"{polygonId}-ring",
                vertices,
                UVs: null),
            InteriorRings: [],
            BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
            TexturePayload: null,
            UsesGeneratedDemTexture: true);
    }

    private static TerrainTextureOverlay CreateOverlay(double westLongitude, double eastLongitude)
    {
        return new TerrainTextureOverlay(
            PackageName: "dem",
            UrlTemplate: "https://tiles.example/{z}/{x}/{y}.png",
            ZoomLevel: 18,
            GeographicBounds: new GeographicRectangle(
                MinLatitude: 35.0000,
                MaxLatitude: 35.0200,
                MinLongitude: westLongitude,
                MaxLongitude: eastLongitude),
            MaxTextureSize: LocalCityGmlObjectProjection.DefaultDemTerrainTextureMaxSize);
    }

    private static GeographicRectangle GetSurfaceBounds(LocalCityGmlObjectProjection.ParsedSurface surface)
    {
        return new GeographicRectangle(
            MinLatitude: surface.ExteriorRing.Vertices.Min(static point => point.Latitude),
            MaxLatitude: surface.ExteriorRing.Vertices.Max(static point => point.Latitude),
            MinLongitude: surface.ExteriorRing.Vertices.Min(static point => point.Longitude),
            MaxLongitude: surface.ExteriorRing.Vertices.Max(static point => point.Longitude));
    }

    private static double ComputeApproximateArea(LocalCityGmlObjectProjection.GeodeticPoint[] vertices)
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
            LocalCityGmlObjectProjection.GeodeticPoint current = vertices[index];
            LocalCityGmlObjectProjection.GeodeticPoint next = vertices[(index + 1) % vertices.Length];
            double currentX = current.Longitude * metersPerLongitudeDegree;
            double currentY = current.Latitude * metersPerLatitudeDegree;
            double nextX = next.Longitude * metersPerLongitudeDegree;
            double nextY = next.Latitude * metersPerLatitudeDegree;
            signedArea += (currentX * nextY) - (nextX * currentY);
        }

        return Math.Abs(signedArea) * 0.5;
    }

}
