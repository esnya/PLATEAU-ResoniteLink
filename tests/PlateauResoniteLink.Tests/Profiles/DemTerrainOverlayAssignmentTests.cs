using System;
using System.Linq;

using PlateauResoniteLink.Application.Importing;
using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Tests.Profiles;

public sealed class DemTerrainOverlayAssignmentTests
{
    [Fact]
    public void SplitParsedCityObjectCollapsesCentimeterClassBoundarySliverToDominantClippedOverlay()
    {
        const double boundaryLongitude = 139.0100;
        ParsedSurface surface = CreateGeneratedSurface(
            "dem-sliver",
            [
                new GeodeticPoint(35.0000, 139.0000, 0.0),
                new GeodeticPoint(35.0100, 139.0000, 1.0),
                new GeodeticPoint(35.0100, boundaryLongitude + 0.0000005, 2.0),
            ]);
        ParsedCityObject cityObject = CreateCityObject(surface);
        TerrainTextureOverlay[] overlays =
        [
            CreateOverlay(139.0000, boundaryLongitude),
            CreateOverlay(boundaryLongitude, 139.0200),
        ];

        (ParsedCityObject CityObject, TerrainTextureOverlay? Overlay)[] results =
            DemTerrainOverlayAssignment.SplitParsedCityObject(cityObject, overlays).ToArray();

        (ParsedCityObject splitCityObject, TerrainTextureOverlay? overlay) = Assert.Single(results);
        Assert.NotNull(overlay);
        Assert.Equal(139.0000, overlay.GeographicBounds.MinLongitude, 6);
        Assert.Equal(boundaryLongitude, overlay.GeographicBounds.MaxLongitude, 6);
        Assert.True(Assert.Single(splitCityObject.Surfaces).UsesGeneratedDemTexture);
        GeographicRectangle bounds = GetSurfaceBounds(Assert.Single(splitCityObject.Surfaces));
        Assert.Equal(139.0000, bounds.MinLongitude, 6);
        Assert.Equal(boundaryLongitude, bounds.MaxLongitude, 6);
    }

    [Fact]
    public void SplitParsedCityObjectKeepsMeaningfulBoundarySplit()
    {
        const double boundaryLongitude = 139.0100;
        ParsedSurface surface = CreateGeneratedSurface(
            "dem-wide-split",
            [
                new GeodeticPoint(35.0000, 139.0000, 0.0),
                new GeodeticPoint(35.0100, 139.0000, 1.0),
                new GeodeticPoint(35.0100, 139.0120, 2.0),
            ]);
        ParsedCityObject cityObject = CreateCityObject(surface);
        TerrainTextureOverlay[] overlays =
        [
            CreateOverlay(139.0000, boundaryLongitude),
            CreateOverlay(boundaryLongitude, 139.0200),
        ];

        (ParsedCityObject CityObject, TerrainTextureOverlay? Overlay)[] results =
            DemTerrainOverlayAssignment.SplitParsedCityObject(cityObject, overlays).ToArray();

        Assert.Equal(2, results.Length);
        Assert.All(results, static result => Assert.Single(result.CityObject.Surfaces));
    }

    [Fact]
    public void SplitParsedCityObjectPrunesBoundarySliverOverlayGroupAcrossSurfaces()
    {
        const double boundaryLongitude = 139.0100;
        ParsedSurface dominantSurface = CreateGeneratedSurface(
            "dem-dominant",
            [
                new GeodeticPoint(35.0000, 139.0000, 0.0),
                new GeodeticPoint(35.0100, 139.0000, 1.0),
                new GeodeticPoint(35.0100, boundaryLongitude, 2.0),
            ]);
        ParsedSurface sliverSurface = CreateGeneratedSurface(
            "dem-sliver-group",
            [
                new GeodeticPoint(35.0000, boundaryLongitude, 0.0),
                new GeodeticPoint(35.0100, boundaryLongitude, 1.0),
                new GeodeticPoint(35.0100, boundaryLongitude + 0.0000005, 2.0),
            ]);
        ParsedCityObject cityObject = CreateCityObject(dominantSurface) with
        {
            Surfaces = [dominantSurface, sliverSurface],
        };
        TerrainTextureOverlay[] overlays =
        [
            CreateOverlay(139.0000, boundaryLongitude),
            CreateOverlay(boundaryLongitude, 139.0200),
        ];

        (ParsedCityObject CityObject, TerrainTextureOverlay? Overlay)[] results =
            DemTerrainOverlayAssignment.SplitParsedCityObject(cityObject, overlays).ToArray();

        (ParsedCityObject splitCityObject, TerrainTextureOverlay? overlay) = Assert.Single(results);
        Assert.NotNull(overlay);
        Assert.Equal(139.0000, overlay.GeographicBounds.MinLongitude, 6);
        Assert.Equal(boundaryLongitude, overlay.GeographicBounds.MaxLongitude, 6);
        Assert.Equal("dem-dominant", Assert.Single(splitCityObject.Surfaces).PolygonId);
    }

