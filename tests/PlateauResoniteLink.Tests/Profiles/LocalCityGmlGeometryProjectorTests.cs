using PlateauResoniteLink.Core.Application.Importing;
using PlateauResoniteLink.Core.Application.Importing.Contracts;
using PlateauResoniteLink.Plateau.Application.Importing.Plateau;
using PlateauResoniteLink.Plateau.Application.Importing.Source;

using System;
using System.Linq;

using GeographicLib;

using PlateauResoniteLink.Core.Domain.Importing;

namespace PlateauResoniteLink.Tests.Profiles;

public sealed class LocalCityGmlGeometryProjectorTests
{
    [Fact]
    public void CoordinateReferenceSystemParseTreatsWgs84IdentifiersAsCompatible()
    {
        CoordinateReferenceSystem epsg4326 = CoordinateReferenceSystem.Parse("EPSG:4326");
        CoordinateReferenceSystem uri4326 = CoordinateReferenceSystem.Parse("http://www.opengis.net/def/crs/EPSG/0/4326");
        CoordinateReferenceSystem epsg4979 = CoordinateReferenceSystem.Parse("EPSG:4979");

        Assert.NotNull(epsg4326.Geocentric);
        Assert.True(epsg4326.IsCompatibleWith(uri4326));
        Assert.True(epsg4326.IsCompatibleWith(epsg4979));
    }

    [Fact]
    public void CoordinateReferenceSystemParseRejectsUnsupportedGeographicIdentifier()
    {
        PlateauImportValidationException exception = Assert.Throws<PlateauImportValidationException>(
            () => CoordinateReferenceSystem.Parse("EPSG:999999"));

        Assert.Contains("Unsupported CityGML coordinate reference system", exception.Errors.Single());
    }

    [Fact]
    public void ProjectCityObjectsValidatesReferenceSystemBeforeProjectingCanonicalObjects()
    {
        LocalCityGmlGeometryProjector projector = new(new DefaultMaterialResolver(CommonMaterialCatalog.Create()));
        CoordinateReferenceSystem sourceReferenceSystem = CoordinateReferenceSystem.Parse("EPSG:6697");
        CachedSourceFileDescriptor sourceFile = new(
            new SourceFileDescriptor(
                RelativePath: "udx/bldg/53394525/plateau_tokyo23ku_bldg_53394525.gml",
                PackageName: "bldg",
                MatchedMeshCode: "53394525",
                RequiresMeshCodeBoundsFilter: false),
            [
                new ParsedCityObject(
                    SlotKey: "slot",
                    DisplayName: "display",
                    PackageName: "bldg",
                    ActualMeshCode: "53394525",
                    LodLevel: 1,
                    Surfaces: [],
                    ReferenceSystem: sourceReferenceSystem,
                    SourceFileRelativePath: "udx/bldg/53394525/plateau_tokyo23ku_bldg_53394525.gml",
                    SharedAcrossMeshCodes: false,
                    BuildingAttributes: BuildingAttributeContext.Empty),
            ],
            sourceReferenceSystem);

        PlateauImportValidationException exception = Assert.Throws<PlateauImportValidationException>(
            () => projector.ProjectCityObjects(
                    sourceFile,
                    CoordinateReferenceSystem.Parse("EPSG:6696"),
                    new GeodeticPoint(35.0, 139.0, 0.0),
                    globalCartesian: new LocalCartesian(35.0, 139.0, 0.0, new Geocentric(Ellipsoid.GRS80)),
                    demTerrainTextureOverlays: [],
                    requestedMeshCodeBounds: [],
                    selectedMeshCodes: ["53394525"],
                    request: new PlateauImportRequest(
                        Dataset: "tokyo23ku",
                        MeshCode: "53394525",
                        CityGmlSource: DatasetLocation.Local("C:\\fixture"),
                        PackageNames: ["bldg"]))
                .ToArray());

        Assert.Contains("Mixed CityGML coordinate reference systems are not supported", exception.Errors.Single());
    }

