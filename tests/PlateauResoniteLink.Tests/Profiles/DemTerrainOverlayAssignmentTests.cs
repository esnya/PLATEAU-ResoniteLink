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
        Assert.True(Assert.Single(splitCityObject.Surfaces).UsesGeneratedDemTexture);
        GeographicRectangle bounds = GetSurfaceBounds(Assert.Single(splitCityObject.Surfaces));
        Assert.Equal(139.0000, bounds.MinLongitude, 6);
        Assert.Equal(boundaryLongitude, bounds.MaxLongitude, 6);
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
    public void HasOverlayCoverageReturnsTrueForBootstrapParsedCityObjectWhenSplitCoverageExists()
    {
        const double boundaryLongitude = 139.0100;
        BootstrapParsedCityObject cityObject = BootstrapParsedCityObject.FromLegacy(CreateCityObject(
            CreateGeneratedSurface(
                "dem-wide-split",
                [
                    new LocalCityGmlObjectProjection.GeodeticPoint(35.0000, 139.0000, 0.0),
                    new LocalCityGmlObjectProjection.GeodeticPoint(35.0100, 139.0000, 1.0),
                    new LocalCityGmlObjectProjection.GeodeticPoint(35.0100, 139.0120, 2.0),
                ])));
        TerrainTextureOverlay[] overlays =
        [
            CreateOverlay(139.0000, boundaryLongitude),
            CreateOverlay(boundaryLongitude, 139.0200),
        ];

        bool hasCoverage = DemTerrainOverlayAssignment.HasOverlayCoverage(cityObject, overlays);

        Assert.True(hasCoverage);
    }

    [Fact]
    public void HasOverlayCoverageReturnsFalseForBootstrapParsedCityObjectWhenSurfaceMissesOverlayBounds()
    {
        BootstrapParsedCityObject cityObject = BootstrapParsedCityObject.FromLegacy(CreateCityObject(
            CreateGeneratedSurface(
                "dem-nearest-overlay",
                [
                    new LocalCityGmlObjectProjection.GeodeticPoint(35.0000, 139.0200002, 0.0),
                    new LocalCityGmlObjectProjection.GeodeticPoint(35.0100, 139.0200002, 1.0),
                    new LocalCityGmlObjectProjection.GeodeticPoint(35.0100, 139.0200006, 2.0),
                ])));
        TerrainTextureOverlay[] overlays =
        [
            CreateOverlay(139.0000, 139.0100),
            CreateOverlay(139.0100, 139.0200),
        ];

        bool hasCoverage = DemTerrainOverlayAssignment.HasOverlayCoverage(cityObject, overlays);

        Assert.False(hasCoverage);
    }

    [Fact]
    public void HasOverlayCoverageReturnsFalseForBootstrapParsedCityObjectWhenSurfaceHasNoVertices()
    {
        BootstrapParsedCityObject cityObject = BootstrapParsedCityObject.FromLegacy(CreateCityObject(
            CreateGeneratedSurface("dem-empty-surface", [])));
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
        Assert.True(bounds.MaxLongitude <= 139.0100 + 1e-9);
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
            ExteriorRing: new LocalCityGmlObjectProjection.ParsedRing($"{polygonId}-ring", vertices, UVs: null),
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
            MaxTextureSize: LocalCityGmlObjectProjection.DefaultDemTerrainTextureMaxSize,
            Sources: [new TerrainTextureTileSource("https://tiles.example/{z}/{x}/{y}.png", 18)]);
    }

    private static GeographicRectangle GetSurfaceBounds(LocalCityGmlObjectProjection.ParsedSurface surface)
    {
        return new GeographicRectangle(
            MinLatitude: surface.ExteriorRing.Vertices.Min(static point => point.Latitude),
            MaxLatitude: surface.ExteriorRing.Vertices.Max(static point => point.Latitude),
            MinLongitude: surface.ExteriorRing.Vertices.Min(static point => point.Longitude),
            MaxLongitude: surface.ExteriorRing.Vertices.Max(static point => point.Longitude));
    }
}