    [Fact]
    public void SplitParsedCityObjectKeepsSmallCompactOverlayGroupAcrossSurfaces()
    {
        const double boundaryLongitude = 139.0100;
        ParsedSurface dominantSurface = CreateGeneratedSurface(
            "dem-dominant",
            [
                new GeodeticPoint(35.0000, 139.0000, 0.0),
                new GeodeticPoint(35.0100, 139.0000, 1.0),
                new GeodeticPoint(35.0100, boundaryLongitude, 2.0),
            ]);
        ParsedSurface compactSurface = CreateGeneratedSurface(
            "dem-small-compact",
            [
                new GeodeticPoint(35.00000, boundaryLongitude + 0.00020, 0.0),
                new GeodeticPoint(35.00001, boundaryLongitude + 0.00020, 1.0),
                new GeodeticPoint(35.00001, boundaryLongitude + 0.00021, 2.0),
            ]);
        ParsedCityObject cityObject = CreateCityObject(dominantSurface) with
        {
            Surfaces = [dominantSurface, compactSurface],
        };
        TerrainTextureOverlay[] overlays =
        [
            CreateOverlay(139.0000, boundaryLongitude),
            CreateOverlay(boundaryLongitude, 139.0200),
        ];

        (ParsedCityObject CityObject, TerrainTextureOverlay? Overlay)[] results =
            DemTerrainOverlayAssignment.SplitParsedCityObject(cityObject, overlays).ToArray();

        Assert.Equal(2, results.Length);
        Assert.Contains(
            results,
            static result => Assert.Single(result.CityObject.Surfaces).PolygonId == "dem-small-compact");
    }

    [Fact]
    public void SplitParsedCityObjectKeepsCompactSurfaceWhenMixedWithSliverInSmallOverlayGroup()
    {
        const double boundaryLongitude = 139.0100;
        ParsedSurface dominantSurface = CreateGeneratedSurface(
            "dem-dominant",
            [
                new GeodeticPoint(35.0000, 139.0000, 0.0),
                new GeodeticPoint(35.0100, 139.0000, 1.0),
                new GeodeticPoint(35.0100, boundaryLongitude, 2.0),
            ]);
        ParsedSurface sliverSurface = CreateGeneratedSurface(
            "dem-mixed-sliver",
            [
                new GeodeticPoint(35.00000, boundaryLongitude + 0.0000010, 0.0),
                new GeodeticPoint(35.01000, boundaryLongitude + 0.0000010, 1.0),
                new GeodeticPoint(35.01000, boundaryLongitude + 0.0000015, 2.0),
            ]);
        ParsedSurface compactSurface = CreateGeneratedSurface(
            "dem-mixed-compact",
            [
                new GeodeticPoint(35.00000, boundaryLongitude + 0.00020, 0.0),
                new GeodeticPoint(35.00001, boundaryLongitude + 0.00020, 1.0),
                new GeodeticPoint(35.00001, boundaryLongitude + 0.00021, 2.0),
            ]);
        ParsedCityObject cityObject = CreateCityObject(dominantSurface) with
        {
            Surfaces = [dominantSurface, sliverSurface, compactSurface],
        };
        TerrainTextureOverlay[] overlays =
        [
            CreateOverlay(139.0000, boundaryLongitude),
            CreateOverlay(boundaryLongitude, 139.0200),
        ];

        (ParsedCityObject CityObject, TerrainTextureOverlay? Overlay)[] results =
            DemTerrainOverlayAssignment.SplitParsedCityObject(cityObject, overlays).ToArray();

        Assert.Equal(2, results.Length);
        ParsedCityObject mixedOverlayObject = Assert.Single(
            results.Select(static result => result.CityObject),
            static cityObject => cityObject.Surfaces.Any(static surface => surface.PolygonId == "dem-mixed-compact"));
        Assert.Contains(mixedOverlayObject.Surfaces, static surface => surface.PolygonId == "dem-mixed-sliver");
        Assert.Contains(mixedOverlayObject.Surfaces, static surface => surface.PolygonId == "dem-mixed-compact");
    }

