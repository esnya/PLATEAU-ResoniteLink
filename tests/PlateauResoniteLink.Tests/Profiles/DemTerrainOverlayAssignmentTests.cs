using PlateauResoniteLink.Application.Importing.Contracts;
using PlateauResoniteLink.Application.Importing.Plateau;
using PlateauResoniteLink.Application.Importing.Source;

using System;
using System.Linq;

using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Tests.Profiles;

public sealed class DemTerrainOverlayAssignmentTests
{
    [Fact]
    public void SplitParsedCityObjectKeepsOneDemObjectAcrossOverlayBoundary()
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

        (ParsedCityObject splitCityObject, TerrainTextureOverlay? overlay) = Assert.Single(results);
        Assert.NotNull(overlay);
        Assert.Equal(cityObject.SlotKey, splitCityObject.SlotKey);
        Assert.Equal(cityObject.DisplayName, splitCityObject.DisplayName);
        GeographicRectangle bounds = GetSurfaceBounds(Assert.Single(splitCityObject.Surfaces));
        Assert.Equal(139.0000, bounds.MinLongitude, 6);
        Assert.Equal(139.0120, bounds.MaxLongitude, 6);
    }

    [Fact]
    public void SplitParsedCityObjectKeepsDemWithinActualThirdMesh()
    {
        ThirdRegionalMeshCode meshCode = ThirdRegionalMeshCode.Parse("53394525");
        JisRegionalMeshBounds bounds = meshCode.Bounds;
        double overrunLongitude = (bounds.EastLongitude - bounds.WestLongitude) * 0.10;
        ParsedSurface surface = CreateGeneratedSurface(
            "dem-crosses-third-mesh-boundary",
            [
                new GeodeticPoint(bounds.SouthLatitude, bounds.WestLongitude, 0.0),
                new GeodeticPoint(bounds.NorthLatitude, bounds.WestLongitude, 1.0),
                new GeodeticPoint(bounds.NorthLatitude, bounds.EastLongitude + overrunLongitude, 2.0),
            ]);
        ParsedCityObject cityObject = CreateCityObject(surface) with
        {
            ActualMeshCode = meshCode.Value,
            SharedAcrossMeshCodes = true,
        };
        TerrainTextureOverlay[] overlays =
        [
            CreateOverlay(meshCode, new GeographicRectangle(
                bounds.SouthLatitude,
                bounds.NorthLatitude,
                bounds.WestLongitude,
                bounds.EastLongitude)),
            CreateOverlay(ThirdRegionalMeshCode.Parse("53394526"), new GeographicRectangle(
                bounds.SouthLatitude,
                bounds.NorthLatitude,
                bounds.EastLongitude,
                bounds.EastLongitude + overrunLongitude)),
        ];

        (ParsedCityObject CityObject, TerrainTextureOverlay? Overlay)[] results =
            DemTerrainOverlayAssignment.SplitParsedCityObject(cityObject, overlays).ToArray();

        (ParsedCityObject splitCityObject, TerrainTextureOverlay? overlay) = Assert.Single(results);
        Assert.NotNull(overlay);
        Assert.Equal(meshCode, overlay.MeshCode);
        Assert.Equal(cityObject.SlotKey, splitCityObject.SlotKey);
        Assert.Equal(cityObject.DisplayName, splitCityObject.DisplayName);
        Assert.Equal(meshCode.Value, splitCityObject.ActualMeshCode);
        Assert.All(
            splitCityObject.Surfaces,
            clippedSurface => Assert.True(GetSurfaceBounds(clippedSurface).MaxLongitude <= bounds.EastLongitude + 1e-9));
    }

    [Fact]
    public void SplitParsedCityObjectKeepsOneDemObjectForMultipleGeneratedSurfacesAcrossOverlays()
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
            "dem-mixed-compact",
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

        (ParsedCityObject splitCityObject, TerrainTextureOverlay? overlay) = Assert.Single(results);
        Assert.NotNull(overlay);
        Assert.Equal(cityObject.SlotKey, splitCityObject.SlotKey);
        Assert.Equal(cityObject.DisplayName, splitCityObject.DisplayName);
        Assert.Equal(2, splitCityObject.Surfaces.Length);
        Assert.Contains(splitCityObject.Surfaces, surface => GetSurfaceBounds(surface).MaxLongitude <= boundaryLongitude + 1e-9);
        Assert.Contains(splitCityObject.Surfaces, surface => GetSurfaceBounds(surface).MinLongitude > boundaryLongitude + 0.00010);
    }

    [Fact]
    public void SplitParsedCityObjectKeepsDemOverlayWhenTextureSourcesAreEmpty()
    {
        ParsedSurface surface = CreateGeneratedSurface(
            "dem-source-less-overlay",
            [
                new GeodeticPoint(35.0000, 139.0000, 0.0),
                new GeodeticPoint(35.0100, 139.0000, 1.0),
                new GeodeticPoint(35.0100, 139.0100, 2.0),
            ]);
        ParsedCityObject cityObject = CreateCityObject(surface);
        TerrainTextureOverlay overlay = new(
            PackageName: "dem",
            MeshCode: ThirdRegionalMeshCode.Parse("53394525"),
            GeographicBounds: new GeographicRectangle(35.0000, 35.0200, 139.0000, 139.0200),
            MaxTextureSize: DemTerrainTextureDefaults.MaxTextureSize,
            Sources: []);

        (ParsedCityObject splitCityObject, TerrainTextureOverlay? assignedOverlay) =
            Assert.Single(DemTerrainOverlayAssignment.SplitParsedCityObject(cityObject, [overlay]));

        Assert.Same(overlay, assignedOverlay);
        Assert.Equal(cityObject.SlotKey, splitCityObject.SlotKey);
        Assert.Equal(cityObject.DisplayName, splitCityObject.DisplayName);
        Assert.Single(splitCityObject.Surfaces);
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
    public void HasOverlayCoverageReturnsTrueWhenRequestedMeshExcludesGeneratedSurface()
    {
        ParsedCityObject cityObject = CreateCityObject(
            CreateGeneratedSurface(
                "dem-outside-request",
                [
                    new GeodeticPoint(35.0000, 139.0000, 0.0),
                    new GeodeticPoint(35.0100, 139.0000, 1.0),
                    new GeodeticPoint(35.0100, 139.0200, 2.0),
                ]));
        TerrainTextureOverlay[] overlays =
        [
            CreateOverlay(139.0000, 139.0200),
        ];
        MeshCodeBounds[] requestedMeshCodeBounds =
        [
            new(35.0000, 35.0200, 139.0300, 139.0400),
        ];

        bool hasCoverage = DemTerrainOverlayAssignment.HasOverlayCoverage(
            cityObject,
            overlays,
            requestedMeshCodeBounds);

        Assert.True(hasCoverage);
    }

    [Fact]
    public void SplitParsedCityObjectSkipsGeneratedSurfaceWhenRequestedMeshExcludesIt()
    {
        ParsedSurface surface = CreateGeneratedSurface(
            "dem-outside-request",
            [
                new GeodeticPoint(35.0000, 139.0000, 0.0),
                new GeodeticPoint(35.0100, 139.0000, 1.0),
                new GeodeticPoint(35.0100, 139.0200, 2.0),
            ]);
        ParsedCityObject cityObject = CreateCityObject(surface);
        TerrainTextureOverlay[] overlays =
        [
            CreateOverlay(139.0000, 139.0200),
        ];
        MeshCodeBounds[] requestedMeshCodeBounds =
        [
            new(35.0000, 35.0200, 139.0300, 139.0400),
        ];

        (ParsedCityObject CityObject, TerrainTextureOverlay? Overlay)[] results =
            DemTerrainOverlayAssignment.SplitParsedCityObject(
                cityObject,
                overlays,
                requestedMeshCodeBounds).ToArray();

        Assert.Empty(results);
    }

    [Fact]
    public void SplitParsedCityObjectSkipsSharedDemWhenRequestedMeshExcludesActualThirdMesh()
    {
        JisRegionalMeshBounds meshBounds = ThirdRegionalMeshCode.Parse("53394525").Bounds;
        ParsedSurface surface = CreateGeneratedSurface(
            "dem-shared-outside-request",
            [
                new GeodeticPoint(meshBounds.SouthLatitude, meshBounds.WestLongitude, 0.0),
                new GeodeticPoint(meshBounds.NorthLatitude, meshBounds.WestLongitude, 1.0),
                new GeodeticPoint(meshBounds.NorthLatitude, meshBounds.EastLongitude, 2.0),
            ]);
        ParsedCityObject cityObject = CreateCityObject(surface) with
        {
            SharedAcrossMeshCodes = true,
        };
        JisRegionalMeshBounds requestedMeshBounds = ThirdRegionalMeshCode.Parse("53394526").Bounds;
        MeshCodeBounds[] requestedMeshCodeBounds =
        [
            new(
                requestedMeshBounds.SouthLatitude,
                requestedMeshBounds.NorthLatitude,
                requestedMeshBounds.WestLongitude,
                requestedMeshBounds.EastLongitude),
        ];

        (ParsedCityObject CityObject, TerrainTextureOverlay? Overlay)[] results =
            DemTerrainOverlayAssignment.SplitParsedCityObject(cityObject, [], requestedMeshCodeBounds).ToArray();

        Assert.Empty(results);
    }

    [Fact]
    public void SplitParsedCityObjectClipsSharedDemToRequestedMeshEvenWhenNoOverlaysExist()
    {
        JisRegionalMeshBounds meshBounds = ThirdRegionalMeshCode.Parse("53394525").Bounds;
        double midpointLongitude = (meshBounds.WestLongitude + meshBounds.EastLongitude) / 2.0;
        ParsedSurface surface = CreateGeneratedSurface(
            "dem-parent",
            [
                new GeodeticPoint(meshBounds.SouthLatitude, meshBounds.WestLongitude, 0.0),
                new GeodeticPoint(meshBounds.NorthLatitude, meshBounds.WestLongitude, 1.0),
                new GeodeticPoint(meshBounds.NorthLatitude, meshBounds.EastLongitude, 2.0),
            ]);
        ParsedCityObject cityObject = CreateCityObject(surface) with
        {
            SharedAcrossMeshCodes = true,
        };
        MeshCodeBounds[] requestedMeshCodeBounds =
        [
            new(meshBounds.SouthLatitude, meshBounds.NorthLatitude, meshBounds.WestLongitude, midpointLongitude),
        ];

        (ParsedCityObject CityObject, TerrainTextureOverlay? Overlay)[] results =
            DemTerrainOverlayAssignment.SplitParsedCityObject(cityObject, [], requestedMeshCodeBounds).ToArray();

        (ParsedCityObject splitCityObject, TerrainTextureOverlay? overlay) = Assert.Single(results);
        Assert.Null(overlay);
        GeographicRectangle bounds = GetSurfaceBounds(Assert.Single(splitCityObject.Surfaces));
        Assert.True(bounds.MaxLongitude <= midpointLongitude + 1e-9);
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
            MeshCode: ThirdRegionalMeshCode.Parse("53394525"),
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
            depthOffset: null);

        (Float2? textureScale, Float2? textureOffset) = DemTerrainOverlayUvMapper.TryCreateTerrainGridTextureTransform(
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
            SharedAcrossMeshCodes: false,
            BuildingAttributes: BuildingAttributeContext.Empty);
    }

    private static ParsedSurface CreateGeneratedSurface(
        string polygonId,
        GeodeticPoint[] vertices)
    {
        return new ParsedSurface(
            Semantic: ParsedSurfaceSemantic.Ground,
            ExteriorRing: new ParsedRing(vertices, UVs: null),
            InteriorRings: [],
            BaseColor: new ColorRgba(1.0, 1.0, 1.0, 1.0),
            TexturePayload: null);
    }

    private static TerrainTextureOverlay CreateOverlay(double westLongitude, double eastLongitude)
    {
        return new TerrainTextureOverlay(
            PackageName: "dem",
            MeshCode: ThirdRegionalMeshCode.Parse("53394525"),
            GeographicBounds: new GeographicRectangle(35.0000, 35.0200, westLongitude, eastLongitude),
            MaxTextureSize: DemTerrainTextureDefaults.MaxTextureSize,
            Sources: [new TerrainTextureTileSource("https://tiles.example/{z}/{x}/{y}.png", 18)]);
    }

    private static TerrainTextureOverlay CreateOverlay(
        ThirdRegionalMeshCode meshCode,
        GeographicRectangle geographicBounds)
    {
        return new TerrainTextureOverlay(
            PackageName: "dem",
            MeshCode: meshCode,
            GeographicBounds: geographicBounds,
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
