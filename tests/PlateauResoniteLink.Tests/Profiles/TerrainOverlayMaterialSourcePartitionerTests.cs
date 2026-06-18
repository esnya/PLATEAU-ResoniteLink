using PlateauResoniteLink.Application.Importing;
using PlateauResoniteLink.Application.Importing.Contracts;
using PlateauResoniteLink.Application.Importing.Plateau;
using PlateauResoniteLink.Application.Importing.Source;

using System.Linq;

using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Tests.Profiles;

public sealed class TerrainOverlayMaterialSourcePartitionerTests
{
    [Fact]
    public void PartitionParsedCityObjectAssignsTerrainOverlayMaterialSourceToGeneratedNoWallSlabParts()
    {
        MeshCodeBounds meshBounds = MeshCodeBounds.TryParse("53394525")!;
        ParsedSurface top = CreateHorizontalSurface("roof", altitude: 10.0, meshBounds: meshBounds);
        ParsedSurface bottom = CreateHorizontalSurface("roof_roof-slab-bottom", altitude: 9.7, meshBounds: meshBounds);
        ParsedSurface side = CreateVerticalSurface("roof_roof-slab-side-0", meshBounds);
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

        ConstructionCityObjectDraft draft = new(
            cityObject,
            [
                new ConstructionFace(top, ConstructionFaceRole.RoofSlab),
                new ConstructionFace(bottom, ConstructionFaceRole.RoofSlab),
                new ConstructionFace(side, ConstructionFaceRole.RoofSlab),
            ]);

        (ConstructionCityObjectDraft CityObject, TerrainTextureOverlay? Overlay)[] results =
            TerrainOverlayMaterialSourcePartitioner.PartitionConstructionCityObject(
                draft,
                [overlay],
                requestedMeshCodeBounds)
            .ToArray();

        ConstructionFace[] terrainMaterialFaces = results
            .SelectMany(static result => result.CityObject.Faces)
            .Where(static face => face.MaterialTreatment == SurfaceMaterialTreatment.TerrainOverlayMaterialSource)
            .ToArray();
        Assert.Equal(3, terrainMaterialFaces.Length);
        Assert.Contains(
            terrainMaterialFaces,
            face => face.Surface.Semantic == top.Semantic
                && face.Surface.ExteriorRing.Vertices.SequenceEqual(top.ExteriorRing.Vertices));

        ConstructionFace[] roofSlabFaces = results
            .SelectMany(static result => result.CityObject.Faces)
            .Where(static face => face.Role == ConstructionFaceRole.RoofSlab)
            .ToArray();
        Assert.Equal(3, roofSlabFaces.Length);
        Assert.All(roofSlabFaces, static face =>
        {
            Assert.Equal(SurfaceMaterialTreatment.TerrainOverlayMaterialSource, face.MaterialTreatment);
            Assert.Equal(ConstructionFaceRole.RoofSlab, face.Role);
        });
        ConstructionFace noWallSide = Assert.Single(roofSlabFaces, static face => face.Surface.Semantic == ParsedSurfaceSemantic.Wall);
        Assert.Equal(ParsedSurfaceSemantic.Wall, noWallSide.Surface.Semantic);
    }

    [Fact]
    public void PartitionParsedCityObjectRejectsInvalidTerrainMaterialSourceMeshCodeBeforeOverlayFallback()
    {
        MeshCodeBounds meshBounds = MeshCodeBounds.TryParse("53394525")!;
        ParsedCityObject cityObject = new(
            SlotKey: "bldg-invalid-mesh",
            DisplayName: "Invalid mesh building",
            PackageName: "bldg",
            ActualMeshCode: "not-a-mesh",
            LodLevel: 2,
            Surfaces: [CreateHorizontalSurface("roof", altitude: 10.0, meshBounds: meshBounds)],
            ReferenceSystem: CoordinateReferenceSystem.Parse("EPSG:4326"),
            SourceFileRelativePath: "udx/bldg/not-a-mesh/bldg.gml",
            SharedAcrossMeshCodes: false,
            BuildingAttributes: BuildingAttributeContext.Empty with { CityGmlClassCodes = ["3003"] });

        PlateauImportValidationException exception = Assert.Throws<PlateauImportValidationException>(
            () => TerrainOverlayMaterialSourcePartitioner.PartitionParsedCityObject(
                    cityObject,
                    [CreateOverlay(meshBounds)],
                    [meshBounds])
                .ToArray());
        string error = Assert.Single(exception.Errors);
        Assert.Contains("must be a valid second- or third-level mesh-code", error);
    }

    private static ParsedSurface CreateHorizontalSurface(string polygonId, double altitude, MeshCodeBounds meshBounds)
    {
        return new ParsedSurface(ParsedSurfaceSemantic.Roof,
            new ParsedRing([
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
        return new ParsedSurface(ParsedSurfaceSemantic.Wall,
            new ParsedRing([
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
            MeshCode: ThirdRegionalMeshCode.Parse("53394525"),
            GeographicBounds: new GeographicRectangle(
                meshBounds.SouthLatitude,
                meshBounds.NorthLatitude,
                meshBounds.WestLongitude,
                meshBounds.EastLongitude),
            MaxTextureSize: DemTerrainTextureDefaults.MaxTextureSize,
            Sources: [new TerrainTextureTileSource("https://tiles.example/{z}/{x}/{y}.png", 18)]);
    }
}