    [Fact]
    public void SplitParsedCityObjectRejectsGeneratedSurfaceWhenSurfaceMissesOverlayBounds()
    {
        ParsedSurface surface = CreateGeneratedSurface(
            "dem-nearest-overlay",
            [
                new GeodeticPoint(35.0000, 139.0200002, 0.0),
                new GeodeticPoint(35.0100, 139.0200002, 1.0),
                new GeodeticPoint(35.0100, 139.0200006, 2.0),
            ]);
        ParsedCityObject cityObject = CreateCityObject(surface);
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
    public void HasOverlayCoverageReturnsTrueForParsedCityObjectWhenSplitCoverageExists()
    {
        const double boundaryLongitude = 139.0100;
        ParsedCityObject cityObject = CreateCityObject(
            CreateGeneratedSurface(
                "dem-wide-split",
                [
                    new GeodeticPoint(35.0000, 139.0000, 0.0),
                    new GeodeticPoint(35.0100, 139.0000, 1.0),
                    new GeodeticPoint(35.0100, 139.0120, 2.0),
                ]));
        TerrainTextureOverlay[] overlays =
        [
            CreateOverlay(139.0000, boundaryLongitude),
            CreateOverlay(boundaryLongitude, 139.0200),
        ];

        bool hasCoverage = DemTerrainOverlayAssignment.HasOverlayCoverage(cityObject, overlays);

        Assert.True(hasCoverage);
    }

    [Fact]
    public void HasOverlayCoverageReturnsFalseForParsedCityObjectWhenSurfaceMissesOverlayBounds()
    {
        ParsedCityObject cityObject = CreateCityObject(
            CreateGeneratedSurface(
                "dem-nearest-overlay",
                [
                    new GeodeticPoint(35.0000, 139.0200002, 0.0),
                    new GeodeticPoint(35.0100, 139.0200002, 1.0),
                    new GeodeticPoint(35.0100, 139.0200006, 2.0),
                ]));
        TerrainTextureOverlay[] overlays =
        [
            CreateOverlay(139.0000, 139.0100),
            CreateOverlay(139.0100, 139.0200),
        ];

        bool hasCoverage = DemTerrainOverlayAssignment.HasOverlayCoverage(cityObject, overlays);

        Assert.False(hasCoverage);
    }

    [Fact]
    public void HasOverlayCoverageReturnsFalseForParsedCityObjectWhenSurfaceHasNoVertices()
    {
        ParsedCityObject cityObject = CreateCityObject(
            CreateGeneratedSurface("dem-empty-surface", []));
        TerrainTextureOverlay[] overlays =
        [
            CreateOverlay(139.0000, 139.0100),
        ];

        bool hasCoverage = DemTerrainOverlayAssignment.HasOverlayCoverage(cityObject, overlays);

        Assert.False(hasCoverage);
    }

    [Fact]
    public void SplitParsedCityObjectClipsSharedDemToRequestedMeshEvenWhenNoOverlaysExist()
    {
        ParsedSurface surface = CreateGeneratedSurface(
            "dem-parent",
            [
                new GeodeticPoint(35.0000, 139.0000, 0.0),
                new GeodeticPoint(35.0100, 139.0000, 1.0),
                new GeodeticPoint(35.0100, 139.0200, 2.0),
            ]);
        ParsedCityObject cityObject = CreateCityObject(surface) with
        {
            SharedAcrossMeshCodes = true,
        };
        MeshCodeBounds[] requestedMeshCodeBounds =
        [
            new(35.0000, 35.0200, 139.0000, 139.0100),
        ];

        (ParsedCityObject CityObject, TerrainTextureOverlay? Overlay)[] results =
            DemTerrainOverlayAssignment.SplitParsedCityObject(cityObject, [], requestedMeshCodeBounds).ToArray();

        (ParsedCityObject splitCityObject, TerrainTextureOverlay? overlay) = Assert.Single(results);
        Assert.Null(overlay);
        GeographicRectangle bounds = GetSurfaceBounds(Assert.Single(splitCityObject.Surfaces));
        Assert.True(bounds.MaxLongitude <= 139.0100 + 1e-9);
    }

    [Fact]
    public void TryCreateTerrainGridTextureTransformReturnsOccupiedOverlayUvTransform()
    {
        ParsedSurface surface = CreateGeneratedSurface(
            "dem-transform",
            [
                new GeodeticPoint(35.0000, 139.0000, 0.0),
                new GeodeticPoint(35.0100, 139.0000, 1.0),
                new GeodeticPoint(35.0100, 139.0100, 2.0),
            ]);
        ParsedCityObject cityObject = CreateCityObject(surface);
        TerrainTextureOverlay overlay = new(
            PackageName: "dem",
            GeographicBounds: new GeographicRectangle(35.0000, 35.0200, 139.0000, 139.0200),
            MaxTextureSize: DemTerrainTextureDefaults.MaxTextureSize,
            Sources: [new TerrainTextureTileSource("https://tiles.example/{z}/{x}/{y}.png", 18)]);
        GeographicRectangle objectBounds = GetSurfaceBounds(surface);
        ResolvedSurfaceMaterial material = new(
            surface,
            new ResolvedMaterial(
                MaterialType.Standard,
                TexturePayload: null,
                TextureSourceKind.Dataset,
                MaterialProjection.Uv,
                Family: null,
                TextureScale: null,
                MaterialReuseScope.PerObject,
                TerrainOverlay: overlay),
            DepthOffset: null);

        (Float2? textureScale, Float2? textureOffset) = DemTerrainOverlayAssignment.TryCreateTerrainGridTextureTransform(
            cityObject,
            material,
            overlay);

        double overlayWest = WebMercatorTileMath.LongitudeToNormalizedX(overlay.GeographicBounds.MinLongitude);
        double overlayEast = WebMercatorTileMath.LongitudeToNormalizedX(overlay.GeographicBounds.MaxLongitude);
        double overlayNorth = WebMercatorTileMath.LatitudeToNormalizedY(overlay.GeographicBounds.MaxLatitude);
        double overlaySouth = WebMercatorTileMath.LatitudeToNormalizedY(overlay.GeographicBounds.MinLatitude);
        double objectWest = WebMercatorTileMath.LongitudeToNormalizedX(objectBounds.MinLongitude);
        double objectEast = WebMercatorTileMath.LongitudeToNormalizedX(objectBounds.MaxLongitude);
        double objectNorth = WebMercatorTileMath.LatitudeToNormalizedY(objectBounds.MaxLatitude);
        double objectSouth = WebMercatorTileMath.LatitudeToNormalizedY(objectBounds.MinLatitude);
        double expectedScaleX = (objectEast - objectWest) / (overlayEast - overlayWest);
        double expectedScaleY = (objectSouth - objectNorth) / (overlaySouth - overlayNorth);
        double expectedOffsetX = (objectWest - overlayWest) / (overlayEast - overlayWest);
        double expectedOffsetY = (overlaySouth - objectSouth) / (overlaySouth - overlayNorth);

        Assert.True(textureScale is not null);
        Assert.True(textureOffset is not null);
        Assert.Equal(expectedScaleX, textureScale.X, 9);
        Assert.Equal(expectedScaleY, textureScale.Y, 9);
        Assert.Equal(expectedOffsetX, textureOffset.X, 9);
        Assert.Equal(expectedOffsetY, textureOffset.Y, 9);
    }

    private static ParsedCityObject CreateCityObject(
        ParsedSurface surface)
    {
        return new ParsedCityObject(
            SlotKey: "dem-object",
            DisplayName: "DEM Object",
            PackageName: "dem",
            ActualMeshCode: "53394525",
            LodLevel: 1,
            Surfaces: [surface],
            ReferenceSystem: CoordinateReferenceSystem.Parse("EPSG:4326"),
            SourceFileRelativePath: "udx/dem/53394525/sample.gml",
            SharedAcrossMeshCodes: false);
    }

    private static ParsedSurface CreateGeneratedSurface(
        string polygonId,
        GeodeticPoint[] vertices)
    {
        return new ParsedSurface(
            PolygonId: polygonId,
            Semantic: ParsedSurfaceSemantic.Ground,
            ExteriorRing: new ParsedRing($"{polygonId}-ring", vertices, UVs: null),
            InteriorRings: [],
            BaseColor: new ColorRgba(1.0, 1.0, 1.0, 1.0),
            TexturePayload: null,
            UsesGeneratedDemTexture: true);
    }

    private static TerrainTextureOverlay CreateOverlay(double westLongitude, double eastLongitude)
    {
        return new TerrainTextureOverlay(
            PackageName: "dem",
            GeographicBounds: new GeographicRectangle(35.0000, 35.0200, westLongitude, eastLongitude),
            MaxTextureSize: DemTerrainTextureDefaults.MaxTextureSize,
            Sources: [new TerrainTextureTileSource("https://tiles.example/{z}/{x}/{y}.png", 18)]);
    }

    private static GeographicRectangle GetSurfaceBounds(ParsedSurface surface)
    {
        return new GeographicRectangle(
            MinLatitude: surface.ExteriorRing.Vertices.Min(static point => point.Latitude),
            MaxLatitude: surface.ExteriorRing.Vertices.Max(static point => point.Latitude),
            MinLongitude: surface.ExteriorRing.Vertices.Min(static point => point.Longitude),
            MaxLongitude: surface.ExteriorRing.Vertices.Max(static point => point.Longitude));
    }
}