    [Fact]
    public void ProjectCityObjectsValidatesSourceFileReferenceSystemWhenSourceFileHasNoCityObjects()
    {
        LocalCityGmlGeometryProjector projector = new(new DefaultMaterialResolver(CommonMaterialCatalog.Create()));
        CachedSourceFileDescriptor sourceFile = new(
            new SourceFileDescriptor(
                RelativePath: "udx/bldg/53394525/empty.gml",
                PackageName: "bldg",
                MatchedMeshCode: "53394525",
                RequiresMeshCodeBoundsFilter: false),
            [],
            CoordinateReferenceSystem.Parse("EPSG:6697"));

        PlateauImportValidationException exception = Assert.Throws<PlateauImportValidationException>(
            () => projector.ProjectCityObjects(
                    sourceFile,
                    CoordinateReferenceSystem.Parse("EPSG:6696"),
                    new GeodeticPoint(35.0, 139.0, 0.0),
                    globalCartesian: new LocalCartesian(35.0, 139.0, 0.0, new Geocentric(Ellipsoid.GRS80)),
                    demTerrainTextureOverlays: [],
                    requestedMeshCodeBounds: [],
                    selectedMeshCodes: ["53394525"],
                    request: new PlateauImportRequest(
                        Dataset: "tokyo23ku",
                        MeshCode: "53394525",
                        CityGmlSource: DatasetLocation.Local("C:\\fixture"),
                        PackageNames: ["bldg"]))
                .ToArray());

        Assert.Contains("Mixed CityGML coordinate reference systems are not supported", exception.Errors.Single());
    }

    [Fact]
    public void ProjectCityObjectsUsesSelectedMeshCodesForRegexDemParentSourceFile()
    {
        LocalCityGmlGeometryProjector projector = new(new DefaultMaterialResolver(CommonMaterialCatalog.Create()));
        CoordinateReferenceSystem referenceSystem = CoordinateReferenceSystem.Parse("EPSG:4326");
        string sourceFileRelativePath = "udx/dem/533945/plateau_tokyo23ku_dem_533945.gml";
        CachedSourceFileDescriptor sourceFile = new(
            new SourceFileDescriptor(
                RelativePath: sourceFileRelativePath,
                PackageName: "dem",
                MatchedMeshCode: "533945",
                RequiresMeshCodeBoundsFilter: true),
            [
                new ParsedCityObject(
                    SlotKey: "dem-parent",
                    DisplayName: "DEM 533945",
                    PackageName: "dem",
                    ActualMeshCode: "533945",
                    LodLevel: 1,
                    Surfaces: [CreateDemSurfaceCovering("53394525", "53394526")],
                    ReferenceSystem: referenceSystem,
                    SourceFileRelativePath: sourceFileRelativePath,
                    SharedAcrossMeshCodes: true,
                    BuildingAttributes: BuildingAttributeContext.Empty),
            ],
            referenceSystem);
        string[] selectedMeshCodes = ["53394525", "53394526"];

        ImportedCityObject[] cityObjects = projector.ProjectCityObjects(
                sourceFile,
                referenceSystem,
                new GeodeticPoint(35.0, 139.0, 0.0),
                globalCartesian: new LocalCartesian(35.0, 139.0, 0.0, referenceSystem.Geocentric),
                demTerrainTextureOverlays: [],
                requestedMeshCodeBounds: MeshCodeBounds.CreateManyFromSelectedMeshCodes(selectedMeshCodes),
                selectedMeshCodes: selectedMeshCodes,
                request: new PlateauImportRequest(
                    Dataset: "tokyo23ku",
                    MeshCode: "5339452[56]",
                    CityGmlSource: DatasetLocation.Local("C:\\fixture"),
                    PackageNames: ["dem"]))
            .ToArray();

        Assert.Equal(["53394525", "53394526"], cityObjects.Select(static cityObject => cityObject.ActualMeshCode).ToArray());
        Assert.All(cityObjects, static cityObject => Assert.Equal("dem", cityObject.PackageName));
    }

    private static ParsedSurface CreateDemSurfaceCovering(string firstMeshCode, string secondMeshCode)
    {
        MeshCodeBounds firstBounds = MeshCodeBounds.Parse(firstMeshCode);
        MeshCodeBounds secondBounds = MeshCodeBounds.Parse(secondMeshCode);
        double southLatitude = Math.Min(firstBounds.SouthLatitude, secondBounds.SouthLatitude);
        double northLatitude = Math.Max(firstBounds.NorthLatitude, secondBounds.NorthLatitude);
        double westLongitude = Math.Min(firstBounds.WestLongitude, secondBounds.WestLongitude);
        double eastLongitude = Math.Max(firstBounds.EastLongitude, secondBounds.EastLongitude);

        return new ParsedSurface(
            ParsedSurfaceSemantic.Ground,
            new ParsedRing(
                [
                    new GeodeticPoint(southLatitude, westLongitude, 0.0),
                    new GeodeticPoint(southLatitude, eastLongitude, 0.0),
                    new GeodeticPoint(northLatitude, eastLongitude, 1.0),
                    new GeodeticPoint(northLatitude, westLongitude, 1.0),
                    new GeodeticPoint(southLatitude, westLongitude, 0.0),
                ],
                UVs: null),
            [],
            new ColorRgba(1.0, 1.0, 1.0, 1.0),
            TexturePayload: null);
    }
}
