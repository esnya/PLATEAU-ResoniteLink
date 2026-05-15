using System.Linq;

using GeographicLib;

using PlateauResoniteLink.Application.Importing;
using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Tests.Profiles;

public sealed class LocalCityGmlGeometryProjectorTests
{
    [Fact]
    public void ProjectCityObjectsValidatesReferenceSystemBeforeProjectingCanonicalObjects()
    {
        LocalCityGmlGeometryProjector projector = new(new DefaultMaterialResolver(CommonMaterialCatalog.Create()));
        CachedSourceFileDescriptor sourceFile = new(
            new SourceFileDescriptor(
                RelativePath: "udx/bldg/53394525/plateau_tokyo23ku_bldg_53394525.gml",
                PackageName: "bldg",
                MatchedMeshCode: "53394525",
                RequiresMeshAreaFilter: false),
            [
                new ParsedCityObject(
                    SlotKey: "slot",
                    DisplayName: "display",
                    PackageName: "bldg",
                    ActualMeshCode: "53394525",
                    LodLevel: 1,
                    Surfaces: [],
                    ReferenceSystem: CoordinateReferenceSystem.Parse("EPSG:6697"),
                    SourceFileRelativePath: "udx/bldg/53394525/plateau_tokyo23ku_bldg_53394525.gml",
                    SharedAcrossMeshCodes: false),
            ]);

        PlateauImportValidationException exception = Assert.Throws<PlateauImportValidationException>(
            () => projector.ProjectCityObjects(
                    sourceFile,
                    CoordinateReferenceSystem.Parse("EPSG:6696"),
                    new GeodeticPoint(35.0, 139.0, 0.0),
                    globalCartesian: new LocalCartesian(35.0, 139.0, 0.0, new Geocentric(Ellipsoid.GRS80)),
                    demTerrainTextureOverlays: [],
                    requestedMeshAreas: [],
                    request: new PlateauImportRequest(
                        Dataset: "tokyo23ku",
                        MeshCode: "53394525",
                        Source: DatasetLocation.Local("C:\\fixture"),
                        PackageNames: ["bldg"]))
                .ToArray());

        Assert.Contains("Mixed CityGML coordinate reference systems are not supported", exception.Errors.Single());
    }
}
