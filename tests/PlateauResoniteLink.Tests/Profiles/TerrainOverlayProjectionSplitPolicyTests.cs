using System.Linq;

using PlateauResoniteLink.Application.Importing;
using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Tests.Profiles;

public sealed class TerrainOverlayProjectionSplitPolicyTests
{
    [Fact]
    public void SplitParsedCityObjectExcludesGeneratedNoWallSlabPartsFromTerrainRoofProjection()
    {
        MeshCodeBounds meshBounds = MeshCodeBounds.TryParse("53394525")!;
        ParsedSurface top = CreateHorizontalSurface("roof", altitude: 10.0, meshBounds: meshBounds);
        ParsedSurface bottom = CreateHorizontalSurface("roof_generated_no-wall-bottom", altitude: 9.7, meshBounds: meshBounds);
        ParsedSurface side = CreateVerticalSurface("roof_generated_no-wall-side-0", meshBounds);
        ParsedCityObject cityObject = new(
            SlotKey: "bldg-no-wall",
            DisplayName: "No-wall building",
            PackageName: "bldg",
            ActualMeshCode: "53394525",
            LodLevel: 2,
            Surfaces: [top, bottom, side],
            ReferenceSystem: CoordinateReferenceSystem.Parse("EPSG:4326"),
            SourceFileRelativePath: "udx/bldg/53394525/bldg.gml",
            SharedAcrossMeshCodes: false,
            BuildingAttributes: BuildingAttributeContext.Empty with { CityGmlClassCodes = ["3003"] });
        TerrainTextureOverlay overlay = CreateOverlay(meshBounds);
        MeshCodeBounds[] requestedMeshCodeBounds =
        [
            meshBounds,
        ];

        (ParsedCityObject CityObject, TerrainTextureOverlay? Overlay)[] results =
            TerrainOverlayProjectionSplitPolicy.SplitParsedCityObject(
                cityObject,
                [overlay],
                requestedMeshCodeBounds)
            .ToArray();
        ParsedCityObject[] regeneratedSplitObjects = results
            .Select(static result => GeneratedLod1RoofCityObjectFactory.Create(result.CityObject))
            .ToArray();

        ParsedSurface[] generatedTerrainSurfaces = regeneratedSplitObjects
            .SelectMany(static cityObject => cityObject.Surfaces)
            .Where(static surface => surface.UsesGeneratedDemTexture)
            .ToArray();
        ParsedSurface generatedTop = Assert.Single(generatedTerrainSurfaces);
        Assert.Equal("roof", generatedTop.PolygonId);

        ParsedSurface[] noWallSlabParts = regeneratedSplitObjects
            .SelectMany(static cityObject => cityObject.Surfaces)
            .Where(static surface => surface.PolygonId.Contains("_generated_no-wall-", System.StringComparison.Ordinal))
            .ToArray();
        Assert.Equal(2, noWallSlabParts.Length);
        Assert.All(noWallSlabParts, static surface => Assert.False(surface.UsesGeneratedDemTexture));
    }

    private static ParsedSurface CreateHorizontalSurface(string polygonId, double altitude, MeshCodeBounds meshBounds)
    {
        return new ParsedSurface(
            polygonId,
            ParsedSurfaceSemantic.Roof,
            new ParsedRing(
                $"{polygonId}-ring",
                [
                    new GeodeticPoint(meshBounds.SouthLatitude, meshBounds.WestLongitude, altitude),
                    new GeodeticPoint(meshBounds.SouthLatitude, meshBounds.EastLongitude, altitude),
                    new GeodeticPoint(meshBounds.NorthLatitude, meshBounds.EastLongitude, altitude),
                    new GeodeticPoint(meshBounds.NorthLatitude, meshBounds.WestLongitude, altitude),
                    new GeodeticPoint(meshBounds.SouthLatitude, meshBounds.WestLongitude, altitude),
                ],
                UVs: null),
            InteriorRings: [],
            BaseColor: new ColorRgba(1.0, 1.0, 1.0, 1.0),
            TexturePayload: null);
    }

    private static ParsedSurface CreateVerticalSurface(string polygonId, MeshCodeBounds meshBounds)
    {
        return new ParsedSurface(
            polygonId,
            ParsedSurfaceSemantic.Roof,
            new ParsedRing(
                $"{polygonId}-ring",
                [
                    new GeodeticPoint(meshBounds.SouthLatitude, meshBounds.WestLongitude, 10.0),
                    new GeodeticPoint(meshBounds.SouthLatitude, meshBounds.WestLongitude, 9.7),
                    new GeodeticPoint(meshBounds.SouthLatitude, meshBounds.EastLongitude, 9.7),
                    new GeodeticPoint(meshBounds.SouthLatitude, meshBounds.EastLongitude, 10.0),
                    new GeodeticPoint(meshBounds.SouthLatitude, meshBounds.WestLongitude, 10.0),
                ],
                UVs: null),
            InteriorRings: [],
            BaseColor: new ColorRgba(1.0, 1.0, 1.0, 1.0),
            TexturePayload: null);
    }

    private static TerrainTextureOverlay CreateOverlay(MeshCodeBounds meshBounds)
    {
        return new TerrainTextureOverlay(
            PackageName: "dem",
            GeographicBounds: new GeographicRectangle(
                meshBounds.SouthLatitude,
                meshBounds.NorthLatitude,
                meshBounds.WestLongitude,
                meshBounds.EastLongitude),
            MaxTextureSize: DemTerrainTextureDefaults.MaxTextureSize,
            Sources: [new TerrainTextureTileSource("https://tiles.example/{z}/{x}/{y}.png", 18)]);
    }
}
