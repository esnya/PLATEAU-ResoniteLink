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
            RequiresMeshAreaFilter: true);
        BootstrapParsedCityObject[] cityObjects =
        [
            CreateCityObject("dem-b", "polygon-b", "50303312", sourceFile.RelativePath, referenceSystem),
            CreateCityObject("dem-a", "polygon-a", "50303312", sourceFile.RelativePath, referenceSystem),
        ];

        BootstrapParsedCityObject result = Assert.Single(
            DemCityObjectAggregation.AggregateBySourceFileAndThirdMesh(sourceFile, cityObjects));

        Assert.Equal("dem_plateau_fukuoka_dem_503033_50303312", result.SlotKey);
        Assert.Equal("DEM 50303312", result.DisplayName);
        Assert.Equal("50303312", result.ActualMeshCode);
        Assert.Equal(sourceFile.RelativePath, result.SourceFileRelativePath);
        Assert.Equal(["polygon-a", "polygon-b"], result.Surfaces.Select(static surface => surface.PolygonId).ToArray());
    }

    [Fact]
    public void AggregateBySourceFileAndThirdMeshKeepsDifferentThirdMeshCodesSeparate()
    {
        CoordinateReferenceSystem referenceSystem = CoordinateReferenceSystem.Parse("EPSG:4326");
        SourceFileDescriptor sourceFile = new(
            "udx/dem/503033/plateau_fukuoka_dem_503033.gml",
            "dem",
            "503033",
            RequiresMeshAreaFilter: true);
        BootstrapParsedCityObject[] cityObjects =
        [
            CreateCityObject("dem-2", "polygon-2", "50303313", sourceFile.RelativePath, referenceSystem),
            CreateCityObject("dem-1", "polygon-1", "50303312", sourceFile.RelativePath, referenceSystem),
        ];

        BootstrapParsedCityObject[] results =
            DemCityObjectAggregation.AggregateBySourceFileAndThirdMesh(sourceFile, cityObjects);

        Assert.Equal(["50303312", "50303313"], results.Select(static result => result.ActualMeshCode).ToArray());
        Assert.All(results, static result => Assert.Single(result.Surfaces));
    }

    private static BootstrapParsedCityObject CreateCityObject(
        string slotKey,
        string polygonId,
        string actualMeshCode,
        string sourceFileRelativePath,
        CoordinateReferenceSystem referenceSystem)
    {
        BootstrapParsedRing exteriorRing = new(
            $"{polygonId}-ring",
            [
                new GeodeticPoint(35.0, 139.0, 0.0),
                new GeodeticPoint(35.0, 139.001, 0.0),
                new GeodeticPoint(35.001, 139.001, 1.0),
            ],
            UVs: null);
        BootstrapParsedSurface surface = new(
            polygonId,
            BootstrapParsedSurfaceSemantic.Ground,
            exteriorRing,
            [],
            new ColorRgba(1.0, 1.0, 1.0, 1.0),
            TexturePayload: null,
            UsesGeneratedDemTexture: true);
        return new BootstrapParsedCityObject(
            slotKey,
            slotKey,
            "dem",
            actualMeshCode,
            LodLevel: 1,
            Surfaces: [surface],
            referenceSystem,
            sourceFileRelativePath,
            SharedAcrossMeshCodes: true);
    }
}
