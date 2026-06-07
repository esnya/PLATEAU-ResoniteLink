using System.Linq;

using PlateauResoniteLink.Application.Importing;

namespace PlateauResoniteLink.Tests.Profiles;

public sealed class DemCityObjectAggregationTests
{
    [Fact]
    public void AggregateBySourceFileAndThirdMeshMergesDemObjectsDeterministically()
    {
        CoordinateReferenceSystem referenceSystem = CoordinateReferenceSystem.Parse("EPSG:4326");
        SourceFileDescriptor sourceFile = new(
            "udx/dem/503033/plateau_fukuoka_dem_503033.gml",
            "dem",
            "503033",
            RequiresMeshCodeBoundsFilter: true);
        ParsedCityObject[] cityObjects =
        [
            CreateCityObject("dem-b", "polygon-b", "50303312", sourceFile.RelativePath, referenceSystem),
            CreateCityObject("dem-a", "polygon-a", "50303312", sourceFile.RelativePath, referenceSystem),
        ];

        ParsedCityObject result = Assert.Single(
            DemCityObjectAggregation.AggregateBySourceFileAndThirdMesh(sourceFile, cityObjects));

        Assert.Equal("dem_plateau_fukuoka_dem_503033_50303312", result.SlotKey);
        Assert.Equal("DEM 50303312", result.DisplayName);
        Assert.Equal("50303312", result.ActualMeshCode);
        Assert.Equal(sourceFile.RelativePath, result.SourceFileRelativePath);
        Assert.Equal(
            result.Surfaces,
            result.Surfaces.OrderBy(static surface => surface, ParsedSurfaceStructuralComparer.Instance).ToArray());
    }

    [Fact]
    public void AggregateBySourceFileAndThirdMeshKeepsDifferentThirdMeshCodesSeparate()
    {
        CoordinateReferenceSystem referenceSystem = CoordinateReferenceSystem.Parse("EPSG:4326");
        SourceFileDescriptor sourceFile = new(
            "udx/dem/503033/plateau_fukuoka_dem_503033.gml",
            "dem",
            "503033",
            RequiresMeshCodeBoundsFilter: true);
        ParsedCityObject[] cityObjects =
        [
            CreateCityObject("dem-2", "polygon-2", "50303313", sourceFile.RelativePath, referenceSystem),
            CreateCityObject("dem-1", "polygon-1", "50303312", sourceFile.RelativePath, referenceSystem),
        ];

        ParsedCityObject[] results =
            DemCityObjectAggregation.AggregateBySourceFileAndThirdMesh(sourceFile, cityObjects);

        Assert.Equal(["50303312", "50303313"], results.Select(static result => result.ActualMeshCode).ToArray());
        Assert.All(results, static result => Assert.Single(result.Surfaces));
    }

    [Fact]
    public void AggregateBySourceFileAndThirdMeshUsesSelectedThirdMeshForParentDemSourceFile()
    {
        CoordinateReferenceSystem referenceSystem = CoordinateReferenceSystem.Parse("EPSG:4326");
        SourceFileDescriptor sourceFile = new(
            "udx/dem/503033/plateau_fukuoka_dem_503033.gml",
            "dem",
            "503033",
            RequiresMeshCodeBoundsFilter: true);
        ParsedCityObject[] cityObjects =
        [
            CreateCityObject("dem-parent", "polygon-parent", "503033", sourceFile.RelativePath, referenceSystem),
        ];

        ParsedCityObject result = Assert.Single(
            DemCityObjectAggregation.AggregateBySourceFileAndThirdMesh(
                sourceFile,
                cityObjects,
                ["50303312"]));

        Assert.Equal("dem_plateau_fukuoka_dem_503033_50303312", result.SlotKey);
        Assert.Equal("DEM 50303312", result.DisplayName);
        Assert.Equal("50303312", result.ActualMeshCode);
    }

    private static ParsedCityObject CreateCityObject(
        string slotKey,
        string polygonId,
        string actualMeshCode,
        string sourceFileRelativePath,
        CoordinateReferenceSystem referenceSystem)
    {
        ParsedRing exteriorRing = new(
            [
                new GeodeticPoint(35.0, 139.0, 0.0),
                new GeodeticPoint(35.0, 139.001, 0.0),
                new GeodeticPoint(35.001, 139.001, 1.0),
            ],
            UVs: null);
        ParsedSurface surface = new(
            ParsedSurfaceSemantic.Ground,
            exteriorRing,
            [],
            new ColorRgba(1.0, 1.0, 1.0, 1.0),
            TexturePayload: null);
        return new ParsedCityObject(
            slotKey,
            slotKey,
            "dem",
            actualMeshCode,
            LodLevel: 1,
            Surfaces: [surface],
            referenceSystem,
            sourceFileRelativePath,
            SharedAcrossMeshCodes: true,
            BuildingAttributes: BuildingAttributeContext.Empty);
    }
}
