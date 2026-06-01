using System.Linq;

using GeographicLib;

using PlateauResoniteLink.Application.Importing;
using PlateauResoniteLink.Domain.Importing;

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
                    SharedAcrossMeshCodes: false),
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
                    request: new PlateauImportRequest(
                        Dataset: "tokyo23ku",
                        MeshCode: "53394525",
                        CityGmlSource: DatasetLocation.Local("C:\\fixture"),
                        PackageNames: ["bldg"]))
                .ToArray());

        Assert.Contains("Mixed CityGML coordinate reference systems are not supported", exception.Errors.Single());
    }
}
