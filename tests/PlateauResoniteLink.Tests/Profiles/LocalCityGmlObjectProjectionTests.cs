using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

using PlateauResoniteLink.Application.Importing;
using PlateauResoniteLink.Domain.Importing;
using PlateauResoniteLink.Targets.Resonite;
using PlateauResoniteLink.Tests.Application.Importing;

using ResoniteLink;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace PlateauResoniteLink.Tests.Profiles;

[SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores", Justification = "Test names describe contract cases.")]
public sealed class LocalCityGmlObjectProjectionTests
{
    private static readonly HttpClient SharedDatasetSourceResolverHttpClient = new();

    private static PlateauImportService CreateService(ISceneSink sceneSink, Action<string>? progressReporter = null)
    {
        LocalCityGmlDocumentReader documentReader = CreateDocumentReader();
        return new PlateauImportService(
            sceneSink,
            new CkanPlateauDatasetSourceResolver(
                SharedDatasetSourceResolverHttpClient,
                new RemoteArchiveDistributionPolicy(),
                new ArchiveFileLayoutPolicy()),
            importedSceneSourceFactory: new DefaultImportedSceneSourceFactory(
                documentReader,
                new DefaultImportedSceneSourceComposer(
                    new LocalCityGmlGeometryProjector(new DefaultMaterialResolver(CommonMaterialCatalog.Create())),
                    new DefaultDemTextureSourcePolicy(
                        new DefaultDemTerrainGeoReferencedRasterCatalogFactory(
                            new DefaultPlateauDatasetContentSourceFactory(
                                new RemoteArchiveDistributionPolicy(),
                                new ArchiveFileLayoutPolicy())))),
                new PassthroughImportedObjectUnitOptimizer()),
            commonMaterials: CommonMaterialCatalog.Create(),
            archiveFileLayoutPolicy: new ArchiveFileLayoutPolicy(),
            progressReporter);
    }

    private static LocalCityGmlDocumentReader CreateDocumentReader()
    {
        return new LocalCityGmlDocumentReader(
            new DefaultPlateauDatasetContentSourceFactory(
                new RemoteArchiveDistributionPolicy(),
                new ArchiveFileLayoutPolicy()),
            new CityGmlAppearanceStoreFactory(),
            new CityGmlLodSelector());
    }

    [Fact]
    [SuppressMessage(
        "Performance",
        "CA1849:Call async methods when in an async method",
        Justification = "This test intentionally compares the sync wrapper against the async entrypoint.")]
    public async Task ImportedSceneSourceFactoryComposesExpectedSetupMetadata()
    {
        string fixturePath = TestData.GetFixturePath("LocalPlateauDataset");
        LocalCityGmlDocumentReader documentReader = CreateDocumentReader();
        ResolvedLocalPlateauImportRequest request = ResolvedLocalPlateauImportRequestTestFactory.Create(
            cityGmlLocalSourcePath: fixturePath);
        PlateauImportRequest importRequest = request.ToImportRequest();

        DefaultImportedSceneSourceFactory factory = new(
            documentReader,
            new DefaultImportedSceneSourceComposer(
                new LocalCityGmlGeometryProjector(new DefaultMaterialResolver(CommonMaterialCatalog.Create())),
                new DefaultDemTextureSourcePolicy(
                    new DefaultDemTerrainGeoReferencedRasterCatalogFactory(
                        new DefaultPlateauDatasetContentSourceFactory(
                            new RemoteArchiveDistributionPolicy(),
                            new ArchiveFileLayoutPolicy())))),
            new PassthroughImportedObjectUnitOptimizer());
        IImportedSceneSource source = await factory.CreateAsync(request);

        Assert.Equal("3.0", source.Metadata.SchemaVersion);
        Assert.Equal("PLATEAU tokyo23ku 53394525", source.Metadata.SceneName);
        Assert.Equal(importRequest, source.Metadata.Request);
        Assert.Contains("bldg", source.Metadata.SourceDataset.PackageNames);
        Assert.Contains("53394525", source.Metadata.SourceDataset.SelectedMeshCodes!);
        Assert.NotEmpty(source.Metadata.SourceDataset.SourceFiles);
    }

    [Fact]
    public void GeneratedFacadeUvProjection_UsesFloorUnitsForBuildingWalls()
    {
        CoordinateReferenceSystem referenceSystem = CoordinateReferenceSystem.Parse("http://www.opengis.net/def/crs/EPSG/0/6697");
        GeodeticPoint origin = new(35.0, 139.0, 0.0);
        GeographicLib.LocalCartesian cartesian = new(
            origin.Latitude,
            origin.Longitude,
            origin.Altitude,
            referenceSystem.Geocentric);
        double longitudeDelta = FacadeFloorMetrics.DefaultFloorUnitMeters
            / (111320.0 * Math.Cos(origin.Latitude * (Math.PI / 180.0)));
        GeodeticPoint[] wallVertices =
        [
            origin,
            new(origin.Latitude, origin.Longitude + longitudeDelta, 0.0),
            new(origin.Latitude, origin.Longitude + longitudeDelta, FacadeFloorMetrics.DefaultFloorUnitMeters),
            new(origin.Latitude, origin.Longitude, FacadeFloorMetrics.DefaultFloorUnitMeters),
        ];
        ParsedSurface wallSurface = CreateParsedSurface(
            "wall",
            ParsedSurfaceSemantic.Wall,
            [.. wallVertices, wallVertices[0]]);

        MeshVertex[] vertices = TessellateSurfaceForTest(wallSurface, "bldg", origin, cartesian).Vertices;
        MeshVertex[] bottomVertices = SelectVerticesByY(vertices, vertices.Min(static vertex => vertex.Position.Y));
        MeshVertex[] leftVertices = SelectVerticesByX(vertices, vertices.Min(static vertex => vertex.Position.X));
        double bottomUSpan = bottomVertices.Max(static vertex => vertex.UV0.X) - bottomVertices.Min(static vertex => vertex.UV0.X);
        double bottomVSpan = bottomVertices.Max(static vertex => vertex.UV0.Y) - bottomVertices.Min(static vertex => vertex.UV0.Y);
        double leftUSpan = leftVertices.Max(static vertex => vertex.UV0.X) - leftVertices.Min(static vertex => vertex.UV0.X);
        double leftVSpan = leftVertices.Max(static vertex => vertex.UV0.Y) - leftVertices.Min(static vertex => vertex.UV0.Y);

        Assert.InRange(
            Math.Abs(bottomUSpan),
            0.95,
            1.05);
        Assert.InRange(
            Math.Abs(bottomVSpan),
            0.0,
            1e-6);
        Assert.InRange(
            Math.Abs(leftUSpan),
            0.0,
            1e-6);
        Assert.InRange(
            Math.Abs(leftVSpan),
            0.95,
            1.05);
    }

    [Fact]
    public void GeneratedFacadeUvProjection_AlignsVerticalPhaseToBuildingBottomAndTop()
    {
        CoordinateReferenceSystem referenceSystem = CoordinateReferenceSystem.Parse("http://www.opengis.net/def/crs/EPSG/0/6697");
        GeodeticPoint origin = new(35.0, 139.0, 2.0);
        GeographicLib.LocalCartesian cartesian = new(
            origin.Latitude,
            origin.Longitude,
            origin.Altitude,
            referenceSystem.Geocentric);
        double longitudeDelta = 7.0 / (111320.0 * Math.Cos(origin.Latitude * (Math.PI / 180.0)));
        GeodeticPoint[] wallVertices =
        [
            origin,
            new(origin.Latitude, origin.Longitude + longitudeDelta, 2.0),
            new(origin.Latitude, origin.Longitude + longitudeDelta, 9.0),
            new(origin.Latitude, origin.Longitude, 9.0),
        ];
        ParsedSurface wallSurface = CreateParsedSurface(
            "wall",
            ParsedSurfaceSemantic.Wall,
            [.. wallVertices, wallVertices[0]]);

        MeshVertex[] vertices = TessellateSurfaceForTest(
            wallSurface,
            "bldg",
            origin,
            cartesian,
            minimumY: 0.0,
            maximumY: 7.0,
            floorHeightMeters: 3.5,
            floorCount: 2).Vertices;
        double uvBottomY = AverageUvYAtY(vertices, vertices.Min(static vertex => vertex.Position.Y));
        double uvTopY = AverageUvYAtY(vertices, vertices.Max(static vertex => vertex.Position.Y));

        Assert.InRange(Math.Abs(uvBottomY), 0.0, 1e-5);
        Assert.InRange(Math.Abs(uvTopY - 2.0), 0.0, 1e-5);
    }

    [Fact]
    public void ProjectCityObject_CreatesFacadeUvFromProjectOwnedDefaultFloorUnit()
    {
        CoordinateReferenceSystem referenceSystem = CoordinateReferenceSystem.Parse("http://www.opengis.net/def/crs/EPSG/0/6697");
        GeodeticPoint origin = new(35.0, 139.0, 2.0);
        GeographicLib.LocalCartesian cartesian = new(
            origin.Latitude,
            origin.Longitude,
            origin.Altitude,
            referenceSystem.Geocentric);
        ParsedSurface wallSurface = CreateParsedSurface(
            "floor-context-wall",
            ParsedSurfaceSemantic.Wall,
            CreateVerticalQuadVertices(origin, widthMeters: 7.0, heightMeters: 7.0),
            texturePayload: null);
        ParsedCityObject cityObject = CreateParsedCityObject(
            "bldg",
            [wallSurface],
            referenceSystem,
            floorsAboveGround: 2,
            measuredHeightMeters: 11.8);

        ImportedCityObject projected = LocalCityGmlObjectProjection.ProjectCityObject(
            cityObject,
            origin,
            globalCartesian: cartesian,
            demTerrainTextureOverlay: null,
            materialResolver: new DefaultMaterialResolver(CommonMaterialCatalog.Create()));

        double minV = projected.Mesh.Vertices.Min(static vertex => vertex.UV0.Y);
        double maxV = projected.Mesh.Vertices.Max(static vertex => vertex.UV0.Y);

        Assert.Equal(0.0, minV, 6);
        Assert.Equal(2.0, maxV, 6);
    }

    [Fact]
    public void ProjectCityObject_IgnoresFloorMetadataWhenGeneratingProjectOwnedFacadeUv()
    {
        CoordinateReferenceSystem referenceSystem = CoordinateReferenceSystem.Parse("http://www.opengis.net/def/crs/EPSG/0/6697");
        GeodeticPoint origin = new(35.0, 139.0, 2.0);
        GeographicLib.LocalCartesian cartesian = new(
            origin.Latitude,
            origin.Longitude,
            origin.Altitude,
            referenceSystem.Geocentric);
        ParsedSurface wallSurface = CreateParsedSurface(
            "suspicious-floor-context-wall",
            ParsedSurfaceSemantic.Wall,
            CreateVerticalQuadVertices(origin, widthMeters: 7.0, heightMeters: 30.2),
            texturePayload: null);
        ParsedCityObject cityObject = CreateParsedCityObject(
            "bldg",
            [wallSurface],
            referenceSystem,
            floorsAboveGround: 1,
            measuredHeightMeters: 30.2);

        ImportedCityObject projected = LocalCityGmlObjectProjection.ProjectCityObject(
            cityObject,
            origin,
            globalCartesian: cartesian,
            demTerrainTextureOverlay: null,
            materialResolver: new DefaultMaterialResolver(CommonMaterialCatalog.Create()));

        double minV = projected.Mesh.Vertices.Min(static vertex => vertex.UV0.Y);
        double maxV = projected.Mesh.Vertices.Max(static vertex => vertex.UV0.Y);

        Assert.Equal(0.0, minV, 6);
        Assert.Equal(9.0, maxV, 6);
    }

    [Fact]
    public void ProjectCityObject_IgnoresUnknownStoreySentinelForFacadeFloorUv()
    {
        CoordinateReferenceSystem referenceSystem = CoordinateReferenceSystem.Parse("http://www.opengis.net/def/crs/EPSG/0/6697");
        GeodeticPoint origin = new(35.0, 139.0, 2.0);
        GeographicLib.LocalCartesian cartesian = new(
            origin.Latitude,
            origin.Longitude,
            origin.Altitude,
            referenceSystem.Geocentric);
        ParsedSurface wallSurface = CreateParsedSurface(
            "unknown-storey-wall",
            ParsedSurfaceSemantic.Wall,
            CreateVerticalQuadVertices(origin, widthMeters: 7.0, heightMeters: 3.2),
            texturePayload: null);
        ParsedCityObject cityObject = CreateParsedCityObject(
            "bldg",
            [wallSurface],
            referenceSystem,
            floorsAboveGround: FacadeFloorMetrics.UnknownFloorCountSentinel,
            measuredHeightMeters: null);

        ImportedCityObject projected = LocalCityGmlObjectProjection.ProjectCityObject(
            cityObject,
            origin,
            globalCartesian: cartesian,
            demTerrainTextureOverlay: null,
            materialResolver: new DefaultMaterialResolver(CommonMaterialCatalog.Create()));

        double minV = projected.Mesh.Vertices.Min(static vertex => vertex.UV0.Y);
        double maxV = projected.Mesh.Vertices.Max(static vertex => vertex.UV0.Y);

        Assert.Equal(0.0, minV, 6);
        Assert.Equal(1.0, maxV, 6);
    }

    [Fact]
    public void ProjectCityObjectPreservesSourceTextureCoordinatesForTexturedBuildingWall()
    {
        CoordinateReferenceSystem referenceSystem = CoordinateReferenceSystem.Parse("http://www.opengis.net/def/crs/EPSG/0/6697");
        GeodeticPoint origin = new(35.0, 139.0, 0.0);
        GeographicLib.LocalCartesian cartesian = new(
            origin.Latitude,
            origin.Longitude,
            origin.Altitude,
            referenceSystem.Geocentric);
        Float2[] sourceUvs =
        [
            new(0.10, 0.20),
            new(1.15, -0.20),
            new(1.15, 0.80),
            new(0.10, 0.80),
            new(0.10, 0.20),
        ];
        ParsedSurface wallSurface = CreateParsedSurface(
            "textured-wall",
            ParsedSurfaceSemantic.Wall,
            CreateVerticalQuadVertices(origin, 8.0, 7.0),
            CreateTexturePayload("wall-texture"),
            baseColor: null,
            uvs: sourceUvs);

        ParsedCityObject cityObject = CreateParsedCityObject(
            "bldg",
            [wallSurface],
            referenceSystem,
            lodLevel: 1,
            measuredHeightMeters: 7.0);

        ImportedCityObject projected = LocalCityGmlObjectProjection.ProjectCityObject(
            cityObject,
            origin,
            globalCartesian: cartesian,
            demTerrainTextureOverlay: null,
            materialResolver: new DefaultMaterialResolver(CommonMaterialCatalog.Create()));

        MaterialBinding material = Assert.Single(projected.Materials);
        Assert.Equal(TextureSourceKind.Dataset, material.TextureSourceKind);
        Assert.Null(material.Family);
        Assert.Equal(MaterialProjection.Uv, material.Projection);
        Assert.NotNull(material.TexturePayload);
        Assert.Equal(CommonMaterialCatalog.Create().Generic.Uv, material.CommonMaterial);

        Float2[] projectedUvs = projected.Mesh.Vertices.Select(static vertex => vertex.UV0).ToArray();
        foreach (Float2 sourceUv in sourceUvs.SkipLast(1))
        {
            Assert.Contains(projectedUvs, actualUv => ApproximatelyEqualFloat2(actualUv, sourceUv, 1e-9));
        }
    }

    [Fact]
    public void ProjectCityObjectPreservesDatasetTextureForTexturedBuildingRoof()
    {
        CoordinateReferenceSystem referenceSystem = CoordinateReferenceSystem.Parse("http://www.opengis.net/def/crs/EPSG/0/6697");
        GeodeticPoint origin = new(35.0, 139.0, 0.0);
        GeographicLib.LocalCartesian cartesian = new(
            origin.Latitude,
            origin.Longitude,
            origin.Altitude,
            referenceSystem.Geocentric);
        Float2[] sourceUvs =
        [
            new(0.00, 0.00),
            new(1.00, 0.00),
            new(1.00, 1.00),
            new(0.00, 1.00),
            new(0.00, 0.00),
        ];
        ParsedSurface roofSurface = CreateParsedSurface(
            "textured-roof",
            ParsedSurfaceSemantic.Roof,
            CreateHorizontalQuadVertices(origin, altitudeMeters: 8.0, sizeMeters: 6.0, reverseWinding: true),
            CreateTexturePayload("roof-texture"),
            baseColor: null,
            uvs: sourceUvs);
        ParsedCityObject cityObject = CreateParsedCityObject(
            "bldg",
            [roofSurface],
            referenceSystem,
            lodLevel: 2,
            measuredHeightMeters: 8.0);

        ImportedCityObject projected = LocalCityGmlObjectProjection.ProjectCityObject(
            cityObject,
            origin,
            globalCartesian: cartesian,
            demTerrainTextureOverlay: CreateThirdMeshOverlay("53394525"),
            materialResolver: new DefaultMaterialResolver(CommonMaterialCatalog.Create()));

        MaterialBinding material = Assert.Single(projected.Materials);
        Assert.Equal(TextureSourceKind.Dataset, material.TextureSourceKind);
        Assert.Null(material.Family);
        Assert.Equal(MaterialProjection.Uv, material.Projection);
        Assert.NotNull(material.TexturePayload);
        Assert.Null(material.TerrainOverlay);
        Assert.Equal(CommonMaterialCatalog.Create().Generic.Uv, material.CommonMaterial);

        Float2[] projectedUvs = projected.Mesh.Vertices.Select(static vertex => vertex.UV0).ToArray();
        foreach (Float2 sourceUv in sourceUvs.SkipLast(1))
        {
            Assert.Contains(projectedUvs, actualUv => ApproximatelyEqualFloat2(actualUv, sourceUv, 1e-9));
        }
    }

    [Fact]
    public void ProjectCityObjectAssignsDemTerrainMaterialAndHorizontalUvToTexturelessRoofSurface()
    {
        CoordinateReferenceSystem referenceSystem = CoordinateReferenceSystem.Parse("http://www.opengis.net/def/crs/EPSG/0/6697");
        TerrainTextureOverlay overlay = CreateThirdMeshOverlay("53394525");
        GeodeticPoint origin = CreateMeshCenterPoint("53394525", altitudeMeters: 8.0);
        GeographicLib.LocalCartesian cartesian = new(origin.Latitude, origin.Longitude, origin.Altitude, referenceSystem.Geocentric);
        Float2[] sourceUvs = [new(0.01, 0.02), new(0.03, 0.04), new(0.05, 0.06), new(0.07, 0.08), new(0.01, 0.02)];
        ParsedSurface roofSurface = CreateParsedSurface(
            "textureless-roof",
            ParsedSurfaceSemantic.Roof,
            CreateMeshRelativeQuadVertices("53394525", altitudeMeters: 8.0, minRatio: 0.45, maxRatio: 0.55, reverseWinding: true),
            texturePayload: null,
            uvs: sourceUvs);
        ParsedCityObject cityObject = CreateParsedCityObject("bldg", [roofSurface], referenceSystem);

        ImportedCityObject projected = LocalCityGmlObjectProjection.ProjectCityObject(
            cityObject,
            origin,
            globalCartesian: cartesian,
            demTerrainTextureOverlay: overlay,
            materialResolver: new DefaultMaterialResolver(CommonMaterialCatalog.Create()));

        MaterialBinding material = Assert.Single(projected.Materials);
        Assert.Equal(TextureSourceKind.Dataset, material.TextureSourceKind);
        Assert.Equal(MaterialProjection.Uv, material.Projection);
        Assert.Equal(MaterialReuseScope.PerObject, material.ReuseScope);
        Assert.Null(material.Family);
        Assert.Null(material.TexturePayload);
        Assert.Same(overlay, material.TerrainOverlay);
        Assert.Equal(CommonMaterialCatalog.Create().Generic.Uv, material.CommonMaterial);
        Assert.All(projected.Mesh.Vertices, vertex => Assert.DoesNotContain(sourceUvs, sourceUv => ApproximatelyEqualFloat2(vertex.UV0, sourceUv, 1e-9)));
        Assert.Contains(projected.Mesh.Vertices, vertex => vertex.UV0.X is > 0.45 and < 0.55);
        Assert.Contains(projected.Mesh.Vertices, vertex => vertex.UV0.Y is > 0.45 and < 0.55);
    }

    [Fact]
    public void ProjectCityObjectGeneratesGableRoofFromLod1RectangularTopAndKeepsHorizontalDemUv()
    {
        CoordinateReferenceSystem referenceSystem = CoordinateReferenceSystem.Parse("http://www.opengis.net/def/crs/EPSG/0/6697");
        TerrainTextureOverlay overlay = CreateThirdMeshOverlay("53394525");
        GeodeticPoint origin = CreateMeshCenterPoint("53394525", altitudeMeters: 8.0);
        ParsedSurface topSurface = CreateParsedSurface(
            "lod1-top",
            ParsedSurfaceSemantic.Roof,
            CreateMeshRelativeQuadVertices("53394525", altitudeMeters: 8.0, minRatio: 0.45, maxRatio: 0.55, reverseWinding: true),
            texturePayload: null);
        ParsedSurface bottomSurface = CreateParsedSurface(
            "lod1-bottom",
            ParsedSurfaceSemantic.Ground,
            CreateMeshRelativeQuadVertices("53394525", altitudeMeters: 0.0, minRatio: 0.45, maxRatio: 0.55, reverseWinding: false),
            texturePayload: null);
        ParsedCityObject cityObject = CreateParsedCityObject(
            "bldg",
            [bottomSurface, topSurface],
            referenceSystem,
            buildingAttributes: CreateBuildingAttributes(CityGmlRoofShape.Gable));

        ImportedCityObject projected = LocalCityGmlObjectProjection.ProjectCityObject(
            cityObject,
            origin,
            globalCartesian: new GeographicLib.LocalCartesian(origin.Latitude, origin.Longitude, origin.Altitude, referenceSystem.Geocentric),
            demTerrainTextureOverlay: overlay,
            materialResolver: new DefaultMaterialResolver(CommonMaterialCatalog.Create()));

        Assert.Contains(projected.Materials, material => ReferenceEquals(overlay, material.TerrainOverlay));
        Assert.Contains(projected.Materials, IsBuildingFacadeMaterial);
        Assert.DoesNotContain(projected.Materials, static material => material.Family == BundledDefaultMaterialFamilies.Roof);
        Assert.DoesNotContain(projected.Materials, static material => material.Projection == MaterialProjection.Triplanar);
        Assert.True(projected.Mesh.Vertices.Max(static vertex => vertex.Position.Y) > 8.25);
        Assert.Contains(projected.Mesh.Vertices, vertex => vertex.UV0.X is > 0.45 and < 0.55);
        Assert.Contains(projected.Mesh.Vertices, vertex => vertex.UV0.Y is > 0.45 and < 0.55);
    }

    [Fact]
    public void ProjectCityObjectInfersRoofShapeWhenRoofTypeIsOther()
    {
        CoordinateReferenceSystem referenceSystem = CoordinateReferenceSystem.Parse("http://www.opengis.net/def/crs/EPSG/0/6697");
        TerrainTextureOverlay overlay = CreateThirdMeshOverlay("53394525");
        GeodeticPoint origin = CreateMeshCenterPoint("53394525", altitudeMeters: 8.0);
        ParsedSurface topSurface = CreateParsedSurface(
            "roof-type-other-lod1-top",
            ParsedSurfaceSemantic.Roof,
            CreateMeshRelativeQuadVertices("53394525", altitudeMeters: 8.0, minRatio: 0.45, maxRatio: 0.55, reverseWinding: true),
            texturePayload: null);
        ParsedSurface bottomSurface = CreateParsedSurface(
            "roof-type-other-lod1-bottom",
            ParsedSurfaceSemantic.Ground,
            CreateMeshRelativeQuadVertices("53394525", altitudeMeters: 0.0, minRatio: 0.45, maxRatio: 0.55, reverseWinding: false),
            texturePayload: null);
        ParsedCityObject cityObject = CreateParsedCityObject(
            "bldg",
            [bottomSurface, topSurface],
            referenceSystem,
            buildingAttributes: CreateBuildingAttributes(
                CityGmlRoofShape.Other,
                PlateauBuildingUse.DetachedResidential,
                PlateauBuildingStructure.Wood));

        ImportedCityObject projected = LocalCityGmlObjectProjection.ProjectCityObject(
            cityObject,
            origin,
            globalCartesian: new GeographicLib.LocalCartesian(origin.Latitude, origin.Longitude, origin.Altitude, referenceSystem.Geocentric),
            demTerrainTextureOverlay: overlay,
            materialResolver: new DefaultMaterialResolver(CommonMaterialCatalog.Create()));

        Assert.Contains(projected.Materials, material => ReferenceEquals(overlay, material.TerrainOverlay));
        Assert.DoesNotContain(projected.Materials, static material => material.Family == BundledDefaultMaterialFamilies.Roof);
        Assert.DoesNotContain(projected.Materials, static material => material.Projection == MaterialProjection.Triplanar);
        Assert.True(projected.Mesh.Vertices.Max(static vertex => vertex.Position.Y) > 8.25);
    }

    [Theory]
    [InlineData((int)PlateauBuildingStructure.NonWood)]
    [InlineData((int)PlateauBuildingStructure.ReinforcedConcrete)]
    [InlineData((int)PlateauBuildingStructure.SteelReinforcedConcrete)]
    public void ProjectCityObjectInfersUrbanFlatRoofFromCityGmlFunctionCodeAndFlatRoofStructures(int structureValue)
    {
        CoordinateReferenceSystem referenceSystem = CoordinateReferenceSystem.Parse("http://www.opengis.net/def/crs/EPSG/0/6697");
        TerrainTextureOverlay overlay = CreateThirdMeshOverlay("53394525");
        GeodeticPoint origin = CreateMeshCenterPoint("53394525", altitudeMeters: 8.0);
        PlateauBuildingStructure structure = (PlateauBuildingStructure)structureValue;
        ParsedSurface topSurface = CreateParsedSurface(
            "function-code-lod1-top",
            ParsedSurfaceSemantic.Roof,
            CreateMeshRelativeQuadVertices("53394525", altitudeMeters: 8.0, minRatio: 0.45, maxRatio: 0.55, reverseWinding: true),
            texturePayload: null);
        ParsedSurface bottomSurface = CreateParsedSurface(
            "function-code-lod1-bottom",
            ParsedSurfaceSemantic.Ground,
            CreateMeshRelativeQuadVertices("53394525", altitudeMeters: 0.0, minRatio: 0.45, maxRatio: 0.55, reverseWinding: false),
            texturePayload: null);
        ParsedCityObject cityObject = CreateParsedCityObject(
            "bldg",
            [bottomSurface, topSurface],
            referenceSystem,
            buildingAttributes: BuildingAttributeContext.Empty with
            {
                CityGmlFunctionCodes = ["401"],
                Structures = [new BuildingCodeValue<PlateauBuildingStructure>(structure, CreateStructureTypeCode(structure))],
                MeasuredHeightMeters = new BuildingMetricValue(8.0),
                BuildingFootprintArea = new BuildingMetricValue(100.0),
            });

        ImportedCityObject projected = LocalCityGmlObjectProjection.ProjectCityObject(
            cityObject,
            origin,
            globalCartesian: new GeographicLib.LocalCartesian(origin.Latitude, origin.Longitude, origin.Altitude, referenceSystem.Geocentric),
            demTerrainTextureOverlay: overlay,
            materialResolver: new DefaultMaterialResolver(CommonMaterialCatalog.Create()));

        MaterialBinding roofMaterial = Assert.Single(projected.Materials, material => ReferenceEquals(overlay, material.TerrainOverlay));
        Assert.True(projected.Mesh.Vertices.Max(static vertex => vertex.Position.Y) < 8.25);
        Assert.DoesNotContain(projected.Materials, static material => material.Family == BundledDefaultMaterialFamilies.Roof);
        Assert.DoesNotContain(projected.Materials, static material => material.Projection == MaterialProjection.Triplanar);
    }

    [Theory]
    [InlineData((int)PlateauBuildingStructure.Steel)]
    [InlineData((int)PlateauBuildingStructure.LightweightSteel)]
    [InlineData((int)PlateauBuildingStructure.ConcreteBlock)]
    public void ProjectCityObjectDoesNotForceUrbanFlatRoofFromGeneralNonWoodStructures(int structureValue)
    {
        CoordinateReferenceSystem referenceSystem = CoordinateReferenceSystem.Parse("http://www.opengis.net/def/crs/EPSG/0/6697");
        TerrainTextureOverlay overlay = CreateThirdMeshOverlay("53394525");
        GeodeticPoint origin = CreateMeshCenterPoint("53394525", altitudeMeters: 8.0);
        PlateauBuildingStructure structure = (PlateauBuildingStructure)structureValue;
        ParsedSurface topSurface = CreateParsedSurface(
            "general-non-wood-lod1-top",
            ParsedSurfaceSemantic.Roof,
            CreateMeshRelativeRectangleVertices(
                "53394525",
                altitudeMeters: 8.0,
                minLatitudeRatio: 0.47,
                maxLatitudeRatio: 0.53,
                minLongitudeRatio: 0.35,
                maxLongitudeRatio: 0.65,
                reverseWinding: true),
            texturePayload: null);
        ParsedSurface bottomSurface = CreateParsedSurface(
            "general-non-wood-lod1-bottom",
            ParsedSurfaceSemantic.Ground,
            CreateMeshRelativeRectangleVertices(
                "53394525",
                altitudeMeters: 0.0,
                minLatitudeRatio: 0.47,
                maxLatitudeRatio: 0.53,
                minLongitudeRatio: 0.35,
                maxLongitudeRatio: 0.65,
                reverseWinding: false),
            texturePayload: null);
        ParsedCityObject cityObject = CreateParsedCityObject(
            "bldg",
            [bottomSurface, topSurface],
            referenceSystem,
            buildingAttributes: BuildingAttributeContext.Empty with
            {
                CityGmlFunctionCodes = ["401"],
                Structures = [new BuildingCodeValue<PlateauBuildingStructure>(structure, CreateStructureTypeCode(structure))],
                MeasuredHeightMeters = new BuildingMetricValue(8.0),
                BuildingFootprintArea = new BuildingMetricValue(100.0),
            });

        ImportedCityObject projected = LocalCityGmlObjectProjection.ProjectCityObject(
            cityObject,
            origin,
            globalCartesian: new GeographicLib.LocalCartesian(origin.Latitude, origin.Longitude, origin.Altitude, referenceSystem.Geocentric),
            demTerrainTextureOverlay: overlay,
            materialResolver: new DefaultMaterialResolver(CommonMaterialCatalog.Create()));

        Assert.True(projected.Mesh.Vertices.Max(static vertex => vertex.Position.Y) > 8.25);
        Assert.Contains(projected.Materials, IsBuildingFacadeMaterial);
        Assert.DoesNotContain(projected.Materials, static material => material.Projection == MaterialProjection.Triplanar);
    }

    [Fact]
    public void ProjectCityObjectGeneratesShedRoofWallExtensionsAsFacadeWithoutStretchingOriginalWallUv()
    {
        CoordinateReferenceSystem referenceSystem = CoordinateReferenceSystem.Parse("http://www.opengis.net/def/crs/EPSG/0/6697");
        TerrainTextureOverlay overlay = CreateThirdMeshOverlay("53394525");
        GeodeticPoint origin = CreateMeshCenterPoint("53394525", altitudeMeters: 0.0);
        ParsedSurface topSurface = CreateParsedSurface(
            "shed-lod1-top",
            ParsedSurfaceSemantic.Roof,
            CreateMeshRelativeQuadVertices("53394525", altitudeMeters: 8.0, minRatio: 0.45, maxRatio: 0.55, reverseWinding: true),
            texturePayload: null);
        ParsedSurface bottomSurface = CreateParsedSurface(
            "shed-lod1-bottom",
            ParsedSurfaceSemantic.Ground,
            CreateMeshRelativeQuadVertices("53394525", altitudeMeters: 0.0, minRatio: 0.45, maxRatio: 0.55, reverseWinding: false),
            texturePayload: null);
        ParsedSurface wallSurface = CreateParsedSurface(
            "shed-wall",
            ParsedSurfaceSemantic.Wall,
            CreateMeshEdgeWallVertices("53394525", altitudeMeters: 0.0, heightMeters: 8.0, ratio: 0.45),
            texturePayload: null);
        ParsedCityObject cityObject = CreateParsedCityObject(
            "bldg",
            [bottomSurface, wallSurface, topSurface],
            referenceSystem,
            buildingAttributes: CreateBuildingAttributes(CityGmlRoofShape.Shed));

        ImportedCityObject projected = LocalCityGmlObjectProjection.ProjectCityObject(
            cityObject,
            origin,
            globalCartesian: new GeographicLib.LocalCartesian(origin.Latitude, origin.Longitude, origin.Altitude, referenceSystem.Geocentric),
            demTerrainTextureOverlay: overlay,
            materialResolver: new DefaultMaterialResolver(CommonMaterialCatalog.Create()));

        MaterialBinding facadeMaterial = Assert.Single(projected.Materials, IsBuildingFacadeMaterial);
        Assert.Contains(projected.Materials, material => ReferenceEquals(overlay, material.TerrainOverlay));
        Assert.DoesNotContain(projected.Materials, static material => material.Family == BundledDefaultMaterialFamilies.Roof);
        Assert.DoesNotContain(projected.Materials, static material => material.Projection == MaterialProjection.Triplanar);

        int facadeSubmeshIndex = facadeMaterial.SubmeshIndices.Single();
        MeshSubmesh facadeSubmesh = Assert.Single(projected.Mesh.Submeshes, submesh => submesh.Index == facadeSubmeshIndex);
        double maxFacadeVAtOriginalTop = facadeSubmesh.TriangleVertexIndices
            .Select(index => projected.Mesh.Vertices[index])
            .Where(vertex => Math.Abs(vertex.Position.Y - 8.0) < 0.05)
            .Select(static vertex => vertex.UV0.Y)
            .DefaultIfEmpty(double.NaN)
            .Max();
        double expectedOriginalWallTopV = Math.Ceiling(8.0 / FacadeFloorMetrics.DefaultFloorUnitMeters);
        Assert.InRange(maxFacadeVAtOriginalTop, expectedOriginalWallTopV - 0.05, expectedOriginalWallTopV + 0.05);
    }

    [Fact]
    public void ProjectCityObjectUsesGeneratedRoofWallsForFacadeUvWhenNoOriginalWallHeightExists()
    {
        CoordinateReferenceSystem referenceSystem = CoordinateReferenceSystem.Parse("http://www.opengis.net/def/crs/EPSG/0/6697");
        TerrainTextureOverlay overlay = CreateThirdMeshOverlay("53394525");
        GeodeticPoint origin = CreateMeshCenterPoint("53394525", altitudeMeters: 0.0);
        ParsedSurface topSurface = CreateParsedSurface(
            "shed-generated-only-lod1-top",
            ParsedSurfaceSemantic.Roof,
            CreateMeshRelativeQuadVertices("53394525", altitudeMeters: 8.0, minRatio: 0.45, maxRatio: 0.55, reverseWinding: true),
            texturePayload: null);
        ParsedSurface bottomSurface = CreateParsedSurface(
            "shed-generated-only-lod1-bottom",
            ParsedSurfaceSemantic.Ground,
            CreateMeshRelativeQuadVertices("53394525", altitudeMeters: 0.0, minRatio: 0.45, maxRatio: 0.55, reverseWinding: false),
            texturePayload: null);
        ParsedCityObject cityObject = CreateParsedCityObject(
            "bldg",
            [bottomSurface, topSurface],
            referenceSystem,
            buildingAttributes: CreateBuildingAttributes(CityGmlRoofShape.Shed));

        ImportedCityObject projected = LocalCityGmlObjectProjection.ProjectCityObject(
            cityObject,
            origin,
            globalCartesian: new GeographicLib.LocalCartesian(origin.Latitude, origin.Longitude, origin.Altitude, referenceSystem.Geocentric),
            demTerrainTextureOverlay: overlay,
            materialResolver: new DefaultMaterialResolver(CommonMaterialCatalog.Create()));

        MaterialBinding facadeMaterial = Assert.Single(projected.Materials, IsBuildingFacadeMaterial);
        MeshSubmesh facadeSubmesh = Assert.Single(projected.Mesh.Submeshes, submesh => submesh.Index == facadeMaterial.SubmeshIndices.Single());
        double maxFacadeV = facadeSubmesh.TriangleVertexIndices
            .Select(index => projected.Mesh.Vertices[index].UV0.Y)
            .Max();

        Assert.InRange(maxFacadeV, 0.1, 10.0);
    }

    [Theory]
    [InlineData((int)CityGmlRoofShape.Shed)]
    [InlineData((int)CityGmlRoofShape.Gable)]
    public void ProjectCityObjectGeneratesLod1RoofWallFacesOutward(int roofShapeValue)
    {
        CityGmlRoofShape roofShape = (CityGmlRoofShape)roofShapeValue;
        CoordinateReferenceSystem referenceSystem = CoordinateReferenceSystem.Parse("http://www.opengis.net/def/crs/EPSG/0/6697");
        TerrainTextureOverlay overlay = CreateThirdMeshOverlay("53394525");
        GeodeticPoint origin = CreateMeshCenterPoint("53394525", altitudeMeters: 0.0);
        ParsedSurface topSurface = CreateParsedSurface(
            "roof-wall-facing-lod1-top",
            ParsedSurfaceSemantic.Roof,
            CreateMeshRelativeQuadVertices("53394525", altitudeMeters: 8.0, minRatio: 0.45, maxRatio: 0.55, reverseWinding: true),
            texturePayload: null);
        ParsedSurface bottomSurface = CreateParsedSurface(
            "roof-wall-facing-lod1-bottom",
            ParsedSurfaceSemantic.Ground,
            CreateMeshRelativeQuadVertices("53394525", altitudeMeters: 0.0, minRatio: 0.45, maxRatio: 0.55, reverseWinding: false),
            texturePayload: null);
        ParsedCityObject cityObject = CreateParsedCityObject(
            "bldg",
            [bottomSurface, topSurface],
            referenceSystem,
            buildingAttributes: CreateBuildingAttributes(roofShape));

        ImportedCityObject projected = LocalCityGmlObjectProjection.ProjectCityObject(
            cityObject,
            origin,
            globalCartesian: new GeographicLib.LocalCartesian(origin.Latitude, origin.Longitude, origin.Altitude, referenceSystem.Geocentric),
            demTerrainTextureOverlay: overlay,
            materialResolver: new DefaultMaterialResolver(CommonMaterialCatalog.Create()));

        MaterialBinding facadeMaterial = Assert.Single(projected.Materials, IsBuildingFacadeMaterial);
        MeshSubmesh facadeSubmesh = Assert.Single(projected.Mesh.Submeshes, submesh => submesh.Index == facadeMaterial.SubmeshIndices.Single());
        AssertGeneratedUpperFacadeTrianglesFaceOutward(projected.Mesh, facadeSubmesh, baseHeight: 8.0);
    }

    [Theory]
    [InlineData((int)CityGmlRoofShape.Shed)]
    [InlineData((int)CityGmlRoofShape.Gable)]
    [InlineData((int)CityGmlRoofShape.Hip)]
    [InlineData((int)CityGmlRoofShape.Irimoya)]
    public void ProjectCityObjectGeneratesLod1RoofFacesUpward(int roofShapeValue)
    {
        CityGmlRoofShape roofShape = (CityGmlRoofShape)roofShapeValue;
        CoordinateReferenceSystem referenceSystem = CoordinateReferenceSystem.Parse("http://www.opengis.net/def/crs/EPSG/0/6697");
        TerrainTextureOverlay overlay = CreateThirdMeshOverlay("53394525");
        GeodeticPoint origin = CreateMeshCenterPoint("53394525", altitudeMeters: 0.0);
        ParsedSurface topSurface = CreateParsedSurface(
            "roof-facing-lod1-top",
            ParsedSurfaceSemantic.Roof,
            CreateMeshRelativeQuadVertices("53394525", altitudeMeters: 8.0, minRatio: 0.45, maxRatio: 0.55, reverseWinding: true),
            texturePayload: null);
        ParsedSurface bottomSurface = CreateParsedSurface(
            "roof-facing-lod1-bottom",
            ParsedSurfaceSemantic.Ground,
            CreateMeshRelativeQuadVertices("53394525", altitudeMeters: 0.0, minRatio: 0.45, maxRatio: 0.55, reverseWinding: false),
            texturePayload: null);
        ParsedCityObject cityObject = CreateParsedCityObject(
            "bldg",
            [bottomSurface, topSurface],
            referenceSystem,
            buildingAttributes: CreateBuildingAttributes(roofShape));

        ImportedCityObject projected = LocalCityGmlObjectProjection.ProjectCityObject(
            cityObject,
            origin,
            globalCartesian: new GeographicLib.LocalCartesian(origin.Latitude, origin.Longitude, origin.Altitude, referenceSystem.Geocentric),
            demTerrainTextureOverlay: overlay,
            materialResolver: new DefaultMaterialResolver(CommonMaterialCatalog.Create()));

        MaterialBinding roofMaterial = Assert.Single(projected.Materials, material => ReferenceEquals(overlay, material.TerrainOverlay));
        MeshSubmesh roofSubmesh = Assert.Single(projected.Mesh.Submeshes, submesh => submesh.Index == roofMaterial.SubmeshIndices.Single());
        AssertGeneratedUpperRoofTrianglesFaceUpward(projected.Mesh, roofSubmesh, baseHeight: 8.0);
    }

    [Fact]
    public void ProjectCityObjectDoesNotGenerateRoofForLod2RectangularTop()
    {
        CoordinateReferenceSystem referenceSystem = CoordinateReferenceSystem.Parse("http://www.opengis.net/def/crs/EPSG/0/6697");
        TerrainTextureOverlay overlay = CreateThirdMeshOverlay("53394525");
        GeodeticPoint origin = CreateMeshCenterPoint("53394525", altitudeMeters: 8.0);
        ParsedSurface topSurface = CreateParsedSurface(
            "lod2-top",
            ParsedSurfaceSemantic.Roof,
            CreateMeshRelativeQuadVertices("53394525", altitudeMeters: 8.0, minRatio: 0.45, maxRatio: 0.55, reverseWinding: true),
            texturePayload: null);
        ParsedSurface bottomSurface = CreateParsedSurface(
            "lod2-bottom",
            ParsedSurfaceSemantic.Ground,
            CreateMeshRelativeQuadVertices("53394525", altitudeMeters: 0.0, minRatio: 0.45, maxRatio: 0.55, reverseWinding: false),
            texturePayload: null);
        ParsedCityObject cityObject = CreateParsedCityObject(
            "bldg",
            [bottomSurface, topSurface],
            referenceSystem,
            lodLevel: 2,
            buildingAttributes: CreateBuildingAttributes(CityGmlRoofShape.Gable));

        ImportedCityObject projected = LocalCityGmlObjectProjection.ProjectCityObject(
            cityObject,
            origin,
            globalCartesian: new GeographicLib.LocalCartesian(origin.Latitude, origin.Longitude, origin.Altitude, referenceSystem.Geocentric),
            demTerrainTextureOverlay: overlay,
            materialResolver: new DefaultMaterialResolver(CommonMaterialCatalog.Create()));

        Assert.True(projected.Mesh.Vertices.Max(static vertex => vertex.Position.Y) < 8.25);
    }

    [Theory]
    [InlineData((int)ParsedSurfaceSemantic.Ground)]
    [InlineData((int)ParsedSurfaceSemantic.OuterCeiling)]
    [InlineData((int)ParsedSurfaceSemantic.OuterFloor)]
    public void ProjectCityObjectAssignsDemTerrainMaterialToFlatTopHorizontalBuildingSurface(
        int topSemanticValue)
    {
        ParsedSurfaceSemantic topSemantic = (ParsedSurfaceSemantic)topSemanticValue;
        CoordinateReferenceSystem referenceSystem = CoordinateReferenceSystem.Parse("http://www.opengis.net/def/crs/EPSG/0/6697");
        TerrainTextureOverlay overlay = CreateThirdMeshOverlay("53394525");
        GeodeticPoint origin = CreateMeshCenterPoint("53394525", altitudeMeters: 8.0);
        ParsedSurface topSurface = CreateParsedSurface(
            "flat-top",
            topSemantic,
            CreateMeshRelativeQuadVertices("53394525", altitudeMeters: 8.0, minRatio: 0.45, maxRatio: 0.55, reverseWinding: true),
            texturePayload: null);
        ParsedSurface bottomSurface = CreateParsedSurface(
            "flat-bottom",
            ParsedSurfaceSemantic.Ground,
            CreateMeshRelativeQuadVertices("53394525", altitudeMeters: 0.0, minRatio: 0.45, maxRatio: 0.55, reverseWinding: false),
            texturePayload: null);
        ParsedCityObject cityObject = CreateParsedCityObject(
            "bldg",
            [bottomSurface, topSurface],
            referenceSystem,
            buildingAttributes: CreateBuildingAttributes(CityGmlRoofShape.Flat));

        ImportedCityObject projected = LocalCityGmlObjectProjection.ProjectCityObject(
            cityObject,
            origin,
            globalCartesian: new GeographicLib.LocalCartesian(origin.Latitude, origin.Longitude, origin.Altitude, referenceSystem.Geocentric),
            demTerrainTextureOverlay: overlay,
            materialResolver: new DefaultMaterialResolver(CommonMaterialCatalog.Create()));

        MaterialBinding material = Assert.Single(projected.Materials);
        Assert.Same(overlay, material.TerrainOverlay);
        Assert.Equal(MaterialProjection.Uv, material.Projection);
        Assert.Null(material.Family);
    }

    [Fact]
    public void ProjectParsedCityObjectKeepsFlatLod1TopAsTerrainOverlayMaterial()
    {
        CoordinateReferenceSystem referenceSystem = CoordinateReferenceSystem.Parse("http://www.opengis.net/def/crs/EPSG/0/6697");
        TerrainTextureOverlay overlay = CreateThirdMeshOverlay("53394525");
        GeodeticPoint origin = CreateMeshCenterPoint("53394525", altitudeMeters: 0.0);
        ParsedSurface topSurface = CreateParsedSurface(
            "flat-lod1-top",
            ParsedSurfaceSemantic.OuterCeiling,
            CreateMeshRelativeQuadVertices("53394525", altitudeMeters: 8.0, minRatio: 0.45, maxRatio: 0.55, reverseWinding: true),
            texturePayload: null);
        ParsedSurface bottomSurface = CreateParsedSurface(
            "flat-lod1-bottom",
            ParsedSurfaceSemantic.Ground,
            CreateMeshRelativeQuadVertices("53394525", altitudeMeters: 0.0, minRatio: 0.45, maxRatio: 0.55, reverseWinding: false),
            texturePayload: null);
        ParsedSurface wallSurface = CreateParsedSurface(
            "flat-lod1-wall",
            ParsedSurfaceSemantic.Wall,
            CreateMeshEdgeWallVertices("53394525", altitudeMeters: 0.0, heightMeters: 8.0, ratio: 0.45),
            texturePayload: null);
        ParsedCityObject cityObject = CreateParsedCityObject(
            "bldg",
            [bottomSurface, wallSurface, topSurface],
            referenceSystem,
            buildingAttributes: CreateBuildingAttributes(CityGmlRoofShape.Flat));
        PlateauImportRequest request = new(
            Dataset: "tokyo23ku",
            MeshCode: "53394525",
            CityGmlSource: DatasetLocation.Local("/tmp/plateau"));

        ImportedCityObject[] projected = LocalCityGmlObjectProjection.ProjectParsedCityObject(
            cityObject,
            origin,
            globalCartesian: new GeographicLib.LocalCartesian(origin.Latitude, origin.Longitude, origin.Altitude, referenceSystem.Geocentric),
            demTerrainTextureOverlays: [overlay],
            requestedMeshCodeBounds: [MeshCodeBounds.TryParse("53394525")!],
            terrainHeightSampler: null,
            request,
            new DefaultMaterialResolver(CommonMaterialCatalog.Create())).ToArray();

        Assert.Contains(projected, cityObject => cityObject.Materials.Any(material => ReferenceEquals(overlay, material.TerrainOverlay)));
        Assert.DoesNotContain(projected.SelectMany(static cityObject => cityObject.Materials), static material => material.Family == BundledDefaultMaterialFamilies.Roof);
        Assert.DoesNotContain(projected.SelectMany(static cityObject => cityObject.Materials), static material => material.Projection == MaterialProjection.Triplanar);
    }

    [Fact]
    public void ProjectParsedCityObjectKeepsGeneratedShedHighWallAsFacadeWithTerrainOverlayMaterial()
    {
        CoordinateReferenceSystem referenceSystem = CoordinateReferenceSystem.Parse("http://www.opengis.net/def/crs/EPSG/0/6697");
        TerrainTextureOverlay overlay = CreateThirdMeshOverlay("53394525");
        GeodeticPoint origin = CreateMeshCenterPoint("53394525", altitudeMeters: 0.0);
        ParsedSurface topSurface = CreateParsedSurface(
            "parsed-shed-lod1-top",
            ParsedSurfaceSemantic.Roof,
            CreateMeshRelativeQuadVertices("53394525", altitudeMeters: 8.0, minRatio: 0.45, maxRatio: 0.55, reverseWinding: true),
            texturePayload: null);
        ParsedSurface bottomSurface = CreateParsedSurface(
            "parsed-shed-lod1-bottom",
            ParsedSurfaceSemantic.Ground,
            CreateMeshRelativeQuadVertices("53394525", altitudeMeters: 0.0, minRatio: 0.45, maxRatio: 0.55, reverseWinding: false),
            texturePayload: null);
        ParsedSurface wallSurface = CreateParsedSurface(
            "parsed-shed-wall",
            ParsedSurfaceSemantic.Wall,
            CreateMeshEdgeWallVertices("53394525", altitudeMeters: 0.0, heightMeters: 8.0, ratio: 0.45),
            texturePayload: null);
        ParsedCityObject cityObject = CreateParsedCityObject(
            "bldg",
            [bottomSurface, wallSurface, topSurface],
            referenceSystem,
            buildingAttributes: CreateBuildingAttributes(CityGmlRoofShape.Shed));
        PlateauImportRequest request = new(
            Dataset: "tokyo23ku",
            MeshCode: "53394525",
            CityGmlSource: DatasetLocation.Local("/tmp/plateau"));

        ImportedCityObject[] projected = LocalCityGmlObjectProjection.ProjectParsedCityObject(
            cityObject,
            origin,
            globalCartesian: new GeographicLib.LocalCartesian(origin.Latitude, origin.Longitude, origin.Altitude, referenceSystem.Geocentric),
            demTerrainTextureOverlays: [overlay],
            requestedMeshCodeBounds: [MeshCodeBounds.TryParse("53394525")!],
            terrainHeightSampler: null,
            request,
            new DefaultMaterialResolver(CommonMaterialCatalog.Create())).ToArray();

        ImportedCityObject facadeObject = Assert.Single(projected, static cityObject =>
            cityObject.Materials.Any(IsBuildingFacadeMaterial));
        Assert.True(facadeObject.Mesh.Vertices.Max(static vertex => vertex.Position.Y) > 8.25);
        Assert.Contains(projected, cityObject => cityObject.Materials.Any(material => ReferenceEquals(overlay, material.TerrainOverlay)));
        Assert.DoesNotContain(projected.SelectMany(static cityObject => cityObject.Materials), static material => material.Family == BundledDefaultMaterialFamilies.Roof);
        Assert.DoesNotContain(projected.SelectMany(static cityObject => cityObject.Materials), static material => material.Projection == MaterialProjection.Triplanar);
    }

    [Fact]
    public void ProjectParsedCityObjectUsesTerrainOverlayMaterialForTexturelessBuildingRoof()
    {
        CoordinateReferenceSystem referenceSystem = CoordinateReferenceSystem.Parse("http://www.opengis.net/def/crs/EPSG/0/6697");
        TerrainTextureOverlay overlay = CreateThirdMeshOverlay("53394525");
        GeodeticPoint origin = CreateMeshCenterPoint("53394525", altitudeMeters: 8.0);
        ParsedSurface roofSurface = CreateParsedSurface(
            "textureless-roof",
            ParsedSurfaceSemantic.Roof,
            CreateMeshRelativeQuadVertices("53394525", altitudeMeters: 8.0, minRatio: 0.45, maxRatio: 0.55, reverseWinding: true),
            texturePayload: null);
        ParsedCityObject cityObject = CreateParsedCityObject("bldg", [roofSurface], referenceSystem);
        PlateauImportRequest request = new(
            Dataset: "tokyo23ku",
            MeshCode: "53394525",
            CityGmlSource: DatasetLocation.Local("/tmp/plateau"));

        ImportedCityObject projected = Assert.Single(LocalCityGmlObjectProjection.ProjectParsedCityObject(
            cityObject,
            origin,
            globalCartesian: null,
            demTerrainTextureOverlays: [overlay],
            requestedMeshCodeBounds: [MeshCodeBounds.TryParse("53394525")!],
            terrainHeightSampler: null,
            request,
            new DefaultMaterialResolver(CommonMaterialCatalog.Create())));

        MaterialBinding material = Assert.Single(projected.Materials);
        Assert.Same(overlay, material.TerrainOverlay);
        Assert.Equal("53394525", material.TerrainMeshCode);
        Assert.Equal(TextureSourceKind.Dataset, material.TextureSourceKind);
    }

    [Fact]
    public void ProjectParsedCityObjectChoosesOverlappingThirdMeshOverlayForParentMeshBuildingRoof()
    {
        CoordinateReferenceSystem referenceSystem = CoordinateReferenceSystem.Parse("http://www.opengis.net/def/crs/EPSG/0/6697");
        TerrainTextureOverlay unrelatedFirstOverlay = CreateThirdMeshOverlay("53394525");
        TerrainTextureOverlay expectedOverlay = CreateThirdMeshOverlay("53394526");
        GeodeticPoint origin = CreateMeshCenterPoint("53394526", altitudeMeters: 8.0);
        ParsedSurface roofSurface = CreateParsedSurface(
            "textureless-parent-roof",
            ParsedSurfaceSemantic.Roof,
            CreateMeshRelativeQuadVertices("53394526", altitudeMeters: 8.0, minRatio: 0.45, maxRatio: 0.55, reverseWinding: true),
            texturePayload: null);
        ParsedCityObject cityObject = CreateParsedCityObject("bldg", [roofSurface], referenceSystem) with
        {
            ActualMeshCode = "533945",
        };
        PlateauImportRequest request = new(
            Dataset: "tokyo23ku",
            MeshCode: "533945",
            CityGmlSource: DatasetLocation.Local("/tmp/plateau"));

        ImportedCityObject projected = Assert.Single(LocalCityGmlObjectProjection.ProjectParsedCityObject(
            cityObject,
            origin,
            globalCartesian: null,
            demTerrainTextureOverlays: [unrelatedFirstOverlay, expectedOverlay],
            requestedMeshCodeBounds: [MeshCodeBounds.TryParse("533945")!],
            terrainHeightSampler: null,
            request,
            new DefaultMaterialResolver(CommonMaterialCatalog.Create())));

        MaterialBinding material = Assert.Single(projected.Materials);
        Assert.Same(expectedOverlay, material.TerrainOverlay);
        Assert.Equal("53394526", material.TerrainMeshCode);
    }

    [Fact]
    public void ProjectParsedCityObjectSplitsParentMeshBuildingRoofsByThirdMeshOverlay()
    {
        CoordinateReferenceSystem referenceSystem = CoordinateReferenceSystem.Parse("http://www.opengis.net/def/crs/EPSG/0/6697");
        TerrainTextureOverlay firstOverlay = CreateThirdMeshOverlay("53394525");
        TerrainTextureOverlay secondOverlay = CreateThirdMeshOverlay("53394526");
        GeodeticPoint origin = CreateMeshCenterPoint("53394525", altitudeMeters: 8.0);
        ParsedSurface firstRoofSurface = CreateParsedSurface(
            "textureless-parent-roof-first",
            ParsedSurfaceSemantic.Roof,
            CreateMeshRelativeQuadVertices("53394525", altitudeMeters: 8.0, minRatio: 0.45, maxRatio: 0.55, reverseWinding: true),
            texturePayload: null);
        ParsedSurface secondRoofSurface = CreateParsedSurface(
            "textureless-parent-roof-second",
            ParsedSurfaceSemantic.Roof,
            CreateMeshRelativeQuadVertices("53394526", altitudeMeters: 8.0, minRatio: 0.45, maxRatio: 0.55, reverseWinding: true),
            texturePayload: null);
        ParsedCityObject cityObject = CreateParsedCityObject("bldg", [firstRoofSurface, secondRoofSurface], referenceSystem) with
        {
            ActualMeshCode = "533945",
        };
        PlateauImportRequest request = new(
            Dataset: "tokyo23ku",
            MeshCode: "533945",
            CityGmlSource: DatasetLocation.Local("/tmp/plateau"));

        ImportedCityObject[] projected = LocalCityGmlObjectProjection.ProjectParsedCityObject(
            cityObject,
            origin,
            globalCartesian: null,
            demTerrainTextureOverlays: [firstOverlay, secondOverlay],
            requestedMeshCodeBounds: [MeshCodeBounds.TryParse("533945")!],
            terrainHeightSampler: null,
            request,
            new DefaultMaterialResolver(CommonMaterialCatalog.Create())).ToArray();

        Assert.Equal(2, projected.Length);
        Assert.Collection(
            projected.OrderBy(static cityObject => cityObject.Materials.Single().TerrainMeshCode, StringComparer.Ordinal),
            first =>
            {
                MaterialBinding material = Assert.Single(first.Materials);
                Assert.Same(firstOverlay, material.TerrainOverlay);
                Assert.Equal("53394525", material.TerrainMeshCode);
            },
            second =>
            {
                MaterialBinding material = Assert.Single(second.Materials);
                Assert.Same(secondOverlay, material.TerrainOverlay);
                Assert.Equal("53394526", material.TerrainMeshCode);
            });
    }

    [Fact]
    public void ProjectParsedCityObjectAllowsSelectedAdjacentDemOverlayWhenRequestMeshIsExact()
    {
        CoordinateReferenceSystem referenceSystem = CoordinateReferenceSystem.Parse("http://www.opengis.net/def/crs/EPSG/0/6697");
        TerrainTextureOverlay selectedRequestOverlay = CreateThirdMeshOverlay("53394525");
        TerrainTextureOverlay selectedAdjacentOverlay = CreateThirdMeshOverlay("53394526");
        GeodeticPoint origin = CreateMeshCenterPoint("53394526", altitudeMeters: 8.0);
        ParsedSurface demSurface = CreateParsedSurface(
            "selected-adjacent-dem",
            ParsedSurfaceSemantic.Roof,
            CreateMeshRelativeQuadVertices("53394526", altitudeMeters: 8.0, minRatio: 0.45, maxRatio: 0.55, reverseWinding: true),
            texturePayload: null) with
        {
        };
        ParsedCityObject cityObject = CreateParsedCityObject("dem", [demSurface], referenceSystem) with
        {
            ActualMeshCode = "53394526",
        };
        PlateauImportRequest request = new(
            Dataset: "tokyo23ku",
            MeshCode: "53394525",
            CityGmlSource: DatasetLocation.Local("/tmp/plateau"),
            PackageNames: ["dem"],
            TerrainMeshMode: TerrainMeshMode.Grid);

        ImportedCityObject projected = Assert.Single(LocalCityGmlObjectProjection.ProjectParsedCityObject(
            cityObject,
            origin,
            globalCartesian: null,
            demTerrainTextureOverlays: [selectedRequestOverlay, selectedAdjacentOverlay],
            requestedMeshCodeBounds: [MeshCodeBounds.TryParse("53394525")!, MeshCodeBounds.TryParse("53394526")!],
            terrainHeightSampler: null,
            request,
            new DefaultMaterialResolver(CommonMaterialCatalog.Create())));

        MaterialBinding material = Assert.Single(projected.Materials);
        Assert.Same(selectedAdjacentOverlay, material.TerrainOverlay);
        Assert.Equal("53394526", material.TerrainMeshCode);
    }

    [Fact]
    public void ProjectParsedCityObjectUsesConcreteRequestedMeshCodeBoundsForRegexDemGridOverlay()
    {
        CoordinateReferenceSystem referenceSystem = CoordinateReferenceSystem.Parse("http://www.opengis.net/def/crs/EPSG/0/6697");
        TerrainTextureOverlay selectedOverlay = CreateThirdMeshOverlay("53394600");
        GeodeticPoint origin = CreateMeshCenterPoint("53394600", altitudeMeters: 8.0);
        ParsedSurface demSurface = CreateParsedSurface(
            "regex-selected-dem",
            ParsedSurfaceSemantic.Roof,
            CreateMeshRelativeQuadVertices("53394600", altitudeMeters: 8.0, minRatio: 0.45, maxRatio: 0.55, reverseWinding: true),
            texturePayload: null) with
        {
        };
        ParsedCityObject cityObject = CreateParsedCityObject("dem", [demSurface], referenceSystem) with
        {
            ActualMeshCode = "533945",
        };
        PlateauImportRequest request = new(
            Dataset: "tokyo23ku",
            MeshCode: "53394[56]..",
            CityGmlSource: DatasetLocation.Local("/tmp/plateau"),
            PackageNames: ["dem"],
            TerrainMeshMode: TerrainMeshMode.Grid);

        ImportedCityObject projected = Assert.Single(LocalCityGmlObjectProjection.ProjectParsedCityObject(
            cityObject,
            origin,
            globalCartesian: null,
            demTerrainTextureOverlays: [selectedOverlay],
            requestedMeshCodeBounds: [MeshCodeBounds.TryParse("53394600")!],
            terrainHeightSampler: null,
            request,
            new DefaultMaterialResolver(CommonMaterialCatalog.Create())));

        MaterialBinding material = Assert.Single(projected.Materials);
        Assert.Same(selectedOverlay, material.TerrainOverlay);
        Assert.Equal("53394600", material.TerrainMeshCode);
    }

    [Fact]
    public void ProjectParsedCityObjectUsesSourceOverlayForTexturelessBuildingRoofOutsideSourceBounds()
    {
        CoordinateReferenceSystem referenceSystem = CoordinateReferenceSystem.Parse("http://www.opengis.net/def/crs/EPSG/0/6697");
        TerrainTextureOverlay selectedRequestOverlay = CreateThirdMeshOverlay("53394525");
        TerrainTextureOverlay selectedAdjacentOverlay = CreateThirdMeshOverlay("53394526");
        GeodeticPoint origin = CreateMeshCenterPoint("53394526", altitudeMeters: 8.0);
        ParsedSurface roofSurface = CreateParsedSurface(
            "selected-adjacent-textureless-roof",
            ParsedSurfaceSemantic.Roof,
            CreateMeshRelativeQuadVertices("53394526", altitudeMeters: 8.0, minRatio: 0.45, maxRatio: 0.55, reverseWinding: true),
            texturePayload: null);
        ParsedCityObject cityObject = CreateParsedCityObject("bldg", [roofSurface], referenceSystem) with
        {
            ActualMeshCode = "53394525",
        };
        PlateauImportRequest request = new(
            Dataset: "tokyo23ku",
            MeshCode: "53394525",
            CityGmlSource: DatasetLocation.Local("/tmp/plateau"),
            PackageNames: ["dem", "bldg"]);

        ImportedCityObject projected = Assert.Single(LocalCityGmlObjectProjection.ProjectParsedCityObject(
            cityObject,
            origin,
            globalCartesian: null,
            demTerrainTextureOverlays: [selectedRequestOverlay, selectedAdjacentOverlay],
            requestedMeshCodeBounds: [MeshCodeBounds.TryParse("53394525")!, MeshCodeBounds.TryParse("53394526")!],
            terrainHeightSampler: null,
            request,
            new DefaultMaterialResolver(CommonMaterialCatalog.Create())));

        MaterialBinding material = Assert.Single(projected.Materials);
        Assert.Same(selectedRequestOverlay, material.TerrainOverlay);
        Assert.Equal("53394525", material.TerrainMeshCode);
    }

    [Fact]
    public void ProjectParsedCityObjectUsesSourceMeshOverlayBeforeActualMeshOverlay()
    {
        CoordinateReferenceSystem referenceSystem = CoordinateReferenceSystem.Parse("http://www.opengis.net/def/crs/EPSG/0/6697");
        TerrainTextureOverlay sourceOverlay = CreateThirdMeshOverlay("53394525");
        TerrainTextureOverlay actualOverlay = CreateThirdMeshOverlay("53394526");
        GeodeticPoint origin = CreateMeshCenterPoint("53394526", altitudeMeters: 8.0);
        ParsedSurface roofSurface = CreateParsedSurface(
            "actual-mesh-textureless-roof",
            ParsedSurfaceSemantic.Roof,
            CreateMeshRelativeQuadVertices("53394526", altitudeMeters: 8.0, minRatio: 0.45, maxRatio: 0.55, reverseWinding: true),
            texturePayload: null);
        ParsedCityObject cityObject = CreateParsedCityObject("bldg", [roofSurface], referenceSystem) with
        {
            ActualMeshCode = "53394526",
            SourceMeshCode = "53394525",
        };
        PlateauImportRequest request = new(
            Dataset: "tokyo23ku",
            MeshCode: "53394525",
            CityGmlSource: DatasetLocation.Local("/tmp/plateau"),
            PackageNames: ["dem", "bldg"]);

        ImportedCityObject projected = Assert.Single(LocalCityGmlObjectProjection.ProjectParsedCityObject(
            cityObject,
            origin,
            globalCartesian: null,
            demTerrainTextureOverlays: [sourceOverlay, actualOverlay],
            requestedMeshCodeBounds: [MeshCodeBounds.TryParse("53394525")!, MeshCodeBounds.TryParse("53394526")!],
            terrainHeightSampler: null,
            request,
            new DefaultMaterialResolver(CommonMaterialCatalog.Create())));

        MaterialBinding material = Assert.Single(projected.Materials);
        Assert.Same(sourceOverlay, material.TerrainOverlay);
        Assert.Equal("53394525", material.TerrainMeshCode);
    }

    [Fact]
    public void ProjectParsedCityObjectDoesNotUseUnrelatedParentSourceOverlayForTexturelessBuildingRoof()
    {
        CoordinateReferenceSystem referenceSystem = CoordinateReferenceSystem.Parse("http://www.opengis.net/def/crs/EPSG/0/6697");
        TerrainTextureOverlay unrelatedSourceParentOverlay = CreateThirdMeshOverlay("53394525");
        GeodeticPoint origin = CreateMeshCenterPoint("53394526", altitudeMeters: 8.0);
        ParsedSurface roofSurface = CreateParsedSurface(
            "parent-source-partial-coverage-roof",
            ParsedSurfaceSemantic.Roof,
            CreateMeshRelativeQuadVertices("53394526", altitudeMeters: 8.0, minRatio: 0.45, maxRatio: 0.55, reverseWinding: true),
            texturePayload: null);
        ParsedCityObject cityObject = CreateParsedCityObject("bldg", [roofSurface], referenceSystem) with
        {
            ActualMeshCode = "53394526",
            SourceMeshCode = "533945",
        };
        PlateauImportRequest request = new(
            Dataset: "tokyo23ku",
            MeshCode: "533945",
            CityGmlSource: DatasetLocation.Local("/tmp/plateau"),
            PackageNames: ["dem", "bldg"]);

        ImportedCityObject projected = Assert.Single(LocalCityGmlObjectProjection.ProjectParsedCityObject(
            cityObject,
            origin,
            globalCartesian: null,
            demTerrainTextureOverlays: [unrelatedSourceParentOverlay],
            requestedMeshCodeBounds: [MeshCodeBounds.TryParse("533945")!],
            terrainHeightSampler: null,
            request,
            new DefaultMaterialResolver(CommonMaterialCatalog.Create())));

        Assert.DoesNotContain(projected.Materials, static material => material.TerrainOverlay is not null);
    }

    [Fact]
    public void ProjectParsedCityObjectDoesNotUseUnrequestedTerrainOverlay()
    {
        CoordinateReferenceSystem referenceSystem = CoordinateReferenceSystem.Parse("http://www.opengis.net/def/crs/EPSG/0/6697");
        TerrainTextureOverlay unrequestedOverlay = CreateThirdMeshOverlay("53394526");
        GeodeticPoint origin = CreateMeshCenterPoint("53394526", altitudeMeters: 8.0);
        ParsedSurface roofSurface = CreateParsedSurface(
            "unrequested-textureless-roof",
            ParsedSurfaceSemantic.Roof,
            CreateMeshRelativeQuadVertices("53394526", altitudeMeters: 8.0, minRatio: 0.45, maxRatio: 0.55, reverseWinding: true),
            texturePayload: null);
        ParsedCityObject cityObject = CreateParsedCityObject("bldg", [roofSurface], referenceSystem) with
        {
            ActualMeshCode = "53394525",
        };
        PlateauImportRequest request = new(
            Dataset: "tokyo23ku",
            MeshCode: "53394525",
            CityGmlSource: DatasetLocation.Local("/tmp/plateau"),
            PackageNames: ["dem", "bldg"]);

        ImportedCityObject[] cityObjects =
            LocalCityGmlObjectProjection.ProjectParsedCityObject(
                cityObject,
                origin,
                globalCartesian: null,
                demTerrainTextureOverlays: [unrequestedOverlay],
                requestedMeshCodeBounds: [MeshCodeBounds.TryParse("53394525")!],
                terrainHeightSampler: null,
                request,
                new DefaultMaterialResolver(CommonMaterialCatalog.Create())).ToArray();

        ImportedCityObject projectedCityObject = Assert.Single(cityObjects);
        Assert.All(projectedCityObject.Materials, static material => Assert.Null(material.TerrainOverlayMaterial));
    }

    [Fact]
    public void EnumerateCommonMaterialsForParsedCityObjectExcludesTerrainOverlayBuildingRoofs()
    {
        CoordinateReferenceSystem referenceSystem = CoordinateReferenceSystem.Parse("http://www.opengis.net/def/crs/EPSG/0/6697");
        TerrainTextureOverlay firstOverlay = CreateThirdMeshOverlay("53394525");
        TerrainTextureOverlay secondOverlay = CreateThirdMeshOverlay("53394526");
        ParsedSurface firstRoofSurface = CreateParsedSurface(
            "textureless-parent-common-roof-first",
            ParsedSurfaceSemantic.Roof,
            CreateMeshRelativeQuadVertices("53394525", altitudeMeters: 8.0, minRatio: 0.45, maxRatio: 0.55, reverseWinding: true),
            texturePayload: null);
        ParsedSurface secondRoofSurface = CreateParsedSurface(
            "textureless-parent-common-roof-second",
            ParsedSurfaceSemantic.Roof,
            CreateMeshRelativeQuadVertices("53394526", altitudeMeters: 8.0, minRatio: 0.45, maxRatio: 0.55, reverseWinding: true),
            texturePayload: null);
        ParsedCityObject cityObject = CreateParsedCityObject("bldg", [firstRoofSurface, secondRoofSurface], referenceSystem) with
        {
            ActualMeshCode = "533945",
        };
        PlateauImportRequest request = new(
            Dataset: "tokyo23ku",
            MeshCode: "533945",
            CityGmlSource: DatasetLocation.Local("/tmp/plateau"));

        MaterialBinding[] materialBindings = LocalCityGmlObjectProjection.EnumerateCommonMaterialsForParsedCityObject(
            cityObject,
            CreateMeshCenterPoint("53394525", altitudeMeters: 8.0),
            globalCartesian: null,
            demTerrainTextureOverlays: [firstOverlay, secondOverlay],
            requestedMeshCodeBounds: [MeshCodeBounds.TryParse("533945")!],
            terrainHeightSampler: null,
            request,
            new DefaultMaterialResolver(CommonMaterialCatalog.Create())).ToArray();

        Assert.Empty(materialBindings);
    }

    [Fact]
    public void EnumerateCommonMaterialsForParsedCityObjectExcludesTerrainOverlayDemGridForRegexMeshSelector()
    {
        CoordinateReferenceSystem referenceSystem = CoordinateReferenceSystem.Parse("http://www.opengis.net/def/crs/EPSG/0/6697");
        TerrainTextureOverlay overlay = CreateThirdMeshOverlay("53394525");
        ParsedSurface demSurface = CreateParsedSurface(
            "regex-dem-grid-terrain-overlay",
            ParsedSurfaceSemantic.Roof,
            CreateMeshRelativeQuadVertices("53394525", altitudeMeters: 8.0, minRatio: 0.45, maxRatio: 0.55, reverseWinding: true),
            texturePayload: null) with
        {
        };
        ParsedCityObject cityObject = CreateParsedCityObject("dem", [demSurface], referenceSystem) with
        {
            ActualMeshCode = "533945",
        };
        PlateauImportRequest request = new(
            Dataset: "tokyo23ku",
            MeshCode: ".*",
            CityGmlSource: DatasetLocation.Local("/tmp/plateau"),
            PackageNames: ["dem"],
            TerrainMeshMode: TerrainMeshMode.Grid);

        MaterialBinding[] materialBindings = LocalCityGmlObjectProjection.EnumerateCommonMaterialsForParsedCityObject(
            cityObject,
            CreateMeshCenterPoint("53394525", altitudeMeters: 8.0),
            globalCartesian: null,
            demTerrainTextureOverlays: [overlay],
            requestedMeshCodeBounds: [],
            terrainHeightSampler: null,
            request,
            new DefaultMaterialResolver(CommonMaterialCatalog.Create())).ToArray();

        Assert.Empty(materialBindings);
    }

    [Fact]
    public void ProjectParsedCityObjectUsesTypedMeshCodeForDemOverlayMaterial()
    {
        CoordinateReferenceSystem referenceSystem = CoordinateReferenceSystem.Parse("http://www.opengis.net/def/crs/EPSG/0/6697");
        IReadOnlyList<GeodeticPoint> vertices =
            CreateMeshRelativeQuadVertices("53394525", altitudeMeters: 8.0, minRatio: 0.45, maxRatio: 0.55, reverseWinding: true);
        GeographicRectangle nonThirdMeshBounds = new(
            vertices.Min(static vertex => vertex.Latitude),
            vertices.Max(static vertex => vertex.Latitude),
            vertices.Min(static vertex => vertex.Longitude),
            vertices.Max(static vertex => vertex.Longitude));
        TerrainTextureOverlay overlay = new(
            PackageName: "dem",
            MeshCode: ThirdRegionalMeshCode.Parse("53394525"),
            UrlTemplate: "https://terrain.example/non-third/{z}/{x}/{y}.png",
            ZoomLevel: 17,
            GeographicBounds: nonThirdMeshBounds,
            MaxTextureSize: 512);
        ParsedSurface demSurface = new(
            Semantic: ParsedSurfaceSemantic.Ground,
            ExteriorRing: new ParsedRing(vertices.ToArray(), UVs: null),
            InteriorRings: [],
            BaseColor: new ColorRgba(1.0, 1.0, 1.0, 1.0),
            TexturePayload: null);
        ParsedCityObject cityObject = CreateParsedCityObject("dem", [demSurface], referenceSystem);
        PlateauImportRequest request = new(
            Dataset: "tokyo23ku",
            MeshCode: "53394525",
            CityGmlSource: DatasetLocation.Local("/tmp/plateau"));

        ImportedCityObject projected = Assert.Single(
            LocalCityGmlObjectProjection.ProjectParsedCityObject(
                cityObject,
                CreateMeshCenterPoint("53394525", altitudeMeters: 8.0),
                globalCartesian: null,
                demTerrainTextureOverlays: [overlay],
                requestedMeshCodeBounds: [MeshCodeBounds.TryParse("53394525")!],
                terrainHeightSampler: null,
                request,
                new DefaultMaterialResolver(CommonMaterialCatalog.Create())));

        MaterialBinding material = Assert.Single(projected.Materials);
        Assert.Same(overlay, material.TerrainOverlay);
        Assert.Equal("53394525", material.TerrainMeshCode);
    }

    [Fact]
    public void ProjectParsedCityObjectUsesTypedMeshCodeForBuildingOverlayMaterial()
    {
        CoordinateReferenceSystem referenceSystem = CoordinateReferenceSystem.Parse("http://www.opengis.net/def/crs/EPSG/0/6697");
        IReadOnlyList<GeodeticPoint> vertices =
            CreateMeshRelativeQuadVertices("53394525", altitudeMeters: 8.0, minRatio: 0.45, maxRatio: 0.55, reverseWinding: true);
        GeographicRectangle nonThirdMeshBounds = new(
            vertices.Min(static vertex => vertex.Latitude),
            vertices.Max(static vertex => vertex.Latitude),
            vertices.Min(static vertex => vertex.Longitude),
            vertices.Max(static vertex => vertex.Longitude));
        TerrainTextureOverlay overlay = new(
            PackageName: "dem",
            MeshCode: ThirdRegionalMeshCode.Parse("53394525"),
            UrlTemplate: "https://terrain.example/non-third-building/{z}/{x}/{y}.png",
            ZoomLevel: 17,
            GeographicBounds: nonThirdMeshBounds,
            MaxTextureSize: 512);
        ParsedSurface roofSurface = CreateParsedSurface(
            "textureless-roof",
            ParsedSurfaceSemantic.Roof,
            vertices,
            texturePayload: null);
        ParsedCityObject cityObject = CreateParsedCityObject("bldg", [roofSurface], referenceSystem);
        PlateauImportRequest request = new(
            Dataset: "tokyo23ku",
            MeshCode: "53394525",
            CityGmlSource: DatasetLocation.Local("/tmp/plateau"));

        ImportedCityObject projected = Assert.Single(
            LocalCityGmlObjectProjection.ProjectParsedCityObject(
                cityObject,
                CreateMeshCenterPoint("53394525", altitudeMeters: 8.0),
                globalCartesian: null,
                demTerrainTextureOverlays: [overlay],
                requestedMeshCodeBounds: [MeshCodeBounds.TryParse("53394525")!],
                terrainHeightSampler: null,
                request,
                new DefaultMaterialResolver(CommonMaterialCatalog.Create())));

        MaterialBinding material = Assert.Single(projected.Materials);
        Assert.Same(overlay, material.TerrainOverlay);
        Assert.Equal("53394525", material.TerrainMeshCode);
    }

    [Fact]
    public void ProjectCityObjectUsesPerObjectAlbedoOnlyMaterialForTexturelessDemTerrainRoofs()
    {
        CoordinateReferenceSystem referenceSystem = CoordinateReferenceSystem.Parse("http://www.opengis.net/def/crs/EPSG/0/6697");
        TerrainTextureOverlay overlay = CreateThirdMeshOverlay("53394525");
        GeodeticPoint origin = CreateMeshCenterPoint("53394525", altitudeMeters: 8.0);
        GeographicLib.LocalCartesian cartesian = new(origin.Latitude, origin.Longitude, origin.Altitude, referenceSystem.Geocentric);
        ParsedSurface redRoofSurface = CreateParsedSurface(
            "red-textureless-roof",
            ParsedSurfaceSemantic.Roof,
            CreateMeshRelativeQuadVertices("53394525", altitudeMeters: 8.0, minRatio: 0.35, maxRatio: 0.45, reverseWinding: true),
            texturePayload: null,
            baseColor: new ColorRgba(1.0, 0.0, 0.0, 1.0));
        ParsedSurface blueRoofSurface = CreateParsedSurface(
            "blue-textureless-roof",
            ParsedSurfaceSemantic.Roof,
            CreateMeshRelativeQuadVertices("53394525", altitudeMeters: 8.0, minRatio: 0.55, maxRatio: 0.65, reverseWinding: true),
            texturePayload: null,
            baseColor: new ColorRgba(0.0, 0.0, 1.0, 1.0));
        ParsedCityObject cityObject = CreateParsedCityObject("bldg", [redRoofSurface, blueRoofSurface], referenceSystem);

        ImportedCityObject projected = LocalCityGmlObjectProjection.ProjectCityObject(
            cityObject,
            origin,
            globalCartesian: cartesian,
            demTerrainTextureOverlay: overlay,
            materialResolver: new DefaultMaterialResolver(CommonMaterialCatalog.Create()));

        MaterialBinding material = Assert.Single(projected.Materials);
        Assert.Equal(new ColorRgba(1.0, 1.0, 1.0, 1.0), material.BaseColor);
        Assert.Equal(TextureSourceKind.Dataset, material.TextureSourceKind);
        Assert.Equal(MaterialReuseScope.PerObject, material.ReuseScope);
        Assert.Same(overlay, material.TerrainOverlay);
        Assert.Equal(CommonMaterialCatalog.Create().Generic.Uv, material.CommonMaterial);
    }

    [Fact]
    public void ProjectCityObjectUsesProvidedTerrainOverlayMeshCodeForTexturelessRoof()
    {
        CoordinateReferenceSystem referenceSystem = CoordinateReferenceSystem.Parse("http://www.opengis.net/def/crs/EPSG/0/6697");
        TerrainTextureOverlay mismatchedOverlay = CreateThirdMeshOverlay("53394526");
        GeodeticPoint origin = CreateMeshCenterPoint("53394525", altitudeMeters: 8.0);
        GeographicLib.LocalCartesian cartesian = new(origin.Latitude, origin.Longitude, origin.Altitude, referenceSystem.Geocentric);
        ParsedSurface roofSurface = CreateParsedSurface(
            "mismatched-overlay-roof",
            ParsedSurfaceSemantic.Roof,
            CreateMeshRelativeQuadVertices("53394525", altitudeMeters: 8.0, minRatio: 0.45, maxRatio: 0.55, reverseWinding: true),
            texturePayload: null);
        ParsedCityObject cityObject = CreateParsedCityObject("bldg", [roofSurface], referenceSystem);

        ImportedCityObject projected = LocalCityGmlObjectProjection.ProjectCityObject(
            cityObject,
            origin,
            globalCartesian: cartesian,
            demTerrainTextureOverlay: mismatchedOverlay,
            materialResolver: new DefaultMaterialResolver(CommonMaterialCatalog.Create()));

        MaterialBinding material = Assert.Single(projected.Materials);
        Assert.Same(mismatchedOverlay, material.TerrainOverlay);
        Assert.Equal("53394526", material.TerrainMeshCode);
        Assert.Equal(TextureSourceKind.Dataset, material.TextureSourceKind);
    }

    [Fact]
    public void ProjectCityObjectAssignsDemTerrainMaterialToTexturelessUnknownUpwardHorizontalBuildingSurface()
    {
        CoordinateReferenceSystem referenceSystem = CoordinateReferenceSystem.Parse("http://www.opengis.net/def/crs/EPSG/0/6697");
        TerrainTextureOverlay overlay = CreateThirdMeshOverlay("53394525");
        GeodeticPoint origin = CreateMeshCenterPoint("53394525", altitudeMeters: 8.0);
        GeographicLib.LocalCartesian cartesian = new(origin.Latitude, origin.Longitude, origin.Altitude, referenceSystem.Geocentric);
        ParsedSurface unknownSurface = CreateParsedSurface(
            "unknown-upward-roof",
            ParsedSurfaceSemantic.Unknown,
            CreateMeshRelativeQuadVertices("53394525", altitudeMeters: 8.0, minRatio: 0.45, maxRatio: 0.55, reverseWinding: true),
            texturePayload: null);
        ParsedSurface bottomSurface = CreateParsedSurface(
            "unknown-horizontal-bottom",
            ParsedSurfaceSemantic.Unknown,
            CreateMeshRelativeQuadVertices("53394525", altitudeMeters: 0.0, minRatio: 0.45, maxRatio: 0.55, reverseWinding: false),
            texturePayload: null);
        ParsedCityObject cityObject = CreateParsedCityObject("bldg", [bottomSurface, unknownSurface], referenceSystem);

        ImportedCityObject projected = LocalCityGmlObjectProjection.ProjectCityObject(
            cityObject,
            origin,
            globalCartesian: cartesian,
            demTerrainTextureOverlay: overlay,
            materialResolver: new DefaultMaterialResolver(CommonMaterialCatalog.Create()));

        MaterialBinding material = Assert.Single(projected.Materials);
        Assert.Equal(TextureSourceKind.Dataset, material.TextureSourceKind);
        Assert.Null(material.Family);
        Assert.Null(material.TexturePayload);
        Assert.Same(overlay, material.TerrainOverlay);
        Assert.Equal(CommonMaterialCatalog.Create().Generic.Uv, material.CommonMaterial);
    }

    [Fact]
    public void ProjectCityObjectAssignsDemTerrainMaterialToTexturelessUnknownHorizontalBuildingSurfaceRegardlessOfWinding()
    {
        CoordinateReferenceSystem referenceSystem = CoordinateReferenceSystem.Parse("http://www.opengis.net/def/crs/EPSG/0/6697");
        TerrainTextureOverlay overlay = CreateThirdMeshOverlay("53394525");
        GeodeticPoint origin = CreateMeshCenterPoint("53394525", altitudeMeters: 8.0);
        GeographicLib.LocalCartesian cartesian = new(origin.Latitude, origin.Longitude, origin.Altitude, referenceSystem.Geocentric);
        ParsedSurface unknownSurface = CreateParsedSurface(
            "unknown-horizontal-roof-source-winding",
            ParsedSurfaceSemantic.Unknown,
            CreateMeshRelativeQuadVertices("53394525", altitudeMeters: 8.0, minRatio: 0.45, maxRatio: 0.55, reverseWinding: false),
            texturePayload: null);
        ParsedSurface bottomSurface = CreateParsedSurface(
            "unknown-horizontal-bottom",
            ParsedSurfaceSemantic.Unknown,
            CreateMeshRelativeQuadVertices("53394525", altitudeMeters: 0.0, minRatio: 0.45, maxRatio: 0.55, reverseWinding: true),
            texturePayload: null);
        ParsedCityObject cityObject = CreateParsedCityObject("bldg", [bottomSurface, unknownSurface], referenceSystem);

        ImportedCityObject projected = LocalCityGmlObjectProjection.ProjectCityObject(
            cityObject,
            origin,
            globalCartesian: cartesian,
            demTerrainTextureOverlay: overlay,
            materialResolver: new DefaultMaterialResolver(CommonMaterialCatalog.Create()));

        MaterialBinding material = Assert.Single(projected.Materials);
        Assert.Equal(TextureSourceKind.Dataset, material.TextureSourceKind);
        Assert.Null(material.Family);
        Assert.Null(material.TexturePayload);
        Assert.Same(overlay, material.TerrainOverlay);
        Assert.Equal(CommonMaterialCatalog.Create().Generic.Uv, material.CommonMaterial);
    }

    [Fact]
    public void ProjectCityObjectDoesNotAssignDemTerrainMaterialToTexturelessUnknownHorizontalSurfaceAtBuildingBottom()
    {
        CoordinateReferenceSystem referenceSystem = CoordinateReferenceSystem.Parse("http://www.opengis.net/def/crs/EPSG/0/6697");
        TerrainTextureOverlay overlay = CreateThirdMeshOverlay("53394525");
        GeodeticPoint origin = CreateMeshCenterPoint("53394525", altitudeMeters: 8.0);
        GeographicLib.LocalCartesian cartesian = new(origin.Latitude, origin.Longitude, origin.Altitude, referenceSystem.Geocentric);
        ParsedSurface lowerHorizontalSurface = CreateParsedSurface(
            "unknown-horizontal-lower-surface",
            ParsedSurfaceSemantic.Unknown,
            CreateMeshRelativeQuadVertices("53394525", altitudeMeters: 8.0, minRatio: 0.45, maxRatio: 0.55, reverseWinding: false),
            texturePayload: null);
        ParsedSurface wallSurface = CreateParsedSurface(
            "building-wall-reaching-higher-altitude",
            ParsedSurfaceSemantic.Wall,
            CreateVerticalQuadVertices(origin, widthMeters: 5.0, heightMeters: 2.0),
            texturePayload: null);
        ParsedCityObject cityObject = CreateParsedCityObject("bldg", [lowerHorizontalSurface, wallSurface], referenceSystem);

        ImportedCityObject projected = LocalCityGmlObjectProjection.ProjectCityObject(
            cityObject,
            origin,
            globalCartesian: cartesian,
            demTerrainTextureOverlay: overlay,
            materialResolver: new DefaultMaterialResolver(CommonMaterialCatalog.Create()));

        Assert.DoesNotContain(projected.Materials, static material => material.TerrainOverlay is not null);
    }

    [Fact]
    public void ProjectCityObjectAssignsDemTerrainMaterialToTexturelessUnknownRoofBelowHigherBuildingDetail()
    {
        CoordinateReferenceSystem referenceSystem = CoordinateReferenceSystem.Parse("http://www.opengis.net/def/crs/EPSG/0/6697");
        TerrainTextureOverlay overlay = CreateThirdMeshOverlay("53394525");
        GeodeticPoint origin = CreateMeshCenterPoint("53394525", altitudeMeters: 0.0);
        GeographicLib.LocalCartesian cartesian = new(origin.Latitude, origin.Longitude, origin.Altitude, referenceSystem.Geocentric);
        ParsedSurface mainRoofSurface = CreateParsedSurface(
            "unknown-horizontal-main-roof",
            ParsedSurfaceSemantic.Unknown,
            CreateMeshRelativeQuadVertices("53394525", altitudeMeters: 20.0, minRatio: 0.45, maxRatio: 0.55, reverseWinding: false),
            texturePayload: null);
        ParsedSurface higherDetailSurface = CreateParsedSurface(
            "building-detail-reaching-higher-altitude",
            ParsedSurfaceSemantic.Wall,
            CreateVerticalQuadVertices(new(origin.Latitude, origin.Longitude, origin.Altitude), widthMeters: 5.0, heightMeters: 20.2),
            texturePayload: null);
        ParsedCityObject cityObject = CreateParsedCityObject(
            "bldg",
            [mainRoofSurface, higherDetailSurface],
            referenceSystem);

        ImportedCityObject projected = LocalCityGmlObjectProjection.ProjectCityObject(
            cityObject,
            origin,
            globalCartesian: cartesian,
            demTerrainTextureOverlay: overlay,
            materialResolver: new DefaultMaterialResolver(CommonMaterialCatalog.Create()));

        MaterialBinding roofMaterial = Assert.Single(
            projected.Materials,
            static material => material.TerrainOverlay is not null);
        Assert.Same(overlay, roofMaterial.TerrainOverlay);
    }

    [Fact]
    public void ProjectCityObjectAssignsDemTerrainMaterialToMatsumotoLod1SolidTopWinding()
    {
        CoordinateReferenceSystem referenceSystem = CoordinateReferenceSystem.Parse("http://www.opengis.net/def/crs/EPSG/0/6697");
        TerrainTextureOverlay overlay = CreateThirdMeshOverlay("54372778");
        GeodeticPoint origin = new(36.23163715441054, 137.97501759714283, 590.343);
        GeographicLib.LocalCartesian cartesian = new(origin.Latitude, origin.Longitude, origin.Altitude, referenceSystem.Geocentric);
        ParsedSurface bottomSurface = CreateParsedSurface(
            "matsumoto-lod1-solid-bottom",
            ParsedSurfaceSemantic.Unknown,
            CreateMatsumotoLod1SolidHorizontalRing(altitudeMeters: 590.343),
            texturePayload: null);
        ParsedSurface topSurface = CreateParsedSurface(
            "matsumoto-lod1-solid-top",
            ParsedSurfaceSemantic.Unknown,
            CreateMatsumotoLod1SolidHorizontalRing(altitudeMeters: 595.009),
            texturePayload: null);
        ParsedCityObject cityObject =
            CreateParsedCityObject(
                "bldg",
                [bottomSurface, topSurface],
                referenceSystem) with
            {
                ActualMeshCode = "54372778",
            };

        ImportedCityObject projected = LocalCityGmlObjectProjection.ProjectCityObject(
            cityObject,
            origin,
            globalCartesian: cartesian,
            demTerrainTextureOverlay: overlay,
            materialResolver: new DefaultMaterialResolver(CommonMaterialCatalog.Create()));

        MaterialBinding material = Assert.Single(projected.Materials);
        Assert.Equal(TextureSourceKind.Dataset, material.TextureSourceKind);
        Assert.Equal(MaterialReuseScope.PerObject, material.ReuseScope);
        Assert.Null(material.Family);
        Assert.Null(material.TexturePayload);
        Assert.Same(overlay, material.TerrainOverlay);
        Assert.Equal("54372778", material.TerrainMeshCode);
    }

    [Fact]
    public void ProjectParsedCityObjectAssignsDemTerrainMaterialToMatsumotoLod1SolidTopWinding()
    {
        CoordinateReferenceSystem referenceSystem = CoordinateReferenceSystem.Parse("http://www.opengis.net/def/crs/EPSG/0/6697");
        TerrainTextureOverlay overlay = CreateThirdMeshOverlay("54372778");
        GeodeticPoint origin = new(36.23163715441054, 137.97501759714283, 590.343);
        ParsedSurface bottomSurface = CreateParsedSurface(
            "matsumoto-parsed-lod1-solid-bottom",
            ParsedSurfaceSemantic.Unknown,
            CreateMatsumotoLod1SolidHorizontalRing(altitudeMeters: 590.343),
            texturePayload: null);
        ParsedSurface topSurface = CreateParsedSurface(
            "matsumoto-parsed-lod1-solid-top",
            ParsedSurfaceSemantic.Unknown,
            CreateMatsumotoLod1SolidHorizontalRing(altitudeMeters: 595.009),
            texturePayload: null);
        ParsedCityObject cityObject =
            CreateParsedCityObject(
                "bldg",
                [bottomSurface, topSurface],
                referenceSystem) with
            {
                ActualMeshCode = "54372778",
            };
        PlateauImportRequest request = new(
            Dataset: "plateau-20202-matsumoto-shi-2020",
            MeshCode: "54372778",
            CityGmlSource: DatasetLocation.Local("/tmp/plateau"));

        ImportedCityObject[] projected = LocalCityGmlObjectProjection.ProjectParsedCityObject(
            cityObject,
            origin,
            globalCartesian: null,
            demTerrainTextureOverlays: [overlay],
            requestedMeshCodeBounds: [MeshCodeBounds.TryParse("54372778")!],
            terrainHeightSampler: null,
            request,
            new DefaultMaterialResolver(CommonMaterialCatalog.Create())).ToArray();

        MaterialBinding material = Assert.Single(
            projected.SelectMany(static cityObject => cityObject.Materials),
            static material => material.TerrainOverlay is not null);
        Assert.Equal(TextureSourceKind.Dataset, material.TextureSourceKind);
        Assert.Equal(MaterialReuseScope.PerObject, material.ReuseScope);
        Assert.Null(material.Family);
        Assert.Null(material.TexturePayload);
        Assert.Same(overlay, material.TerrainOverlay);
        Assert.Equal("54372778", material.TerrainMeshCode);
    }

    [Fact]
    public void ProjectCityObjectKeepsTexturedRoofOnDatasetTextureMaterial()
    {
        CoordinateReferenceSystem referenceSystem = CoordinateReferenceSystem.Parse("http://www.opengis.net/def/crs/EPSG/0/6697");
        TerrainTextureOverlay overlay = CreateThirdMeshOverlay("53394525");
        GeodeticPoint origin = CreateMeshCenterPoint("53394525", altitudeMeters: 8.0);
        GeographicLib.LocalCartesian cartesian = new(origin.Latitude, origin.Longitude, origin.Altitude, referenceSystem.Geocentric);
        ParsedSurface roofSurface = CreateParsedSurface(
            "textured-roof",
            ParsedSurfaceSemantic.Roof,
            CreateMeshRelativeQuadVertices("53394525", altitudeMeters: 8.0, minRatio: 0.45, maxRatio: 0.55, reverseWinding: true),
            CreateTexturePayload("roof-texture"));
        ParsedCityObject cityObject = CreateParsedCityObject("bldg", [roofSurface], referenceSystem);

        ImportedCityObject projected = LocalCityGmlObjectProjection.ProjectCityObject(
            cityObject,
            origin,
            globalCartesian: cartesian,
            demTerrainTextureOverlay: overlay,
            materialResolver: new DefaultMaterialResolver(CommonMaterialCatalog.Create()));

        MaterialBinding material = Assert.Single(projected.Materials);
        Assert.Equal(TextureSourceKind.Dataset, material.TextureSourceKind);
        Assert.NotNull(material.TexturePayload);
        Assert.Null(material.TerrainOverlay);
    }

    [Fact]
    public void ProjectCityObjectKeepsRoofDemHorizontalUvOutsideThirdMeshUnclamped()
    {
        CoordinateReferenceSystem referenceSystem = CoordinateReferenceSystem.Parse("http://www.opengis.net/def/crs/EPSG/0/6697");
        TerrainTextureOverlay overlay = CreateThirdMeshOverlay("53394525");
        GeodeticPoint origin = CreateMeshCenterPoint("53394525", altitudeMeters: 8.0);
        GeographicLib.LocalCartesian cartesian = new(origin.Latitude, origin.Longitude, origin.Altitude, referenceSystem.Geocentric);
        ParsedSurface roofSurface = CreateParsedSurface(
            "outside-roof",
            ParsedSurfaceSemantic.Roof,
            CreateMeshRelativeQuadVertices("53394525", altitudeMeters: 8.0, minRatio: -0.05, maxRatio: 1.05, reverseWinding: true),
            texturePayload: null);
        ParsedCityObject cityObject = CreateParsedCityObject("bldg", [roofSurface], referenceSystem);

        ImportedCityObject projected = LocalCityGmlObjectProjection.ProjectCityObject(
            cityObject,
            origin,
            globalCartesian: cartesian,
            demTerrainTextureOverlay: overlay,
            materialResolver: new DefaultMaterialResolver(CommonMaterialCatalog.Create()));

        Assert.Contains(projected.Mesh.Vertices, static vertex => vertex.UV0.X < 0.0);
        Assert.Contains(projected.Mesh.Vertices, static vertex => vertex.UV0.X > 1.0);
        Assert.Contains(projected.Mesh.Vertices, static vertex => vertex.UV0.Y < 0.0);
        Assert.Contains(projected.Mesh.Vertices, static vertex => vertex.UV0.Y > 1.0);
    }

    [Fact]
    public void ProjectParsedCityObjectKeepsSingleOverlayRoofDemHorizontalUvOutsideThirdMeshUnclamped()
    {
        CoordinateReferenceSystem referenceSystem = CoordinateReferenceSystem.Parse("http://www.opengis.net/def/crs/EPSG/0/6697");
        TerrainTextureOverlay overlay = CreateThirdMeshOverlay("53394525");
        GeodeticPoint origin = CreateMeshCenterPoint("53394525", altitudeMeters: 8.0);
        ParsedSurface roofSurface = CreateParsedSurface(
            "outside-parsed-roof",
            ParsedSurfaceSemantic.Roof,
            CreateMeshRelativeQuadVertices("53394525", altitudeMeters: 8.0, minRatio: -0.05, maxRatio: 1.05, reverseWinding: true),
            texturePayload: null);
        ParsedCityObject cityObject = CreateParsedCityObject("bldg", [roofSurface], referenceSystem);
        PlateauImportRequest request = new(
            Dataset: "tokyo23ku",
            MeshCode: "53394525",
            CityGmlSource: DatasetLocation.Local("/tmp/plateau"));

        ImportedCityObject projected = Assert.Single(LocalCityGmlObjectProjection.ProjectParsedCityObject(
            cityObject,
            origin,
            globalCartesian: null,
            demTerrainTextureOverlays: [overlay],
            requestedMeshCodeBounds: [MeshCodeBounds.TryParse("53394525")!],
            terrainHeightSampler: null,
            request,
            new DefaultMaterialResolver(CommonMaterialCatalog.Create())));

        Assert.Contains(projected.Mesh.Vertices, static vertex => vertex.UV0.X < 0.0);
        Assert.Contains(projected.Mesh.Vertices, static vertex => vertex.UV0.X > 1.0);
        Assert.Contains(projected.Mesh.Vertices, static vertex => vertex.UV0.Y < 0.0);
        Assert.Contains(projected.Mesh.Vertices, static vertex => vertex.UV0.Y > 1.0);
    }

    [Fact]
    public async Task ParsedX3DMaterialOpticalAttributesReportWarningsWithoutChangingResoniteMaterialMembers()
    {
        using TemporaryDirectory datasetRoot = new();
        CreateX3DMaterialOpticalFixture(datasetRoot.Path);

        await using StubSceneSink sceneSink = new();
        List<string> progressMessages = [];
        PlateauImportService service = CreateService(sceneSink, progressMessages.Add);

        await service.ExecuteAsync(
            new PlateauImportRequest(
                Dataset: "tokyo23ku",
                MeshCode: "53394525",
                CityGmlSource: DatasetLocation.Local(datasetRoot.Path),
                PackageNames: ["bldg"]
),
            workRoot: "runtime/resonite");

        ImportedCityObject cityObject = Assert.Single(sceneSink.CityObjects);
        MaterialBinding material = Assert.Single(cityObject.Materials);

        Assert.Equal(new ColorRgba(0.2, 0.4, 0.6, 0.75), material.BaseColor);
        Assert.Null(material.TexturePayload);

        ResoniteMaterialBinding resoniteMaterial = ResoniteDynamicMaterialUvNormalizer.NormalizeMaterialBinding(
            SceneImportContractMapper.ToInternal(material));
        Dictionary<string, Member> members = ResoniteMaterialComponentPolicy.CreateMembers(resoniteMaterial);

        Field_colorX albedo = Assert.IsType<Field_colorX>(members["AlbedoColor"]);
        Field_float smoothness = Assert.IsType<Field_float>(members["Smoothness"]);

        Assert.Equal(0.2f, albedo.Value.r, 6);
        Assert.Equal(0.4f, albedo.Value.g, 6);
        Assert.Equal(0.6f, albedo.Value.b, 6);
        Assert.Equal(0.75f, albedo.Value.a, 6);
        Assert.Equal(0.0f, smoothness.Value, 6);
        Assert.DoesNotContain("EmissiveColor", members.Keys);
        Assert.DoesNotContain("AmbientIntensity", members.Keys);
        Assert.DoesNotContain("SpecularColor", members.Keys);

        Assert.Contains(
            progressMessages,
            static message => message.Contains("[import][warn]", StringComparison.Ordinal)
                && message.Contains("Unsupported X3DMaterial optical attribute summary", StringComparison.Ordinal)
                && message.Contains("unsupported_x3d_material_surfaces=1", StringComparison.Ordinal)
                && message.Contains("shininess_nonzero=1", StringComparison.Ordinal)
                && message.Contains("specular_nondefault=1", StringComparison.Ordinal)
                && message.Contains("specular_nondefault_only=0", StringComparison.Ordinal)
                && message.Contains("shininess_nonzero_only=0", StringComparison.Ordinal)
                && message.Contains("specular_nondefault_with_shininess=1", StringComparison.Ordinal)
                && message.Contains("emissive_nonzero=1", StringComparison.Ordinal)
                && message.Contains("ambient_nonzero=1", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DefaultEmissiveAndProjectedTransparencyDoNotReportUnsupportedX3DOpticalWarnings()
    {
        using TemporaryDirectory datasetRoot = new();
        CreateProjectedTransparencyOnlyX3DMaterialFixture(datasetRoot.Path);

        await using StubSceneSink sceneSink = new();
        List<string> progressMessages = [];
        PlateauImportService service = CreateService(sceneSink, progressMessages.Add);

        await service.ExecuteAsync(
            new PlateauImportRequest(
                Dataset: "tokyo23ku",
                MeshCode: "53394525",
                CityGmlSource: DatasetLocation.Local(datasetRoot.Path),
                PackageNames: ["bldg"]
),
            workRoot: "runtime/resonite");

        ImportedCityObject cityObject = Assert.Single(sceneSink.CityObjects);
        MaterialBinding material = Assert.Single(cityObject.Materials);

        Assert.Equal(new ColorRgba(0.2, 0.4, 0.6, 0.75), material.BaseColor);
        Assert.DoesNotContain(
            progressMessages,
            static message => message.Contains("Unsupported X3DMaterial optical attribute summary", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData((int)ParsedSurfaceSemantic.Wall)]
    [InlineData((int)ParsedSurfaceSemantic.Unknown)]
    public void CreateCommonMaterialBindingsPrecreatesCommonFacadeForTexturelessVerticalBuildingSurfaces(
        int semanticValue)
    {
        ParsedSurfaceSemantic semantic = (ParsedSurfaceSemantic)semanticValue;
        CoordinateReferenceSystem referenceSystem = CoordinateReferenceSystem.Parse("http://www.opengis.net/def/crs/EPSG/0/6697");
        GeodeticPoint origin = new(35.0, 139.0, 0.0);
        GeographicLib.LocalCartesian cartesian = new(
            origin.Latitude,
            origin.Longitude,
            origin.Altitude,
            referenceSystem.Geocentric);
        ParsedSurface surface = CreateParsedSurface(
            $"texturedless-{semantic}",
            semantic,
            CreateVerticalQuadVertices(origin, 8.0, 7.0),
            texturePayload: null);

        ParsedCityObject cityObject = CreateParsedCityObject(
            "bldg",
            [surface],
            referenceSystem,
            floorsAboveGround: 1,
            measuredHeightMeters: 7.0);

        MaterialBinding[] materialBindings = CreateCommonMaterialBindingsForTest(
            cityObject,
            origin,
            cartesian);

        MaterialBinding material = Assert.Single(materialBindings);
        Assert.Equal(BundledDefaultMaterialFamilies.WallResidentialPlasterLow, material.Family);
        Assert.Equal(MaterialReuseScope.Shared, material.ReuseScope);
        Assert.Equal(TextureSourceKind.Bundled, material.TextureSourceKind);
        Assert.Equal(MaterialProjection.Uv, material.Projection);
        Assert.Null(material.TexturePayload);
        string texturePath = BundledDefaultMaterialFamilies.GetVariant(material.Family!, material.BundledVariantIndex!.Value);
        BundledDefaultMaterialProfile profile = BundledDefaultMaterialProfiles.GetProfile(texturePath);
        Assert.Equal(new Float2(profile.TextureScale.X, profile.TextureScale.Y), material.TextureScale);
        Assert.Equal(
            profile.TextureOffset is null ? null : new Float2(profile.TextureOffset.X, profile.TextureOffset.Y),
            material.TextureOffset);
    }

    [Theory]
    [InlineData((int)ParsedSurfaceSemantic.Roof)]
    [InlineData((int)ParsedSurfaceSemantic.Ground)]
    public void CreateCommonMaterialBindingsDoesNotGenerateFacadeMaterialForRoofOrGround(
        int semanticValue)
    {
        ParsedSurfaceSemantic semantic = (ParsedSurfaceSemantic)semanticValue;
        CoordinateReferenceSystem referenceSystem = CoordinateReferenceSystem.Parse("http://www.opengis.net/def/crs/EPSG/0/6697");
        GeodeticPoint origin = new(35.0, 139.0, 0.0);
        GeographicLib.LocalCartesian cartesian = new(
            origin.Latitude,
            origin.Longitude,
            origin.Altitude,
            referenceSystem.Geocentric);
        IReadOnlyList<GeodeticPoint> surfaceVertices =
            CreateHorizontalQuadVertices(origin, altitudeMeters: 0.0, sizeMeters: 8.0, reverseWinding: true);
        ParsedSurface surface = CreateParsedSurface(
            $"non-facade-{semantic}",
            semantic,
            surfaceVertices,
            texturePayload: null);

        ParsedCityObject cityObject = CreateParsedCityObject(
            "bldg",
            [surface],
            referenceSystem,
            floorsAboveGround: 2,
            measuredHeightMeters: 7.0);

        MaterialBinding[] materialBindings = CreateCommonMaterialBindingsForTest(
            cityObject,
            origin,
            cartesian);

        MaterialBinding material = Assert.Single(materialBindings);
        Assert.Equal(MaterialProjection.Triplanar, material.Projection);
        Assert.NotEqual(BundledDefaultMaterialFamilies.Facade, material.Family);
        Assert.Equal(BundledDefaultMaterialFamilies.Roof, material.Family);
    }

    [Fact]
    public void ProjectCityObjectCullsBottomBandBuildingSurfacesBySemanticOrDownwardLod1Face()
    {
        CoordinateReferenceSystem referenceSystem = CoordinateReferenceSystem.Parse("http://www.opengis.net/def/crs/EPSG/0/6697");
        GeodeticPoint origin = new(35.0, 139.0, 0.0);
        GeographicLib.LocalCartesian cartesian = new(
            origin.Latitude,
            origin.Longitude,
            origin.Altitude,
            referenceSystem.Geocentric);
        ParsedSurface wallSurface = CreateParsedSurface(
            "wall",
            ParsedSurfaceSemantic.Wall,
            CreateVerticalQuadVertices(origin, 8.0, 6.0),
            CreateTexturePayload("wall"));
        ParsedSurface roofSurface = CreateParsedSurface(
            "roof",
            ParsedSurfaceSemantic.Roof,
            CreateHorizontalQuadVertices(origin, altitudeMeters: 6.0, sizeMeters: 8.0, reverseWinding: false),
            CreateTexturePayload("roof"));
        ParsedSurface groundSurface = CreateParsedSurface(
            "ground",
            ParsedSurfaceSemantic.Ground,
            CreateHorizontalQuadVertices(origin, altitudeMeters: 0.0, sizeMeters: 8.0, reverseWinding: false),
            CreateTexturePayload("ground"));
        ParsedSurface reversedGroundSurface = CreateParsedSurface(
            "ground-reversed",
            ParsedSurfaceSemantic.Ground,
            CreateHorizontalQuadVertices(origin, altitudeMeters: 0.0, sizeMeters: 6.0, reverseWinding: true),
            CreateTexturePayload("ground-reversed"));
        ParsedSurface outerFloorSurface = CreateParsedSurface(
            "outer-floor",
            ParsedSurfaceSemantic.OuterFloor,
            CreateHorizontalQuadVertices(origin, altitudeMeters: 0.0, sizeMeters: 4.0, reverseWinding: true),
            CreateTexturePayload("outer-floor"));
        ParsedSurface highOuterFloorSurface = CreateParsedSurface(
            "high-outer-floor",
            ParsedSurfaceSemantic.OuterFloor,
            CreateHorizontalQuadVertices(origin, altitudeMeters: 0.5, sizeMeters: 4.0, reverseWinding: true),
            CreateTexturePayload("high-outer-floor"));

        HashSet<ParsedSurface> culledSurfaces = GetculledSurfacesBeforeProjectionForTest(
            "bldg",
            [wallSurface, roofSurface, groundSurface, reversedGroundSurface, outerFloorSurface, highOuterFloorSurface],
            origin,
            cartesian);

        Assert.Contains(groundSurface, culledSurfaces);
        Assert.Contains(reversedGroundSurface, culledSurfaces);
        Assert.Contains(outerFloorSurface, culledSurfaces);
        Assert.DoesNotContain(highOuterFloorSurface, culledSurfaces);
        Assert.DoesNotContain(roofSurface, culledSurfaces);

        ParsedCityObject cityObject = CreateParsedCityObject(
            "bldg",
            [wallSurface, roofSurface, groundSurface, reversedGroundSurface, outerFloorSurface, highOuterFloorSurface],
            referenceSystem);

        ImportedCityObject projected = LocalCityGmlObjectProjection.ProjectCityObject(
            cityObject,
            origin,
            globalCartesian: cartesian,
            demTerrainTextureOverlay: null,
            materialResolver: new DefaultMaterialResolver(CommonMaterialCatalog.Create()));

        Assert.Equal(3, projected.Materials.Count);
        Assert.DoesNotContain(projected.Materials, static material => material.TexturePayload?.Source.Description == "ground");
        Assert.DoesNotContain(projected.Materials, static material => material.TexturePayload?.Source.Description == "ground-reversed");
        Assert.DoesNotContain(projected.Materials, static material => material.TexturePayload?.Source.Description == "outer-floor");
        Assert.Contains(projected.Materials, static material => material.TexturePayload?.Source.Description == "high-outer-floor");
    }

    [Fact]
    public void ProjectCityObjectKeepsNonBuildingDownwardHorizontalGroundSurface()
    {
        CoordinateReferenceSystem referenceSystem = CoordinateReferenceSystem.Parse("http://www.opengis.net/def/crs/EPSG/0/6697");
        GeodeticPoint origin = new(35.0, 139.0, 0.0);
        GeographicLib.LocalCartesian cartesian = new(
            origin.Latitude,
            origin.Longitude,
            origin.Altitude,
            referenceSystem.Geocentric);
        ParsedSurface groundSurface = CreateParsedSurface(
            "tran-ground",
            ParsedSurfaceSemantic.Ground,
            CreateHorizontalQuadVertices(origin, altitudeMeters: 0.0, sizeMeters: 8.0, reverseWinding: false),
            CreateTexturePayload("tran-ground"));

        HashSet<ParsedSurface> culledSurfaces = GetculledSurfacesBeforeProjectionForTest(
            "tran",
            [groundSurface],
            origin,
            cartesian);

        Assert.Empty(culledSurfaces);

        ParsedCityObject cityObject = CreateParsedCityObject("tran", [groundSurface], referenceSystem);

        ImportedCityObject projected = LocalCityGmlObjectProjection.ProjectCityObject(
            cityObject,
            origin,
            globalCartesian: cartesian,
            demTerrainTextureOverlay: null,
            materialResolver: new DefaultMaterialResolver(CommonMaterialCatalog.Create()));

        Assert.Single(projected.Materials);
        Assert.Equal("tran-ground", projected.Materials[0].TexturePayload?.Source.Description);
        Assert.NotEmpty(projected.Mesh.Vertices);
    }

    [Fact]
    public void ProjectCityObjectCullsBuildingLod1UnknownBottomBandSurfaces()
    {
        CoordinateReferenceSystem referenceSystem = CoordinateReferenceSystem.Parse("http://www.opengis.net/def/crs/EPSG/0/6697");
        GeodeticPoint origin = new(35.0, 139.0, 0.0);
        GeographicLib.LocalCartesian cartesian = new(
            origin.Latitude,
            origin.Longitude,
            origin.Altitude,
            referenceSystem.Geocentric);
        ParsedSurface wallSurface = CreateParsedSurface(
            "lod1-wall",
            ParsedSurfaceSemantic.Unknown,
            CreateVerticalQuadVertices(origin, 8.0, 6.0),
            CreateTexturePayload("lod1-wall"));
        ParsedSurface bottomSurface = CreateParsedSurface(
            "lod1-bottom",
            ParsedSurfaceSemantic.Unknown,
            CreateHorizontalQuadVertices(origin, altitudeMeters: 0.0, sizeMeters: 8.0, reverseWinding: false),
            CreateTexturePayload("lod1-bottom"));
        ParsedSurface reversedBottomSurface = CreateParsedSurface(
            "lod1-bottom-reversed",
            ParsedSurfaceSemantic.Unknown,
            CreateHorizontalQuadVertices(origin, altitudeMeters: 0.0, sizeMeters: 6.0, reverseWinding: true),
            CreateTexturePayload("lod1-bottom-reversed"));
        ParsedSurface roofSurface = CreateParsedSurface(
            "lod1-roof",
            ParsedSurfaceSemantic.Unknown,
            CreateHorizontalQuadVertices(origin, altitudeMeters: 6.0, sizeMeters: 8.0, reverseWinding: true),
            CreateTexturePayload("lod1-roof"));
        ParsedCityObject cityObject = CreateParsedCityObject(
            "bldg",
            [wallSurface, bottomSurface, reversedBottomSurface, roofSurface],
            referenceSystem,
            lodLevel: 1);

        HashSet<ParsedSurface> culledSurfaces = GetculledSurfacesBeforeProjectionForTest(
            "bldg",
            [wallSurface, bottomSurface, reversedBottomSurface, roofSurface],
            origin,
            cartesian);

        Assert.Contains(bottomSurface, culledSurfaces);
        Assert.Contains(reversedBottomSurface, culledSurfaces);
        Assert.DoesNotContain(roofSurface, culledSurfaces);

        ImportedCityObject projected = LocalCityGmlObjectProjection.ProjectCityObject(
            cityObject,
            origin,
            globalCartesian: cartesian,
            demTerrainTextureOverlay: null,
            materialResolver: new DefaultMaterialResolver(CommonMaterialCatalog.Create()));

        Assert.Equal(2, projected.Materials.Count);
        Assert.DoesNotContain(projected.Materials, static material => material.TexturePayload?.Source.Description == "lod1-bottom");
        Assert.DoesNotContain(projected.Materials, static material => material.TexturePayload?.Source.Description == "lod1-bottom-reversed");
        Assert.Contains(projected.Materials, static material => material.TexturePayload?.Source.Description == "lod1-roof");
        Assert.Contains(projected.Materials, static material => material.TexturePayload?.Source.Description == "lod1-wall");
    }

    [Fact]
    public void ProjectCityObjectKeepsHighDownwardHorizontalBuildingSurfaceOutsideBottomBand()
    {
        CoordinateReferenceSystem referenceSystem = CoordinateReferenceSystem.Parse("http://www.opengis.net/def/crs/EPSG/0/6697");
        GeodeticPoint origin = new(35.0, 139.0, 0.0);
        GeographicLib.LocalCartesian cartesian = new(
            origin.Latitude,
            origin.Longitude,
            origin.Altitude,
            referenceSystem.Geocentric);
        ParsedSurface bottomSurface = CreateParsedSurface(
            "bottom",
            ParsedSurfaceSemantic.Unknown,
            CreateHorizontalQuadVertices(origin, altitudeMeters: 0.0, sizeMeters: 8.0, reverseWinding: false),
            CreateTexturePayload("bottom"));
        ParsedSurface highDownwardRoofSurface = CreateParsedSurface(
            "high-roof",
            ParsedSurfaceSemantic.Roof,
            CreateHorizontalQuadVertices(origin, altitudeMeters: 6.0, sizeMeters: 8.0, reverseWinding: false),
            CreateTexturePayload("high-roof"));

        HashSet<ParsedSurface> culledSurfaces = GetculledSurfacesBeforeProjectionForTest(
            "bldg",
            [bottomSurface, highDownwardRoofSurface],
            origin,
            cartesian);

        Assert.Contains(bottomSurface, culledSurfaces);
        Assert.DoesNotContain(highDownwardRoofSurface, culledSurfaces);
    }

    [Fact]
    public void ProjectCityObjectKeepsSingleDownwardHorizontalBuildingSurfaceWhenNoHigherGeometryExists()
    {
        CoordinateReferenceSystem referenceSystem = CoordinateReferenceSystem.Parse("http://www.opengis.net/def/crs/EPSG/0/6697");
        GeodeticPoint origin = new(35.0, 139.0, 0.0);
        GeographicLib.LocalCartesian cartesian = new(
            origin.Latitude,
            origin.Longitude,
            origin.Altitude,
            referenceSystem.Geocentric);
        ParsedSurface onlySurface = CreateParsedSurface(
            "only-surface",
            ParsedSurfaceSemantic.Roof,
            CreateHorizontalQuadVertices(origin, altitudeMeters: 6.0, sizeMeters: 8.0, reverseWinding: false),
            CreateTexturePayload("only-surface"));

        HashSet<ParsedSurface> culledSurfaces = GetculledSurfacesBeforeProjectionForTest(
            "bldg",
            [onlySurface],
            origin,
            cartesian);

        Assert.Empty(culledSurfaces);

        ParsedCityObject cityObject = CreateParsedCityObject(
            "bldg",
            [onlySurface],
            referenceSystem,
            lodLevel: 2);

        ImportedCityObject projected = LocalCityGmlObjectProjection.ProjectCityObject(
            cityObject,
            origin,
            globalCartesian: cartesian,
            demTerrainTextureOverlay: null,
            materialResolver: new DefaultMaterialResolver(CommonMaterialCatalog.Create()));

        Assert.Single(projected.Materials);
        Assert.Contains(projected.Materials, static material => material.TexturePayload?.Source.Description == "only-surface");
    }

    [Fact]
    public void ProjectCityObjectAppliesBottomBandThresholdNearBoundary()
    {
        CoordinateReferenceSystem referenceSystem = CoordinateReferenceSystem.Parse("http://www.opengis.net/def/crs/EPSG/0/6697");
        GeodeticPoint origin = new(35.0, 139.0, 0.0);
        GeographicLib.LocalCartesian cartesian = new(
            origin.Latitude,
            origin.Longitude,
            origin.Altitude,
            referenceSystem.Geocentric);
        ParsedSurface wallSurface = CreateParsedSurface(
            "wall",
            ParsedSurfaceSemantic.Wall,
            CreateVerticalQuadVertices(origin, 8.0, 6.0),
            CreateTexturePayload("wall"));
        ParsedSurface exactBoundaryBottomSurface = CreateParsedSurface(
            "bottom-inside-threshold",
            ParsedSurfaceSemantic.Unknown,
            CreateHorizontalQuadVertices(origin, altitudeMeters: 0.099, sizeMeters: 8.0, reverseWinding: false),
            CreateTexturePayload("bottom-inside-threshold"));
        ParsedSurface aboveBoundaryBottomSurface = CreateParsedSurface(
            "bottom-outside-threshold",
            ParsedSurfaceSemantic.Unknown,
            CreateHorizontalQuadVertices(origin, altitudeMeters: 0.101, sizeMeters: 8.0, reverseWinding: false),
            CreateTexturePayload("bottom-outside-threshold"));
        ParsedSurface roofSurface = CreateParsedSurface(
            "roof",
            ParsedSurfaceSemantic.Unknown,
            CreateHorizontalQuadVertices(origin, altitudeMeters: 6.0, sizeMeters: 8.0, reverseWinding: true),
            CreateTexturePayload("roof"));

        HashSet<ParsedSurface> culledSurfaces = GetculledSurfacesBeforeProjectionForTest(
            "bldg",
            [wallSurface, exactBoundaryBottomSurface, aboveBoundaryBottomSurface, roofSurface],
            origin,
            cartesian);

        Assert.Contains(exactBoundaryBottomSurface, culledSurfaces);
        Assert.DoesNotContain(aboveBoundaryBottomSurface, culledSurfaces);
        Assert.DoesNotContain(roofSurface, culledSurfaces);
    }

    [Fact]
    public void CreateCommonMaterialBindingsExcludesCulledBottomBandBuildingSurface()
    {
        CoordinateReferenceSystem referenceSystem = CoordinateReferenceSystem.Parse("http://www.opengis.net/def/crs/EPSG/0/6697");
        GeodeticPoint origin = new(35.0, 139.0, 0.0);
        GeographicLib.LocalCartesian cartesian = new(
            origin.Latitude,
            origin.Longitude,
            origin.Altitude,
            referenceSystem.Geocentric);
        ParsedSurface wallSurface = CreateParsedSurface(
            "wall",
            ParsedSurfaceSemantic.Wall,
            CreateVerticalQuadVertices(origin, 8.0, 6.0),
            texturePayload: null);
        ParsedSurface bottomSurface = CreateParsedSurface(
            "bottom",
            ParsedSurfaceSemantic.Unknown,
            CreateHorizontalQuadVertices(origin, altitudeMeters: 0.0, sizeMeters: 8.0, reverseWinding: false),
            texturePayload: null,
            baseColor: new ColorRgba(1.0, 0.0, 0.0, 1.0));
        ParsedSurface roofSurface = CreateParsedSurface(
            "roof",
            ParsedSurfaceSemantic.Unknown,
            CreateHorizontalQuadVertices(origin, altitudeMeters: 6.0, sizeMeters: 8.0, reverseWinding: true),
            texturePayload: null,
            baseColor: new ColorRgba(0.0, 0.0, 1.0, 1.0));
        ParsedCityObject cityObject = CreateParsedCityObject(
            "bldg",
            [wallSurface, bottomSurface, roofSurface],
            referenceSystem,
            lodLevel: 1);

        MaterialBinding[] materialBindings = CreateCommonMaterialBindingsForTest(
            cityObject,
            origin,
            cartesian);

        Assert.DoesNotContain(materialBindings, static binding => binding.BaseColor == new ColorRgba(1.0, 0.0, 0.0, 1.0));
        Assert.Contains(materialBindings, static binding => binding.BaseColor == new ColorRgba(0.0, 0.0, 1.0, 1.0));
    }

    [Fact]
    public async Task PartitionParsedCityObjectPreservesNonGeneratedDemSurfacesWhenOverlaysPartition()
    {
        using TemporaryDirectory datasetRoot = new();
        CreateRuntimeMixedSurfaceDemFixture(datasetRoot.Path);

        await using StubSceneSink sceneSink = new();
        PlateauImportService service = CreateService(sceneSink);

        await service.ExecuteAsync(
            new PlateauImportRequest(
                Dataset: "tokyo23ku",
                MeshCode: "53394525",
                CityGmlSource: DatasetLocation.Local(datasetRoot.Path),
                PackageNames: ["dem"]
),
            workRoot: "runtime/resonite");

        ImportedCityObject[] demCityObjects = sceneSink.CityObjects
            .Where(static cityObject => cityObject.PackageName == "dem")
            .ToArray();

        ImportedCityObject demCityObject = Assert.Single(demCityObjects);
        Assert.Equal("dem", demCityObject.PackageName);

        MaterialBinding generatedMaterial = Assert.Single(
            demCityObject.Materials,
            static material => material.TerrainOverlay is not null);
        Assert.Equal(TextureSourceKind.Dataset, generatedMaterial.TextureSourceKind);
        Assert.NotNull(generatedMaterial.TerrainOverlay);
        Assert.Null(generatedMaterial.TexturePayload);

        MaterialBinding explicitMaterial = Assert.Single(
            demCityObject.Materials,
            static material => material.TexturePayload is not null);
        Assert.NotNull(explicitMaterial.TexturePayload);
        Assert.Contains(
            "udx/dem/53394525/appearance/mixed_surface.png",
            explicitMaterial.TexturePayload!.Source.Description,
            StringComparison.Ordinal);
        Assert.Equal(2, demCityObject.Mesh.Submeshes.Count);
        Assert.InRange(demCityObject.Mesh.Vertices.Count, 6, 18);
    }

    [Fact]
    public async Task DemMeshModeNormalizesGeneratedUvPerChunkWithoutRelyingOnMaterialTextureTransform()
    {
        using TemporaryDirectory datasetRoot = new();
        CreateRuntimeDemChunkFixture(datasetRoot.Path);

        await using StubSceneSink sceneSink = new();
        PlateauImportService service = CreateService(sceneSink);

        await service.ExecuteAsync(
            new PlateauImportRequest(
                Dataset: "tokyo23ku",
                MeshCode: "53394525",
                CityGmlSource: DatasetLocation.Local(datasetRoot.Path),
                PackageNames: ["dem"]
),
            workRoot: "runtime/resonite");

        ImportedCityObject demCityObject = Assert.Single(
            sceneSink.CityObjects,
            static cityObject => cityObject.PackageName == "dem"
                && cityObject.Materials.Any(static material => material.TerrainOverlay is not null)
                && cityObject.DisplayName == "DEM 53394525");
        Assert.Equal("dem", demCityObject.PackageName);

        MaterialBinding material = Assert.Single(demCityObject.Materials);
        Assert.NotNull(material.TerrainOverlay);
        Assert.Null(material.TexturePayload);

        double minU = demCityObject.Mesh.Vertices.Min(static vertex => vertex.UV0.X);
        double maxU = demCityObject.Mesh.Vertices.Max(static vertex => vertex.UV0.X);
        double minV = demCityObject.Mesh.Vertices.Min(static vertex => vertex.UV0.Y);
        double maxV = demCityObject.Mesh.Vertices.Max(static vertex => vertex.UV0.Y);
        Assert.True(maxU - minU > 0.79);
        Assert.True(maxV - minV > 0.79);
        Assert.InRange(minU, 0.07, 0.11);
        Assert.InRange(minV, 0.09, 0.11);
        Assert.InRange(maxU, 0.91, 0.93);
        Assert.InRange(maxV, 0.91, 0.93);
        Assert.Single(demCityObject.Mesh.Submeshes);
        Assert.InRange(demCityObject.Mesh.Vertices.Count, 4, 6);
    }

    [Fact]
    public void DemTerrainStaticModeDoesNotRequireGeneratedDemOverlayCoverage()
    {
        CoordinateReferenceSystem referenceSystem = CoordinateReferenceSystem.Parse("http://www.opengis.net/def/crs/EPSG/0/6697");
        ParsedSurface generatedSurface = (CreateParsedSurface(
                "dem-generated",
                ParsedSurfaceSemantic.Ground,
                CreateMeshRelativeQuadVertices("53394525", altitudeMeters: 1.0, minRatio: 0.1, maxRatio: 0.9, reverseWinding: false)) with
        {
        });
        ParsedCityObject cityObject = CreateParsedCityObject("dem", [generatedSurface], referenceSystem);
        GeodeticPoint origin = CreateMeshCenterPoint("53394525", 0.0);
        TerrainTextureOverlay unrelatedOverlay = CreateThirdMeshOverlay("53394526");
        PlateauImportRequest request = new(
            Dataset: "test-dataset",
            MeshCode: "53394525",
            CityGmlSource: DatasetLocation.Local("C:\\dataset"),
            TerrainMeshMode: TerrainMeshMode.Static);

        ImportedCityObject[] projected = LocalCityGmlObjectProjection.ProjectParsedCityObject(
            cityObject,
            origin,
            new GeographicLib.LocalCartesian(origin.Latitude, origin.Longitude, origin.Altitude, referenceSystem.Geocentric),
            [unrelatedOverlay],
            requestedMeshCodeBounds: [MeshCodeBounds.TryParse("53394525")!],
            terrainHeightSampler: null,
            request,
            new DefaultMaterialResolver(CommonMaterialCatalog.Create())).ToArray();

        ImportedCityObject result = Assert.Single(projected);
        Assert.IsType<TriangleMeshGeometry>(result.Geometry);
        Assert.DoesNotContain(result.Materials, static material => material.TerrainOverlay is not null);
        MaterialBinding material = Assert.Single(result.Materials);
        Assert.Equal(181.0 / 255.0, material.BaseColor.R, precision: 6);
        Assert.Equal(176.0 / 255.0, material.BaseColor.G, precision: 6);
        Assert.Equal(166.0 / 255.0, material.BaseColor.B, precision: 6);
    }

    [Fact]
    public void DemTerrainGridModeDoesNotRequireGeneratedDemOverlayCoverage()
    {
        CoordinateReferenceSystem referenceSystem = CoordinateReferenceSystem.Parse("http://www.opengis.net/def/crs/EPSG/0/6697");
        ParsedSurface generatedSurface = (CreateParsedSurface(
                "dem-generated",
                ParsedSurfaceSemantic.Ground,
                CreateMeshRelativeQuadVertices("53394525", altitudeMeters: 1.0, minRatio: 0.1, maxRatio: 0.9, reverseWinding: false)) with
        {
        });
        ParsedCityObject cityObject = CreateParsedCityObject("dem", [generatedSurface], referenceSystem);
        GeodeticPoint origin = CreateMeshCenterPoint("53394525", 0.0);
        TerrainTextureOverlay unrelatedOverlay = CreateThirdMeshOverlay("53394526");
        PlateauImportRequest request = new(
            Dataset: "test-dataset",
            MeshCode: "53394525",
            CityGmlSource: DatasetLocation.Local("C:\\dataset"),
            TerrainMeshMode: TerrainMeshMode.Grid);

        ImportedCityObject[] projected = LocalCityGmlObjectProjection.ProjectParsedCityObject(
            cityObject,
            origin,
            new GeographicLib.LocalCartesian(origin.Latitude, origin.Longitude, origin.Altitude, referenceSystem.Geocentric),
            [unrelatedOverlay],
            requestedMeshCodeBounds: [MeshCodeBounds.TryParse("53394525")!],
            terrainHeightSampler: null,
            request,
            new DefaultMaterialResolver(CommonMaterialCatalog.Create())).ToArray();

        ImportedCityObject result = Assert.Single(projected);
        Assert.IsType<TerrainGridGeometry>(result.Geometry);
        Assert.DoesNotContain(result.Materials, static material => material.TerrainOverlay is not null);
        MaterialBinding material = Assert.Single(result.Materials);
        Assert.Equal(181.0 / 255.0, material.BaseColor.R, precision: 6);
        Assert.Equal(176.0 / 255.0, material.BaseColor.G, precision: 6);
        Assert.Equal(166.0 / 255.0, material.BaseColor.B, precision: 6);
    }

    [Fact]
    public void DemTerrainStaticModeKeepsSingleThirdMeshOverlayMaterialAcrossGeneratedDemSurfaces()
    {
        CoordinateReferenceSystem referenceSystem = CoordinateReferenceSystem.Parse("http://www.opengis.net/def/crs/EPSG/0/6697");
        ParsedSurface coveredSurface = (CreateParsedSurface(
                "dem-covered",
                ParsedSurfaceSemantic.Ground,
                CreateMeshRelativeQuadVertices("53394525", altitudeMeters: 1.0, minRatio: 0.1, maxRatio: 0.2, reverseWinding: false)) with
        {
        });
        ParsedSurface uncoveredSurface = (CreateParsedSurface(
                "dem-uncovered",
                ParsedSurfaceSemantic.Ground,
                CreateMeshRelativeQuadVertices("53394526", altitudeMeters: 1.0, minRatio: 0.1, maxRatio: 0.2, reverseWinding: false)) with
        {
        });
        ParsedCityObject cityObject = CreateParsedCityObject("dem", [coveredSurface, uncoveredSurface], referenceSystem);
        GeodeticPoint origin = CreateMeshCenterPoint("53394525", 0.0);
        TerrainTextureOverlay coveredOverlay = CreateThirdMeshOverlay("53394525");
        PlateauImportRequest request = new(
            Dataset: "test-dataset",
            MeshCode: "53394525",
            CityGmlSource: DatasetLocation.Local("C:\\dataset"),
            TerrainMeshMode: TerrainMeshMode.Static);

        ImportedCityObject[] projected = LocalCityGmlObjectProjection.ProjectParsedCityObject(
            cityObject,
            origin,
            new GeographicLib.LocalCartesian(origin.Latitude, origin.Longitude, origin.Altitude, referenceSystem.Geocentric),
            [coveredOverlay],
            requestedMeshCodeBounds: [MeshCodeBounds.TryParse("53394525")!, MeshCodeBounds.TryParse("53394526")!],
            terrainHeightSampler: null,
            request,
            new DefaultMaterialResolver(CommonMaterialCatalog.Create())).ToArray();

        ImportedCityObject projectedCityObject = Assert.Single(projected);
        MaterialBinding terrainMaterial = Assert.Single(projectedCityObject.Materials);
        Assert.Same(coveredOverlay, terrainMaterial.TerrainOverlay);
        Assert.Equal("53394525", terrainMaterial.TerrainMeshCode);
    }

    [Fact]
    public void DemTerrainStaticModeKeepsPartialSurfaceInSingleThirdMeshOverlayMaterial()
    {
        CoordinateReferenceSystem referenceSystem = CoordinateReferenceSystem.Parse("http://www.opengis.net/def/crs/EPSG/0/6697");
        (double south, double north, double west, double east) = GetMeshBounds("53394525");
        (double _, double _, double eastWest, double eastEast) = GetMeshBounds("53394526");
        ParsedSurface generatedSurface = (CreateParsedSurface(
                "dem-partial",
                ParsedSurfaceSemantic.Ground,
                [
                    new(south + ((north - south) * 0.1), west + ((east - west) * 0.25), 1.0),
                    new(south + ((north - south) * 0.1), eastWest + ((eastEast - eastWest) * 0.25), 1.0),
                    new(south + ((north - south) * 0.2), eastWest + ((eastEast - eastWest) * 0.25), 1.0),
                    new(south + ((north - south) * 0.2), west + ((east - west) * 0.25), 1.0),
                    new(south + ((north - south) * 0.1), west + ((east - west) * 0.25), 1.0),
                ]) with
        {
        });
        ParsedCityObject cityObject = CreateParsedCityObject("dem", [generatedSurface], referenceSystem);
        GeodeticPoint origin = CreateMeshCenterPoint("53394525", 0.0);
        TerrainTextureOverlay coveredOverlay = CreateThirdMeshOverlay("53394525");
        PlateauImportRequest request = new(
            Dataset: "test-dataset",
            MeshCode: "53394525",
            CityGmlSource: DatasetLocation.Local("C:\\dataset"),
            TerrainMeshMode: TerrainMeshMode.Static);

        ImportedCityObject[] projected = LocalCityGmlObjectProjection.ProjectParsedCityObject(
            cityObject,
            origin,
            new GeographicLib.LocalCartesian(origin.Latitude, origin.Longitude, origin.Altitude, referenceSystem.Geocentric),
            [coveredOverlay],
            requestedMeshCodeBounds: [MeshCodeBounds.TryParse("53394525")!, MeshCodeBounds.TryParse("53394526")!],
            terrainHeightSampler: null,
            request,
            new DefaultMaterialResolver(CommonMaterialCatalog.Create())).ToArray();

        ImportedCityObject projectedCityObject = Assert.Single(projected);
        MaterialBinding terrainMaterial = Assert.Single(projectedCityObject.Materials);
        Assert.Same(coveredOverlay, terrainMaterial.TerrainOverlay);
        Assert.Equal("53394525", terrainMaterial.TerrainMeshCode);
        Assert.NotEmpty(((TriangleMeshGeometry)projectedCityObject.Geometry).Mesh.Vertices);
    }

    [Fact]
    public async Task DemTerrainGridModeKeepsMeasuredCoverageOnEachBoundaryEdge()
    {
        using TemporaryDirectory datasetRoot = new();
        CreateRuntimeDemChunkFixture(datasetRoot.Path);

        await using StubSceneSink sceneSink = new();
        PlateauImportService service = CreateService(sceneSink);

        await service.ExecuteAsync(
            new PlateauImportRequest(
                Dataset: "tokyo23ku",
                MeshCode: "53394525",
                CityGmlSource: DatasetLocation.Local(datasetRoot.Path),
                PackageNames: ["dem"],
                TerrainMeshMode: TerrainMeshMode.Grid
),
            workRoot: "runtime/resonite");

        ImportedCityObject demCityObject = Assert.Single(
            sceneSink.CityObjects,
            static cityObject => cityObject.PackageName == "dem"
                && cityObject.Geometry is TerrainGridGeometry);
        TerrainGridGeometry geometry = Assert.IsType<TerrainGridGeometry>(demCityObject.Geometry);

        TerrainGridSampleCoverage[] topCoverage = Enumerable.Range(0, geometry.Width)
            .Select(index => geometry.SampleCoverage![index])
            .ToArray();
        TerrainGridSampleCoverage[] bottomCoverage = Enumerable.Range(0, geometry.Width)
            .Select(index => geometry.SampleCoverage![((geometry.Height - 1) * geometry.Width) + index])
            .ToArray();
        TerrainGridSampleCoverage[] leftCoverage = Enumerable.Range(0, geometry.Height)
            .Select(index => geometry.SampleCoverage![index * geometry.Width])
            .ToArray();
        TerrainGridSampleCoverage[] rightCoverage = Enumerable.Range(0, geometry.Height)
            .Select(index => geometry.SampleCoverage![(index * geometry.Width) + (geometry.Width - 1)])
            .ToArray();

        Assert.True(geometry.HeightSamples.Max() > 0.0, "Measured DEM surface heights were unexpectedly lost.");
        Assert.NotNull(geometry.SampleCoverage);
        Assert.Equal(geometry.Width * geometry.Height, geometry.SampleCoverage.Count);
        Assert.Contains(geometry.SampleCoverage, static coverage => coverage == TerrainGridSampleCoverage.Measured);
        Assert.Contains(topCoverage, static coverage => coverage == TerrainGridSampleCoverage.Measured);
        Assert.Contains(bottomCoverage, static coverage => coverage == TerrainGridSampleCoverage.Measured);
        Assert.Contains(leftCoverage, static coverage => coverage == TerrainGridSampleCoverage.Measured);
        Assert.Contains(rightCoverage, static coverage => coverage == TerrainGridSampleCoverage.Measured);
    }

    [Fact]
    public void DemTerrainGridSpatialIndexDoesNotScanEveryTriangleForEmptyCells()
    {
        TerrainGridSpatialIndex spatialIndex = TerrainGridSpatialIndex.Create(
            [
                new TerrainGridTriangle(new Float3(0.0, 0.0, 0.0), new Float3(1.0, 0.0, 0.0), new Float3(0.0, 0.0, 1.0)),
                new TerrainGridTriangle(new Float3(100.0, 0.0, 100.0), new Float3(101.0, 0.0, 100.0), new Float3(100.0, 0.0, 101.0)),
            ],
            minX: 0.0,
            maxX: 101.0,
            minZ: 0.0,
            maxZ: 101.0);

        TerrainGridSpatialIndex emptySpatialIndex = TerrainGridSpatialIndex.Create(
            [],
            minX: 0.0,
            maxX: 1.0,
            minZ: 0.0,
            maxZ: 1.0);
        IReadOnlyList<int> populatedCellCandidates = spatialIndex.GetCandidateTriangleIndices(0.25, 0.25);

        Assert.Empty(emptySpatialIndex.GetCandidateTriangleIndices(0.5, 0.5));
        Assert.Empty(spatialIndex.GetCandidateTriangleIndices(25.0, 75.0));
        Assert.NotEmpty(populatedCellCandidates);
        Assert.IsType<int[]>(populatedCellCandidates);
    }

    [Fact]
    public async Task DemTerrainGridModeKeepsGeneratedTextureUvTransformOnGridMeshWhenGridCoversPartialOverlayMesh()
    {
        using TemporaryDirectory datasetRoot = new();
        CreateRuntimeDemChunkFixture(datasetRoot.Path);

        await using StubSceneSink sceneSink = new();
        PlateauImportService service = CreateService(sceneSink);

        await service.ExecuteAsync(
            new PlateauImportRequest(
                Dataset: "tokyo23ku",
                MeshCode: "53394525",
                CityGmlSource: DatasetLocation.Local(datasetRoot.Path),
                PackageNames: ["dem"],
                TerrainMeshMode: TerrainMeshMode.Grid
),
            workRoot: "runtime/resonite");

        ImportedCityObject demCityObject = Assert.Single(
            sceneSink.CityObjects,
            static cityObject => cityObject.PackageName == "dem"
                && cityObject.Geometry is TerrainGridGeometry
                && cityObject.Materials.Any(static material => material.TerrainOverlay is not null));
        TerrainGridGeometry geometry = Assert.IsType<TerrainGridGeometry>(demCityObject.Geometry);
        MaterialBinding material = Assert.Single(demCityObject.Materials);

        Assert.NotNull(geometry.UvScale);
        Assert.NotNull(geometry.UvOffset);
        Assert.Null(material.TextureScale);
        Assert.Null(material.TextureOffset);
    }

    [Fact]
    public void DemTerrainGridModeKeepsGeneratedTextureUvTransformWhenGridCoversPartialOverlayMesh()
    {
        CoordinateReferenceSystem referenceSystem = CoordinateReferenceSystem.Parse("http://www.opengis.net/def/crs/EPSG/0/6697");
        const string meshCode = "53394525";
        ParsedSurface partialSurface = CreateParsedSurface(
            "partial-dem",
            ParsedSurfaceSemantic.Ground,
            CreateMeshRelativeQuadVertices(meshCode, altitudeMeters: 20.0, minRatio: 0.2, maxRatio: 0.8, reverseWinding: false),
            texturePayload: null,
            baseColor: new ColorRgba(0.4, 0.5, 0.3, 1.0));
        ParsedCityObject cityObject = CreateParsedCityObject("dem", [partialSurface], referenceSystem);
        GeodeticPoint origin = CreateMeshCenterPoint(meshCode, 0.0);
        GeographicLib.LocalCartesian cartesian = new(origin.Latitude, origin.Longitude, origin.Altitude, referenceSystem.Geocentric);

        ImportedCityObject projected = CityGmlParsedCityObjectProjection.ProjectTerrainMeshModeCityObject(
            GeneratedLod1RoofCityObjectFactory.CreateDraft(cityObject),
            GeneratedLod1RoofCityObjectFactory.CreateDraft(cityObject),
            origin,
            cartesian,
            CreateThirdMeshOverlay(meshCode),
            new PlateauImportRequest(
                Dataset: "tokyo23ku",
                MeshCode: meshCode,
                CityGmlSource: DatasetLocation.Local("dataset"),
                PackageNames: ["dem"],
                TerrainMeshMode: TerrainMeshMode.Grid,
                TerrainGridMetersPerVertex: 100.0,
                TerrainGridMaxResolution: 8),
            [MeshCodeBounds.Parse(meshCode)],
            new DefaultMaterialResolver(CommonMaterialCatalog.Create()),
            progressReporter: null,
            CancellationToken.None);

        TerrainGridGeometry geometry = Assert.IsType<TerrainGridGeometry>(projected.Geometry);
        Float2 uvScale = Assert.IsType<Float2>(geometry.UvScale);
        Float2 uvOffset = Assert.IsType<Float2>(geometry.UvOffset);

        Assert.InRange(uvScale.X, 0.0, 1.0);
        Assert.InRange(uvScale.Y, 0.0, 1.0);
        Assert.InRange(uvOffset.X, 0.0, 1.0);
        Assert.InRange(uvOffset.Y, 0.0, 1.0);
    }

    [Fact]
    public async Task DemTerrainDynamicModeProjectsStaticAndGridGeometry()
    {
        using TemporaryDirectory datasetRoot = new();
        CreateRuntimeDemChunkFixture(datasetRoot.Path);

        await using StubSceneSink sceneSink = new();
        PlateauImportService service = CreateService(sceneSink);

        await service.ExecuteAsync(
            new PlateauImportRequest(
                Dataset: "tokyo23ku",
                MeshCode: "53394525",
                CityGmlSource: DatasetLocation.Local(datasetRoot.Path),
                PackageNames: ["dem"],
                TerrainMeshMode: TerrainMeshMode.Dynamic
),
            workRoot: "runtime/resonite");

        ImportedCityObject demCityObject = Assert.Single(
            sceneSink.CityObjects,
            static cityObject => cityObject.PackageName == "dem"
                && cityObject.Geometry is DynamicTerrainGeometry
                && cityObject.Materials.Any(static material => material.TerrainOverlay is not null));
        DynamicTerrainGeometry geometry = Assert.IsType<DynamicTerrainGeometry>(demCityObject.Geometry);
        MaterialBinding material = Assert.Single(demCityObject.Materials);

        Assert.NotEmpty(geometry.StaticMesh.Mesh.Vertices);
        Assert.NotEmpty(geometry.StaticMesh.Mesh.Submeshes);
        Assert.True(geometry.GridMesh.Width > 1);
        Assert.True(geometry.GridMesh.Height > 1);
        Assert.NotNull(geometry.GridMesh.UvScale);
        Assert.NotNull(geometry.GridMesh.UvOffset);
        Assert.Null(material.TextureScale);
        Assert.Null(material.TextureOffset);
    }

    [Fact]
    public void DemTerrainGridModeSamplesRawDemSourceAcrossThirdMeshBounds()
    {
        CoordinateReferenceSystem referenceSystem = CoordinateReferenceSystem.Parse("http://www.opengis.net/def/crs/EPSG/0/6697");
        const string meshCode = "53394525";
        (double south, double north, double west, double east) = GetMeshBounds(meshCode);
        ParsedSurface rawSurface = CreateParsedSurface(
            "raw-dem",
            ParsedSurfaceSemantic.Ground,
            CreateBoundsQuadVertices(
                south - ((north - south) * 0.05),
                north + ((north - south) * 0.05),
                west - ((east - west) * 0.05),
                east + ((east - west) * 0.05),
                20.0),
            texturePayload: null,
            baseColor: new ColorRgba(0.4, 0.5, 0.3, 1.0));
        ParsedSurface clippedVisualSurface = CreateParsedSurface(
            "clipped-dem",
            ParsedSurfaceSemantic.Ground,
            CreateBoundsQuadVertices(
                south + ((north - south) * 0.05),
                north - ((north - south) * 0.05),
                west + ((east - west) * 0.05),
                east - ((east - west) * 0.05),
                20.0),
            texturePayload: null,
            baseColor: new ColorRgba(0.4, 0.5, 0.3, 1.0));
        ParsedCityObject visualCityObject = CreateParsedCityObject("dem", [clippedVisualSurface], referenceSystem);
        ParsedCityObject rawCityObject = visualCityObject with { Surfaces = [rawSurface] };
        GeodeticPoint origin = CreateMeshCenterPoint(meshCode, 0.0);
        GeographicLib.LocalCartesian cartesian = new(origin.Latitude, origin.Longitude, origin.Altitude, referenceSystem.Geocentric);

        ImportedCityObject projected = CityGmlParsedCityObjectProjection.ProjectTerrainMeshModeCityObject(
            GeneratedLod1RoofCityObjectFactory.CreateDraft(visualCityObject),
            GeneratedLod1RoofCityObjectFactory.CreateDraft(rawCityObject),
            origin,
            cartesian,
            CreateThirdMeshOverlay(meshCode),
            new PlateauImportRequest(
                Dataset: "tokyo23ku",
                MeshCode: meshCode,
                CityGmlSource: DatasetLocation.Local("dataset"),
                PackageNames: ["dem"],
                TerrainMeshMode: TerrainMeshMode.Grid,
                TerrainGridMetersPerVertex: 100.0,
                TerrainGridMaxResolution: 8),
            [MeshCodeBounds.Parse(meshCode)],
            new DefaultMaterialResolver(CommonMaterialCatalog.Create()),
            progressReporter: null,
            CancellationToken.None);

        TerrainGridGeometry geometry = Assert.IsType<TerrainGridGeometry>(projected.Geometry);
        MaterialBinding material = Assert.Single(projected.Materials);
        Assert.All(EnumerateBoundaryCoverage(geometry), static coverage => Assert.Equal(TerrainGridSampleCoverage.Measured, coverage));
        Assert.True(geometry.Size.X > 0.0);
        Assert.True(geometry.Size.Y > 0.0);
        Assert.InRange(
            geometry.Size.X / EstimateProjectedLongitudeSpanMeters(south, north, west, east, origin, cartesian),
            0.99,
            1.01);
        Assert.InRange(
            geometry.Size.Y / EstimateProjectedLatitudeSpanMeters(south, north, west, east, origin, cartesian),
            0.99,
            1.01);
        Assert.Null(geometry.UvScale);
        Assert.Null(geometry.UvOffset);
        Assert.NotNull(material.TerrainOverlay);
    }

    [Fact]
    public void DemTerrainGridModeSkipsSourceSurfaceOutsideActualThirdMeshBounds()
    {
        CoordinateReferenceSystem referenceSystem = CoordinateReferenceSystem.Parse("http://www.opengis.net/def/crs/EPSG/0/6697");
        ParsedSurface rawSurface = CreateParsedSurface(
            "raw-dem-outside-actual-mesh",
            ParsedSurfaceSemantic.Ground,
            CreateMeshRelativeQuadVertices("53394525", altitudeMeters: 20.0, minRatio: 0.1, maxRatio: 0.9, reverseWinding: false),
            texturePayload: null,
            baseColor: new ColorRgba(0.4, 0.5, 0.3, 1.0));
        ParsedCityObject cityObject = CreateParsedCityObject("dem", [rawSurface], referenceSystem) with
        {
            ActualMeshCode = "53394526",
            SharedAcrossMeshCodes = true,
        };
        PlateauImportRequest request = new(
            Dataset: "tokyo23ku",
            MeshCode: "53394526",
            CityGmlSource: DatasetLocation.Local("/tmp/plateau"),
            PackageNames: ["dem"],
            TerrainMeshMode: TerrainMeshMode.Grid,
            TerrainGridMetersPerVertex: 100.0,
            TerrainGridMaxResolution: 8);

        ImportedCityObject[] projected = LocalCityGmlObjectProjection.ProjectParsedCityObject(
            cityObject,
            CreateMeshCenterPoint("53394526", altitudeMeters: 0.0),
            globalCartesian: null,
            demTerrainTextureOverlays: [],
            requestedMeshCodeBounds: [MeshCodeBounds.Parse("53394526")],
            terrainHeightSampler: null,
            request,
            new DefaultMaterialResolver(CommonMaterialCatalog.Create())).ToArray();

        Assert.Empty(projected);
    }

    [Fact]
    public void DemTerrainDynamicModeKeepsMaterialBindingsCompatibleWithStaticMesh()
    {
        CoordinateReferenceSystem referenceSystem = CoordinateReferenceSystem.Parse("http://www.opengis.net/def/crs/EPSG/0/6697");
        GeodeticPoint origin = CreateMeshCenterPoint("53394525", 10.0);
        ParsedSurface validSurface = CreateParsedSurface(
            "valid-dem",
            ParsedSurfaceSemantic.Ground,
            CreateMeshRelativeQuadVertices("53394525", altitudeMeters: 10.0, minRatio: 0.45, maxRatio: 0.55, reverseWinding: false),
            texturePayload: null,
            baseColor: new ColorRgba(1.0, 1.0, 1.0, 1.0));
        ParsedSurface degenerateSurface = CreateParsedSurface(
            "degenerate-dem",
            ParsedSurfaceSemantic.Ground,
            [origin, origin, origin, origin],
            texturePayload: null,
            baseColor: new ColorRgba(0.5, 0.5, 0.5, 1.0));
        ParsedCityObject cityObject = CreateParsedCityObject(
            "dem",
            [validSurface, degenerateSurface],
            referenceSystem);

        ImportedCityObject projected = ProjectTerrainMeshModeCityObjectForTest(
            cityObject,
            origin,
            new PlateauImportRequest(
                Dataset: "tokyo23ku",
                MeshCode: "53394525",
                CityGmlSource: DatasetLocation.Local("dataset"),
                PackageNames: ["dem"],
                TerrainMeshMode: TerrainMeshMode.Dynamic
));

        DynamicTerrainGeometry geometry = Assert.IsType<DynamicTerrainGeometry>(projected.Geometry);
        HashSet<int> staticSubmeshIndices = geometry.StaticMesh.Mesh.Submeshes
            .Select(static submesh => submesh.Index)
            .ToHashSet();
        MaterialBinding material = Assert.Single(projected.Materials);

        Assert.NotEmpty(staticSubmeshIndices);
        Assert.All(
            material.SubmeshIndices,
            submeshIndex => Assert.Contains(submeshIndex, staticSubmeshIndices));
    }

    [Fact]
    public void DemTerrainDynamicModeSkipsDemWhenGridCannotBeProjected()
    {
        CoordinateReferenceSystem referenceSystem = CoordinateReferenceSystem.Parse("http://www.opengis.net/def/crs/EPSG/0/6697");
        ParsedCityObject cityObject = CreateParsedCityObject(
            "dem",
            [],
            referenceSystem);

        ImportedCityObject projected = ProjectTerrainMeshModeCityObjectForTest(
            cityObject,
            new GeodeticPoint(35.0, 139.0, 0.0),
            new PlateauImportRequest(
                Dataset: "tokyo23ku",
                MeshCode: "53394525",
                CityGmlSource: DatasetLocation.Local("dataset"),
                PackageNames: ["dem"],
                TerrainMeshMode: TerrainMeshMode.Dynamic
));

        TriangleMeshGeometry geometry = Assert.IsType<TriangleMeshGeometry>(projected.Geometry);
        Assert.Empty(geometry.Mesh.Vertices);
        Assert.Empty(geometry.Mesh.Submeshes);
        Assert.Empty(projected.Materials);
    }

    [Fact]
    public async Task DemExactMeshRequestFiltersParentMeshPiecesByActualThirdMesh()
    {
        using TemporaryDirectory datasetRoot = new();
        CreateRuntimeParentMeshDemFixture(datasetRoot.Path, "53394525", "53394526");

        await using StubSceneSink sceneSink = new();
        PlateauImportService service = CreateService(sceneSink);

        await service.ExecuteAsync(
            new PlateauImportRequest(
                Dataset: "tokyo23ku",
                MeshCode: "53394525",
                CityGmlSource: DatasetLocation.Local(datasetRoot.Path),
                PackageNames: ["dem"],
                TerrainMeshMode: TerrainMeshMode.Grid
),
            workRoot: "runtime/resonite");

        ImportedCityObject[] demCityObjects = sceneSink.CityObjects
            .Where(static cityObject => cityObject.PackageName == "dem"
                && cityObject.Geometry is TerrainGridGeometry)
            .ToArray();

        Assert.NotEmpty(demCityObjects);
        Assert.All(
            demCityObjects,
            static cityObject =>
            {
                TerrainGridGeometry geometry = Assert.IsType<TerrainGridGeometry>(cityObject.Geometry);
                Assert.Equal("53394525", cityObject.ActualMeshCode);
                Assert.True(geometry.Width > 0);
                Assert.True(geometry.Height > 0);
            });
    }

    [Fact]
    public async Task DemExactMeshRequestPrefersConcreteMeshCodeNamedParentDemObjects()
    {
        using TemporaryDirectory datasetRoot = new();
        CreateRuntimeNamedParentMeshDemFixture(datasetRoot.Path, "53394525", "53394526");

        await using StubSceneSink sceneSink = new();
        PlateauImportService service = CreateService(sceneSink);

        await service.ExecuteAsync(
            new PlateauImportRequest(
                Dataset: "tokyo23ku",
                MeshCode: "53394525",
                CityGmlSource: DatasetLocation.Local(datasetRoot.Path),
                PackageNames: ["dem"],
                TerrainMeshMode: TerrainMeshMode.Grid
),
            workRoot: "runtime/resonite");

        ImportedCityObject demCityObject = Assert.Single(
            sceneSink.CityObjects,
            static cityObject => cityObject.PackageName == "dem"
                && cityObject.DisplayName == "DEM 53394525");
        Assert.Equal("DEM 53394525", demCityObject.DisplayName);
    }

    [Fact]
    public async Task TerrainAlignedObjectDoesNotUseNearestTerrainPointOutsideDemTriangles()
    {
        using TemporaryDirectory datasetRoot = new();
        CreateRuntimeDemAndLandUseGapFixture(datasetRoot.Path);

        await using StubSceneSink sceneSink = new();
        PlateauImportService service = CreateService(sceneSink);

        await service.ExecuteAsync(
            new PlateauImportRequest(
                Dataset: "tokyo23ku",
                MeshCode: "53394525",
                CityGmlSource: DatasetLocation.Local(datasetRoot.Path),
                PackageNames: ["dem", "luse"]
),
            workRoot: "runtime/resonite");

        ImportedCityObject landUse = Assert.Single(
            sceneSink.CityObjects,
            static cityObject => cityObject.PackageName == "luse");

        Assert.True(
            landUse.Transform.Position.Y > 40.0,
            $"Land-use object was incorrectly snapped toward nearby DEM fallback height: y={landUse.Transform.Position.Y:F6}");
    }

    private static void CreateX3DMaterialOpticalFixture(string datasetRoot)
    {
        string packageDirectory = Path.Combine(datasetRoot, "udx", "bldg", "53394525");
        Directory.CreateDirectory(packageDirectory);
        string xml =
            """
            <?xml version="1.0" encoding="UTF-8"?>
            <core:CityModel
              xmlns:app="http://www.opengis.net/citygml/appearance/2.0"
              xmlns:bldg="http://www.opengis.net/citygml/building/2.0"
              xmlns:core="http://www.opengis.net/citygml/2.0"
              xmlns:gml="http://www.opengis.net/gml">
              <app:appearanceMember>
                <app:Appearance>
                  <app:surfaceDataMember>
                    <app:X3DMaterial>
                      <app:ambientIntensity>0.33</app:ambientIntensity>
                      <app:diffuseColor>0.2 0.4 0.6</app:diffuseColor>
                      <app:emissiveColor>0.05 0.10 0.15</app:emissiveColor>
                      <app:specularColor>0.7 0.8 0.9</app:specularColor>
                      <app:shininess>0.72</app:shininess>
                      <app:transparency>0.25</app:transparency>
                      <app:target uri="#poly-x3d-wall" />
                    </app:X3DMaterial>
                  </app:surfaceDataMember>
                </app:Appearance>
              </app:appearanceMember>
              <core:cityObjectMember>
                <bldg:Building gml:id="bldg-x3d">
                  <gml:name>X3D optical material test</gml:name>
                  <bldg:lod2MultiSurface>
                    <gml:MultiSurface>
                      <gml:surfaceMember>
                        <gml:Polygon gml:id="poly-x3d-wall">
                          <gml:exterior>
                            <gml:LinearRing gml:id="ring-x3d-wall">
                              <gml:posList>0 0 0 10 0 0 10 0 10 0 0 10 0 0 0</gml:posList>
                            </gml:LinearRing>
                          </gml:exterior>
                        </gml:Polygon>
                      </gml:surfaceMember>
                    </gml:MultiSurface>
                  </bldg:lod2MultiSurface>
                </bldg:Building>
              </core:cityObjectMember>
            </core:CityModel>
            """;

        File.WriteAllText(Path.Combine(packageDirectory, "plateau_tokyo23ku_bldg_53394525_x3d_material.gml"), xml);
    }

    private static void CreateProjectedTransparencyOnlyX3DMaterialFixture(string datasetRoot)
    {
        string packageDirectory = Path.Combine(datasetRoot, "udx", "bldg", "53394525");
        Directory.CreateDirectory(packageDirectory);
        string xml =
            """
            <?xml version="1.0" encoding="UTF-8"?>
            <core:CityModel
              xmlns:app="http://www.opengis.net/citygml/appearance/2.0"
              xmlns:bldg="http://www.opengis.net/citygml/building/2.0"
              xmlns:core="http://www.opengis.net/citygml/2.0"
              xmlns:gml="http://www.opengis.net/gml">
              <app:appearanceMember>
                <app:Appearance>
                  <app:surfaceDataMember>
                    <app:X3DMaterial>
                      <app:diffuseColor>0.2 0.4 0.6</app:diffuseColor>
                      <app:emissiveColor>0 0 0</app:emissiveColor>
                      <app:transparency>0.25</app:transparency>
                      <app:target uri="#poly-x3d-wall" />
                    </app:X3DMaterial>
                  </app:surfaceDataMember>
                </app:Appearance>
              </app:appearanceMember>
              <core:cityObjectMember>
                <bldg:Building gml:id="bldg-x3d">
                  <gml:name>X3D projected transparency material test</gml:name>
                  <bldg:lod2MultiSurface>
                    <gml:MultiSurface>
                      <gml:surfaceMember>
                        <gml:Polygon gml:id="poly-x3d-wall">
                          <gml:exterior>
                            <gml:LinearRing gml:id="ring-x3d-wall">
                              <gml:posList>0 0 0 10 0 0 10 0 10 0 0 10 0 0 0</gml:posList>
                            </gml:LinearRing>
                          </gml:exterior>
                        </gml:Polygon>
                      </gml:surfaceMember>
                    </gml:MultiSurface>
                  </bldg:lod2MultiSurface>
                </bldg:Building>
              </core:cityObjectMember>
            </core:CityModel>
            """;

        File.WriteAllText(
            Path.Combine(packageDirectory, "plateau_tokyo23ku_bldg_53394525_x3d_transparency.gml"),
            xml);
    }

    private static void CreateRuntimeMixedSurfaceDemFixture(string datasetRoot)
    {
        string packageDirectory = Path.Combine(datasetRoot, "udx", "dem", "53394525");
        Directory.CreateDirectory(packageDirectory);
        (double south, double north, double west, double east) = GetMeshBounds("53394525");

        string westTriangle = CreateTrianglePosListFromRatios(
            south,
            north,
            west,
            east,
            (0.08, 0.06, 5.0),
            (0.92, 0.06, 10.0),
            (0.92, 0.44, 12.0));
        string eastTriangle = CreateTrianglePosListFromRatios(
            south,
            north,
            west,
            east,
            (0.08, 0.56, 6.0),
            (0.92, 0.56, 8.0),
            (0.92, 0.94, 14.0));
        string texturedTriangle = CreateTrianglePosListFromRatios(
            south,
            north,
            west,
            east,
            (0.10, 0.18, 4.0),
            (0.90, 0.18, 7.0),
            (0.90, 0.36, 8.0));

        string xml =
            $$"""
            <?xml version="1.0" encoding="UTF-8"?>
            <core:CityModel xmlns:app="http://www.opengis.net/citygml/appearance/2.0" xmlns:core="http://www.opengis.net/citygml/2.0" xmlns:gml="http://www.opengis.net/gml" xmlns:dem="http://www.opengis.net/citygml/relief/2.0">
              <gml:boundedBy>
                <gml:Envelope srsName="http://www.opengis.net/def/crs/EPSG/0/6697" srsDimension="3">
                  <gml:lowerCorner>{{south.ToString("F8", CultureInfo.InvariantCulture)}} {{west.ToString("F8", CultureInfo.InvariantCulture)}} 0</gml:lowerCorner>
                  <gml:upperCorner>{{north.ToString("F8", CultureInfo.InvariantCulture)}} {{east.ToString("F8", CultureInfo.InvariantCulture)}} 20</gml:upperCorner>
                </gml:Envelope>
              </gml:boundedBy>
              <app:appearanceMember>
                <app:Appearance>
                  <app:surfaceDataMember>
                    <app:ParameterizedTexture>
                      <app:imageURI>appearance/mixed_surface.png</app:imageURI>
                      <app:target uri="#tri-dem-textured">
                        <app:TexCoordList>
                          <app:textureCoordinates ring="#ring-dem-textured">0 0 1 0 1 1 0 1</app:textureCoordinates>
                        </app:TexCoordList>
                      </app:target>
                    </app:ParameterizedTexture>
                  </app:surfaceDataMember>
                </app:Appearance>
              </app:appearanceMember>
              <core:cityObjectMember>
                <dem:ReliefFeature gml:id="dem-mixed">
                  <gml:name>Mixed Relief</gml:name>
                  <dem:reliefComponent>
                    <dem:TINRelief gml:id="dem-mixed-component">
                      <dem:tin>
                        <gml:TriangulatedSurface>
                          <gml:trianglePatches>
                            <gml:Triangle gml:id="tri-dem-west">
                              <gml:exterior>
                                <gml:LinearRing gml:id="ring-dem-west">
                                  <gml:posList>{{westTriangle}}</gml:posList>
                                </gml:LinearRing>
                              </gml:exterior>
                            </gml:Triangle>
                            <gml:Triangle gml:id="tri-dem-east">
                              <gml:exterior>
                                <gml:LinearRing gml:id="ring-dem-east">
                                  <gml:posList>{{eastTriangle}}</gml:posList>
                                </gml:LinearRing>
                              </gml:exterior>
                            </gml:Triangle>
                            <gml:Triangle gml:id="tri-dem-textured">
                              <gml:exterior>
                                <gml:LinearRing gml:id="ring-dem-textured">
                                  <gml:posList>{{texturedTriangle}}</gml:posList>
                                </gml:LinearRing>
                              </gml:exterior>
                            </gml:Triangle>
                          </gml:trianglePatches>
                        </gml:TriangulatedSurface>
                      </dem:tin>
                    </dem:TINRelief>
                  </dem:reliefComponent>
                </dem:ReliefFeature>
              </core:cityObjectMember>
            </core:CityModel>
            """;

        File.WriteAllText(Path.Combine(packageDirectory, "plateau_tokyo23ku_dem_53394525_mixed.gml"), xml);

        Directory.CreateDirectory(Path.Combine(packageDirectory, "appearance"));
        using Image<Rgba32> image = new(1, 1, new Rgba32(255, 255, 255, 255));
        image.SaveAsPng(Path.Combine(packageDirectory, "appearance", "mixed_surface.png"));
    }

    private static void CreateRuntimeDemChunkFixture(string datasetRoot)
    {
        string packageDirectory = Path.Combine(datasetRoot, "udx", "dem", "53394525");
        Directory.CreateDirectory(packageDirectory);
        (double south, double north, double west, double east) = GetMeshBounds("53394525");
        string triangleA = CreateTrianglePosListFromRatios(
            south,
            north,
            west,
            east,
            (0.10, 0.08, 5.0),
            (0.92, 0.08, 10.0),
            (0.92, 0.92, 12.0));
        string triangleB = CreateTrianglePosListFromRatios(
            south,
            north,
            west,
            east,
            (0.10, 0.08, 5.0),
            (0.92, 0.92, 12.0),
            (0.10, 0.92, 7.0));

        string xml =
            $$"""
            <?xml version="1.0" encoding="UTF-8"?>
            <core:CityModel xmlns:core="http://www.opengis.net/citygml/2.0" xmlns:gml="http://www.opengis.net/gml" xmlns:dem="http://www.opengis.net/citygml/relief/2.0">
              <gml:boundedBy>
                <gml:Envelope srsName="http://www.opengis.net/def/crs/EPSG/0/6697" srsDimension="3">
                  <gml:lowerCorner>{{south.ToString("F8", CultureInfo.InvariantCulture)}} {{west.ToString("F8", CultureInfo.InvariantCulture)}} 0</gml:lowerCorner>
                  <gml:upperCorner>{{north.ToString("F8", CultureInfo.InvariantCulture)}} {{east.ToString("F8", CultureInfo.InvariantCulture)}} 20</gml:upperCorner>
                </gml:Envelope>
              </gml:boundedBy>
              <core:cityObjectMember>
                <dem:ReliefFeature gml:id="dem-chunk">
                  <gml:name>Chunk Relief</gml:name>
                  <dem:reliefComponent>
                    <dem:TINRelief gml:id="dem-chunk-component">
                      <dem:tin>
                        <gml:TriangulatedSurface>
                          <gml:trianglePatches>
                            <gml:Triangle gml:id="tri-dem-a">
                              <gml:exterior>
                                <gml:LinearRing gml:id="ring-dem-a">
                                  <gml:posList>{{triangleA}}</gml:posList>
                                </gml:LinearRing>
                              </gml:exterior>
                            </gml:Triangle>
                            <gml:Triangle gml:id="tri-dem-b">
                              <gml:exterior>
                                <gml:LinearRing gml:id="ring-dem-b">
                                  <gml:posList>{{triangleB}}</gml:posList>
                                </gml:LinearRing>
                              </gml:exterior>
                            </gml:Triangle>
                          </gml:trianglePatches>
                        </gml:TriangulatedSurface>
                      </dem:tin>
                    </dem:TINRelief>
                  </dem:reliefComponent>
                </dem:ReliefFeature>
              </core:cityObjectMember>
            </core:CityModel>
            """;

        File.WriteAllText(Path.Combine(packageDirectory, "plateau_tokyo23ku_dem_53394525_chunk.gml"), xml);
    }

    private static void CreateRuntimeDemAndLandUseGapFixture(string datasetRoot)
    {
        string demDirectory = Path.Combine(datasetRoot, "udx", "dem", "53394525");
        string landUseDirectory = Path.Combine(datasetRoot, "udx", "luse", "53394525");
        Directory.CreateDirectory(demDirectory);
        Directory.CreateDirectory(landUseDirectory);
        (double south, double north, double west, double east) = GetMeshBounds("53394525");
        string demTriangle = CreateTrianglePosListFromRatios(
            south,
            north,
            west,
            east,
            (0.10, 0.08, 5.0),
            (0.92, 0.08, 6.0),
            (0.92, 0.38, 7.0));
        string landUsePolygon = CreatePolygonPosListFromRatios(
            south,
            north,
            west,
            east,
            [
                (0.18, 0.62, 50.0),
                (0.82, 0.62, 50.0),
                (0.82, 0.90, 50.0),
                (0.18, 0.90, 50.0),
            ]);

        string demXml =
            $$"""
            <?xml version="1.0" encoding="UTF-8"?>
            <core:CityModel xmlns:core="http://www.opengis.net/citygml/2.0" xmlns:gml="http://www.opengis.net/gml" xmlns:dem="http://www.opengis.net/citygml/relief/2.0">
              <gml:boundedBy>
                <gml:Envelope srsName="http://www.opengis.net/def/crs/EPSG/0/6697" srsDimension="3">
                  <gml:lowerCorner>{{south.ToString("F8", CultureInfo.InvariantCulture)}} {{west.ToString("F8", CultureInfo.InvariantCulture)}} 0</gml:lowerCorner>
                  <gml:upperCorner>{{north.ToString("F8", CultureInfo.InvariantCulture)}} {{east.ToString("F8", CultureInfo.InvariantCulture)}} 20</gml:upperCorner>
                </gml:Envelope>
              </gml:boundedBy>
              <core:cityObjectMember>
                <dem:ReliefFeature gml:id="dem-gap">
                  <gml:name>Gap Relief</gml:name>
                  <dem:reliefComponent>
                    <dem:TINRelief gml:id="dem-gap-component">
                      <dem:tin>
                        <gml:TriangulatedSurface>
                          <gml:trianglePatches>
                            <gml:Triangle gml:id="tri-gap-a">
                              <gml:exterior>
                                <gml:LinearRing gml:id="ring-gap-a">
                                  <gml:posList>{{demTriangle}}</gml:posList>
                                </gml:LinearRing>
                              </gml:exterior>
                            </gml:Triangle>
                          </gml:trianglePatches>
                        </gml:TriangulatedSurface>
                      </dem:tin>
                    </dem:TINRelief>
                  </dem:reliefComponent>
                </dem:ReliefFeature>
              </core:cityObjectMember>
            </core:CityModel>
            """;
        File.WriteAllText(Path.Combine(demDirectory, "plateau_tokyo23ku_dem_53394525_gap.gml"), demXml);

        string landUseXml =
            $$"""
            <?xml version="1.0" encoding="UTF-8"?>
            <core:CityModel xmlns:core="http://www.opengis.net/citygml/2.0" xmlns:gml="http://www.opengis.net/gml" xmlns:luse="http://www.opengis.net/citygml/landuse/2.0">
              <gml:boundedBy>
                <gml:Envelope srsName="http://www.opengis.net/def/crs/EPSG/0/6697" srsDimension="3">
                  <gml:lowerCorner>{{south.ToString("F8", CultureInfo.InvariantCulture)}} {{west.ToString("F8", CultureInfo.InvariantCulture)}} 0</gml:lowerCorner>
                  <gml:upperCorner>{{north.ToString("F8", CultureInfo.InvariantCulture)}} {{east.ToString("F8", CultureInfo.InvariantCulture)}} 60</gml:upperCorner>
                </gml:Envelope>
              </gml:boundedBy>
              <core:cityObjectMember>
                <luse:LandUse gml:id="luse-gap">
                  <gml:name>Gap Land Use</gml:name>
                  <luse:lod1MultiSurface>
                    <gml:MultiSurface>
                      <gml:surfaceMember>
                        <gml:Polygon gml:id="luse-gap-polygon">
                          <gml:exterior>
                            <gml:LinearRing gml:id="luse-gap-ring">
                              <gml:posList>{{landUsePolygon}}</gml:posList>
                            </gml:LinearRing>
                          </gml:exterior>
                        </gml:Polygon>
                      </gml:surfaceMember>
                    </gml:MultiSurface>
                  </luse:lod1MultiSurface>
                </luse:LandUse>
              </core:cityObjectMember>
            </core:CityModel>
            """;
        File.WriteAllText(Path.Combine(landUseDirectory, "plateau_tokyo23ku_luse_53394525_gap.gml"), landUseXml);
    }
    private static void CreateRuntimeParentMeshDemFixture(string datasetRoot, string requestedMeshCode, string adjacentMeshCode)
    {
        string packageDirectory = Path.Combine(datasetRoot, "udx", "dem", requestedMeshCode);
        Directory.CreateDirectory(packageDirectory);

        (double requestedSouth, double requestedNorth, double requestedWest, double requestedEast) = GetMeshBounds(requestedMeshCode);
        (double adjacentSouth, double adjacentNorth, double adjacentWest, double adjacentEast) = GetMeshBounds(adjacentMeshCode);

        string requestedTriangle = CreateTrianglePosList(
            requestedSouth,
            requestedNorth,
            requestedWest,
            requestedEast,
            5.0,
            7.0,
            9.0);
        string adjacentTriangle = CreateTrianglePosList(
            adjacentSouth,
            adjacentNorth,
            adjacentWest,
            adjacentEast,
            6.0,
            8.0,
            10.0);

        string xml =
            $$"""
            <?xml version="1.0" encoding="UTF-8"?>
            <core:CityModel xmlns:core="http://www.opengis.net/citygml/2.0" xmlns:gml="http://www.opengis.net/gml" xmlns:dem="http://www.opengis.net/citygml/relief/2.0">
              <gml:boundedBy>
                <gml:Envelope srsName="http://www.opengis.net/def/crs/EPSG/0/6697" srsDimension="3">
                  <gml:lowerCorner>{{requestedSouth.ToString("F8", CultureInfo.InvariantCulture)}} {{requestedWest.ToString("F8", CultureInfo.InvariantCulture)}} 0</gml:lowerCorner>
                  <gml:upperCorner>{{adjacentNorth.ToString("F8", CultureInfo.InvariantCulture)}} {{adjacentEast.ToString("F8", CultureInfo.InvariantCulture)}} 20</gml:upperCorner>
                </gml:Envelope>
              </gml:boundedBy>
              <core:cityObjectMember>
                <dem:ReliefFeature gml:id="dem-parent">
                  <gml:name>Parent Relief</gml:name>
                  <dem:reliefComponent>
                    <dem:TINRelief gml:id="dem-parent-component">
                      <dem:tin>
                        <gml:TriangulatedSurface>
                          <gml:trianglePatches>
                            <gml:Triangle gml:id="tri-requested">
                              <gml:exterior>
                                <gml:LinearRing gml:id="ring-requested">
                                  <gml:posList>{{requestedTriangle}}</gml:posList>
                                </gml:LinearRing>
                              </gml:exterior>
                            </gml:Triangle>
                            <gml:Triangle gml:id="tri-adjacent">
                              <gml:exterior>
                                <gml:LinearRing gml:id="ring-adjacent">
                                  <gml:posList>{{adjacentTriangle}}</gml:posList>
                                </gml:LinearRing>
                              </gml:exterior>
                            </gml:Triangle>
                          </gml:trianglePatches>
                        </gml:TriangulatedSurface>
                      </dem:tin>
                    </dem:TINRelief>
                  </dem:reliefComponent>
                </dem:ReliefFeature>
              </core:cityObjectMember>
            </core:CityModel>
            """;

        File.WriteAllText(Path.Combine(packageDirectory, $"plateau_tokyo23ku_dem_{requestedMeshCode}_parent.gml"), xml);
    }

    private static void CreateRuntimeNamedParentMeshDemFixture(string datasetRoot, string requestedMeshCode, string adjacentMeshCode)
    {
        string packageDirectory = Path.Combine(datasetRoot, "udx", "dem", requestedMeshCode);
        Directory.CreateDirectory(packageDirectory);

        (double requestedSouth, double requestedNorth, double requestedWest, double requestedEast) = GetMeshBounds(requestedMeshCode);
        (double adjacentSouth, double adjacentNorth, double adjacentWest, double adjacentEast) = GetMeshBounds(adjacentMeshCode);

        string requestedTriangle = CreateTrianglePosList(
            requestedSouth,
            requestedNorth,
            requestedWest,
            requestedEast,
            5.0,
            7.0,
            9.0);
        string adjacentTriangle = CreateTrianglePosList(
            adjacentSouth,
            adjacentNorth,
            adjacentWest,
            adjacentEast,
            6.0,
            8.0,
            10.0);

        string xml =
            $$"""
            <?xml version="1.0" encoding="UTF-8"?>
            <core:CityModel xmlns:core="http://www.opengis.net/citygml/2.0" xmlns:gml="http://www.opengis.net/gml" xmlns:dem="http://www.opengis.net/citygml/relief/2.0">
              <gml:boundedBy>
                <gml:Envelope srsName="http://www.opengis.net/def/crs/EPSG/0/6697" srsDimension="3">
                  <gml:lowerCorner>{{requestedSouth.ToString("F8", CultureInfo.InvariantCulture)}} {{requestedWest.ToString("F8", CultureInfo.InvariantCulture)}} 0</gml:lowerCorner>
                  <gml:upperCorner>{{adjacentNorth.ToString("F8", CultureInfo.InvariantCulture)}} {{adjacentEast.ToString("F8", CultureInfo.InvariantCulture)}} 20</gml:upperCorner>
                </gml:Envelope>
              </gml:boundedBy>
              <core:cityObjectMember>
                <dem:ReliefFeature gml:id="dem-{{requestedMeshCode}}">
                  <gml:name>{{requestedMeshCode}}</gml:name>
                  <dem:reliefComponent>
                    <dem:TINRelief gml:id="dem-{{requestedMeshCode}}-component">
                      <gml:name>{{requestedMeshCode}}</gml:name>
                      <dem:tin>
                        <gml:TriangulatedSurface>
                          <gml:trianglePatches>
                            <gml:Triangle gml:id="tri-{{requestedMeshCode}}">
                              <gml:exterior>
                                <gml:LinearRing gml:id="ring-{{requestedMeshCode}}">
                                  <gml:posList>{{requestedTriangle}}</gml:posList>
                                </gml:LinearRing>
                              </gml:exterior>
                            </gml:Triangle>
                          </gml:trianglePatches>
                        </gml:TriangulatedSurface>
                      </dem:tin>
                    </dem:TINRelief>
                  </dem:reliefComponent>
                </dem:ReliefFeature>
              </core:cityObjectMember>
              <core:cityObjectMember>
                <dem:ReliefFeature gml:id="dem-{{adjacentMeshCode}}">
                  <gml:name>{{adjacentMeshCode}}</gml:name>
                  <dem:reliefComponent>
                    <dem:TINRelief gml:id="dem-{{adjacentMeshCode}}-component">
                      <gml:name>{{adjacentMeshCode}}</gml:name>
                      <dem:tin>
                        <gml:TriangulatedSurface>
                          <gml:trianglePatches>
                            <gml:Triangle gml:id="tri-{{adjacentMeshCode}}">
                              <gml:exterior>
                                <gml:LinearRing gml:id="ring-{{adjacentMeshCode}}">
                                  <gml:posList>{{adjacentTriangle}}</gml:posList>
                                </gml:LinearRing>
                              </gml:exterior>
                            </gml:Triangle>
                          </gml:trianglePatches>
                        </gml:TriangulatedSurface>
                      </dem:tin>
                    </dem:TINRelief>
                  </dem:reliefComponent>
                </dem:ReliefFeature>
              </core:cityObjectMember>
            </core:CityModel>
            """;

        File.WriteAllText(Path.Combine(packageDirectory, $"plateau_tokyo23ku_dem_{requestedMeshCode}_named-parent.gml"), xml);
    }

    private static (double South, double North, double West, double East) GetMeshBounds(string meshCode)
    {
        Assert.True(PlateauMeshCode.TryGetBounds(meshCode, out (double SouthLatitude, double NorthLatitude, double WestLongitude, double EastLongitude) bounds));
        return (bounds.SouthLatitude, bounds.NorthLatitude, bounds.WestLongitude, bounds.EastLongitude);
    }

    private static string CreateTrianglePosList(
        double southLatitude,
        double northLatitude,
        double westLongitude,
        double eastLongitude,
        double heightA,
        double heightB,
        double heightC)
    {
        double latitude0 = southLatitude + ((northLatitude - southLatitude) * 0.15);
        double latitude1 = southLatitude + ((northLatitude - southLatitude) * 0.85);
        double longitude0 = westLongitude + ((eastLongitude - westLongitude) * 0.15);
        double longitude1 = westLongitude + ((eastLongitude - westLongitude) * 0.85);

        return string.Join(
            ' ',
            [
                latitude0.ToString("F8", CultureInfo.InvariantCulture),
                longitude0.ToString("F8", CultureInfo.InvariantCulture),
                heightA.ToString("F3", CultureInfo.InvariantCulture),
                latitude1.ToString("F8", CultureInfo.InvariantCulture),
                longitude0.ToString("F8", CultureInfo.InvariantCulture),
                heightB.ToString("F3", CultureInfo.InvariantCulture),
                latitude1.ToString("F8", CultureInfo.InvariantCulture),
                longitude1.ToString("F8", CultureInfo.InvariantCulture),
                heightC.ToString("F3", CultureInfo.InvariantCulture),
                latitude0.ToString("F8", CultureInfo.InvariantCulture),
                longitude0.ToString("F8", CultureInfo.InvariantCulture),
                heightA.ToString("F3", CultureInfo.InvariantCulture),
            ]);
    }

    private static string CreateTrianglePosListFromRatios(
        double southLatitude,
        double northLatitude,
        double westLongitude,
        double eastLongitude,
        (double LatitudeRatio, double LongitudeRatio, double Height) vertex0,
        (double LatitudeRatio, double LongitudeRatio, double Height) vertex1,
        (double LatitudeRatio, double LongitudeRatio, double Height) vertex2)
    {
        return CreatePosList(
            southLatitude,
            northLatitude,
            westLongitude,
            eastLongitude,
            [vertex0, vertex1, vertex2, vertex0]);
    }

    private static string CreatePolygonPosListFromRatios(
        double southLatitude,
        double northLatitude,
        double westLongitude,
        double eastLongitude,
        IReadOnlyList<(double LatitudeRatio, double LongitudeRatio, double Height)> vertices)
    {
        return CreatePosList(
            southLatitude,
            northLatitude,
            westLongitude,
            eastLongitude,
            [.. vertices, vertices[0]]);
    }

    private static string CreatePosList(
        double southLatitude,
        double northLatitude,
        double westLongitude,
        double eastLongitude,
        IReadOnlyList<(double LatitudeRatio, double LongitudeRatio, double Height)> vertices)
    {
        return string.Join(
            ' ',
            vertices.SelectMany(vertex => new[]
            {
                Interpolate(southLatitude, northLatitude, vertex.LatitudeRatio).ToString("F8", CultureInfo.InvariantCulture),
                Interpolate(westLongitude, eastLongitude, vertex.LongitudeRatio).ToString("F8", CultureInfo.InvariantCulture),
                vertex.Height.ToString("F3", CultureInfo.InvariantCulture),
            }));
    }

    private static double Interpolate(double min, double max, double ratio)
    {
        return min + ((max - min) * ratio);
    }

    private static SurfaceMeshTessellation TessellateSurfaceForTest(
        ParsedSurface surface,
        string packageName,
        GeodeticPoint cityObjectOrigin,
        GeographicLib.LocalCartesian cartesian,
        double minimumY = 0.0,
        double? maximumY = null,
        double? floorHeightMeters = null,
        int floorCount = 1)
    {
        FacadeUvProjectionContext? context = string.Equals(packageName, "bldg", StringComparison.Ordinal)
            || string.Equals(packageName, "ubld", StringComparison.Ordinal)
                ? new FacadeUvProjectionContext(
                    minimumY,
                    maximumY ?? minimumY + (floorHeightMeters ?? FacadeFloorMetrics.DefaultFloorUnitMeters) * floorCount,
                    floorHeightMeters ?? FacadeFloorMetrics.DefaultFloorUnitMeters,
                    floorCount)
                : null;
        ResolvedMaterial material = new(
            MaterialType.Standard,
            TexturePayload: null,
            TextureSourceKind.Bundled,
            MaterialProjection.Uv,
            Family: null,
            TextureScale: null,
            ReuseScope: MaterialReuseScope.PerObject);
        return CityGmlSurfaceMeshTessellator.Tessellate(new SurfaceMeshTessellationRequest(
            packageName,
            new ConstructionFace(surface, ConstructionCityObjectDraft.ResolveRole(surface)),
            material,
            cityObjectOrigin,
            cartesian,
            context,
            DemUvProjection: null));
    }

    private static MeshVertex[] SelectVerticesByX(MeshVertex[] vertices, double x)
    {
        return vertices
            .Where(vertex => Math.Abs(vertex.Position.X - x) < 1e-5)
            .ToArray();
    }

    private static MeshVertex[] SelectVerticesByY(MeshVertex[] vertices, double y)
    {
        return vertices
            .Where(vertex => Math.Abs(vertex.Position.Y - y) < 1e-5)
            .ToArray();
    }

    private static double AverageUvYAtY(MeshVertex[] vertices, double y)
    {
        MeshVertex[] selectedVertices = SelectVerticesByY(vertices, y);
        Assert.NotEmpty(selectedVertices);
        double average = selectedVertices.Average(static vertex => vertex.UV0.Y);
        Assert.All(selectedVertices, vertex => Assert.InRange(Math.Abs(vertex.UV0.Y - average), 0.0, 1e-5));
        return average;
    }
    private static ParsedCityObject CreateParsedCityObject(
        string packageName,
        ParsedSurface[] surfaces,
        CoordinateReferenceSystem referenceSystem,
        int? lodLevel = 1,
        int? floorsAboveGround = null,
        double? measuredHeightMeters = null,
        BuildingAttributeContext? buildingAttributes = null)
    {
        return new ParsedCityObject(
            SlotKey: $"{packageName}-slot",
            DisplayName: $"{packageName}-display",
            PackageName: packageName,
            ActualMeshCode: "53394525",
            LodLevel: lodLevel,
            Surfaces: surfaces,
            ReferenceSystem: referenceSystem,
            SourceFileRelativePath: $"udx/{packageName}/53394525/{packageName}.gml",
            SharedAcrossMeshCodes: false,
            FloorsAboveGround: floorsAboveGround,
            MeasuredHeightMeters: measuredHeightMeters,
            BuildingAttributes: buildingAttributes ?? BuildingAttributeContext.Empty);
    }

    private static BuildingAttributeContext CreateBuildingAttributes(
        CityGmlRoofShape roofShape,
        PlateauBuildingUse use = PlateauBuildingUse.Unknown,
        PlateauBuildingStructure structure = PlateauBuildingStructure.Unknown)
    {
        return BuildingAttributeContext.Empty with
        {
            RoofShape = new BuildingCodeValue<CityGmlRoofShape>(roofShape, CreateRoofTypeCode(roofShape)),
            Uses = use == PlateauBuildingUse.Unknown ? [] : [new BuildingCodeValue<PlateauBuildingUse>(use, CreateUsageCode(use))],
            Structures = structure == PlateauBuildingStructure.Unknown ? [] : [new BuildingCodeValue<PlateauBuildingStructure>(structure, CreateStructureTypeCode(structure))],
        };
    }

    private static string CreateRoofTypeCode(CityGmlRoofShape roofShape)
    {
        return roofShape switch
        {
            CityGmlRoofShape.Gable => "1",
            CityGmlRoofShape.Hip => "2",
            CityGmlRoofShape.Pyramid => "3",
            CityGmlRoofShape.Flat => "4",
            CityGmlRoofShape.Shed => "5",
            CityGmlRoofShape.HalfHip => "6",
            CityGmlRoofShape.Irimoya => "7",
            CityGmlRoofShape.Mansard => "9",
            CityGmlRoofShape.Sawtooth => "14",
            CityGmlRoofShape.Gambrel => "21",
            CityGmlRoofShape.Arch => "23",
            CityGmlRoofShape.Dome => "24",
            CityGmlRoofShape.Other => "28",
            _ => "9999",
        };
    }

    private static string CreateUsageCode(PlateauBuildingUse use)
    {
        return use switch
        {
            PlateauBuildingUse.DetachedResidential => "411",
            PlateauBuildingUse.Apartment => "412",
            PlateauBuildingUse.MixedResidential => "413",
            PlateauBuildingUse.Office => "401",
            PlateauBuildingUse.Commercial => "402",
            PlateauBuildingUse.Warehouse => "431",
            PlateauBuildingUse.Factory => "441",
            PlateauBuildingUse.Public => "421",
            PlateauBuildingUse.Education => "422",
            PlateauBuildingUse.Other => "461",
            _ => "9999",
        };
    }

    private static string CreateStructureTypeCode(PlateauBuildingStructure structure)
    {
        return structure switch
        {
            PlateauBuildingStructure.Wood => "601",
            PlateauBuildingStructure.SteelReinforcedConcrete => "602",
            PlateauBuildingStructure.ReinforcedConcrete => "603",
            PlateauBuildingStructure.Steel => "604",
            PlateauBuildingStructure.LightweightSteel => "605",
            PlateauBuildingStructure.ConcreteBlock => "606",
            PlateauBuildingStructure.NonWood => "610",
            _ => "9999",
        };
    }

    private static ParsedSurface CreateParsedSurface(
        string polygonId,
        ParsedSurfaceSemantic semantic,
        IReadOnlyList<GeodeticPoint> vertices,
        TexturePayload? texturePayload = null,
        ColorRgba? baseColor = null,
        IReadOnlyList<Float2>? uvs = null)
    {
        return new ParsedSurface(
            Semantic: semantic,
            ExteriorRing: new ParsedRing(vertices.ToArray(), UVs: uvs),
            InteriorRings: [],
            BaseColor: baseColor ?? new ColorRgba(1.0, 1.0, 1.0, 1.0),
            TexturePayload: texturePayload);
    }

    private static bool ApproximatelyEqualFloat2(Float2 left, Float2 right, double tolerance)
    {
        return Math.Abs(left.X - right.X) <= tolerance && Math.Abs(left.Y - right.Y) <= tolerance;
    }

    private static IReadOnlyList<GeodeticPoint> CreateHorizontalQuadVertices(
        GeodeticPoint origin,
        double altitudeMeters,
        double sizeMeters,
        bool reverseWinding)
    {
        double latitudeDelta = sizeMeters / 111320.0;
        double longitudeDelta = sizeMeters / (111320.0 * Math.Cos(origin.Latitude * (Math.PI / 180.0)));
        List<GeodeticPoint> vertices =
        [
            new(origin.Latitude, origin.Longitude, altitudeMeters),
            new(origin.Latitude, origin.Longitude + longitudeDelta, altitudeMeters),
            new(origin.Latitude + latitudeDelta, origin.Longitude + longitudeDelta, altitudeMeters),
            new(origin.Latitude + latitudeDelta, origin.Longitude, altitudeMeters),
        ];

        if (reverseWinding)
        {
            vertices.Reverse();
        }

        vertices.Add(vertices[0]);
        return vertices;
    }

    private static IReadOnlyList<GeodeticPoint> CreateMeshRelativeQuadVertices(
        string meshCode,
        double altitudeMeters,
        double minRatio,
        double maxRatio,
        bool reverseWinding)
    {
        (double south, double north, double west, double east) = GetMeshBounds(meshCode);
        List<GeodeticPoint> vertices =
        [
            new(south + ((north - south) * minRatio), west + ((east - west) * minRatio), altitudeMeters),
            new(south + ((north - south) * minRatio), west + ((east - west) * maxRatio), altitudeMeters),
            new(south + ((north - south) * maxRatio), west + ((east - west) * maxRatio), altitudeMeters),
            new(south + ((north - south) * maxRatio), west + ((east - west) * minRatio), altitudeMeters),
        ];

        if (reverseWinding)
        {
            vertices.Reverse();
        }

        vertices.Add(vertices[0]);
        return vertices;
    }

    private static IReadOnlyList<GeodeticPoint> CreateMeshRelativeRectangleVertices(
        string meshCode,
        double altitudeMeters,
        double minLatitudeRatio,
        double maxLatitudeRatio,
        double minLongitudeRatio,
        double maxLongitudeRatio,
        bool reverseWinding)
    {
        (double south, double north, double west, double east) = GetMeshBounds(meshCode);
        List<GeodeticPoint> vertices =
        [
            new(south + ((north - south) * minLatitudeRatio), west + ((east - west) * minLongitudeRatio), altitudeMeters),
            new(south + ((north - south) * minLatitudeRatio), west + ((east - west) * maxLongitudeRatio), altitudeMeters),
            new(south + ((north - south) * maxLatitudeRatio), west + ((east - west) * maxLongitudeRatio), altitudeMeters),
            new(south + ((north - south) * maxLatitudeRatio), west + ((east - west) * minLongitudeRatio), altitudeMeters),
        ];

        if (reverseWinding)
        {
            vertices.Reverse();
        }

        vertices.Add(vertices[0]);
        return vertices;
    }

    private static IReadOnlyList<GeodeticPoint> CreateMeshEdgeWallVertices(
        string meshCode,
        double altitudeMeters,
        double heightMeters,
        double ratio)
    {
        (double south, double north, double west, double east) = GetMeshBounds(meshCode);
        double latitude = south + ((north - south) * ratio);
        GeodeticPoint bottom0 = new(latitude, west + ((east - west) * 0.45), altitudeMeters);
        GeodeticPoint bottom1 = new(latitude, west + ((east - west) * 0.55), altitudeMeters);
        GeodeticPoint top1 = bottom1 with { Altitude = altitudeMeters + heightMeters };
        GeodeticPoint top0 = bottom0 with { Altitude = altitudeMeters + heightMeters };
        return [bottom0, bottom1, top1, top0, bottom0];
    }

    private static IReadOnlyList<GeodeticPoint> CreateMatsumotoLod1SolidHorizontalRing(
        double altitudeMeters)
    {
        return
        [
            new(36.231592650728494, 137.97499237251273, altitudeMeters),
            new(36.231590994539204, 137.97504154730566, altitudeMeters),
            new(36.23162464596417, 137.97504328807037, altitudeMeters),
            new(36.23162469728465, 137.9750418382012, altitudeMeters),
            new(36.23168179137365, 137.97505169002244, altitudeMeters),
            new(36.231683315279405, 137.97498571800617, altitudeMeters),
            new(36.23165163047712, 137.97498460155376, altitudeMeters),
            new(36.231620443262884, 137.97498350402472, altitudeMeters),
            new(36.231620204515266, 137.97499379469681, altitudeMeters),
            new(36.231592650728494, 137.97499237251273, altitudeMeters),
        ];
    }

    private static GeodeticPoint CreateMeshCenterPoint(
        string meshCode,
        double altitudeMeters)
    {
        (double south, double north, double west, double east) = GetMeshBounds(meshCode);
        return new GeodeticPoint((south + north) / 2.0, (west + east) / 2.0, altitudeMeters);
    }

    private static IReadOnlyList<GeodeticPoint> CreateBoundsQuadVertices(
        double south,
        double north,
        double west,
        double east,
        double altitudeMeters)
    {
        return
        [
            new(south, west, altitudeMeters),
            new(south, east, altitudeMeters),
            new(north, east, altitudeMeters),
            new(north, west, altitudeMeters),
            new(south, west, altitudeMeters),
        ];
    }

    private static IEnumerable<TerrainGridSampleCoverage> EnumerateBoundaryCoverage(TerrainGridGeometry geometry)
    {
        for (int column = 0; column < geometry.Width; column++)
        {
            yield return geometry.SampleCoverage[column];
            yield return geometry.SampleCoverage[((geometry.Height - 1) * geometry.Width) + column];
        }

        for (int row = 0; row < geometry.Height; row++)
        {
            yield return geometry.SampleCoverage[row * geometry.Width];
            yield return geometry.SampleCoverage[(row * geometry.Width) + geometry.Width - 1];
        }
    }

    private static double EstimateProjectedLongitudeSpanMeters(
        double south,
        double north,
        double west,
        double east,
        GeodeticPoint origin,
        GeographicLib.LocalCartesian cartesian)
    {
        double latitude = (south + north) / 2.0;
        Float3 westPosition = CreateScenePosition(new GeodeticPoint(latitude, west, origin.Altitude), origin, cartesian);
        Float3 eastPosition = CreateScenePosition(new GeodeticPoint(latitude, east, origin.Altitude), origin, cartesian);
        return Math.Abs(eastPosition.X - westPosition.X);
    }

    private static double EstimateProjectedLatitudeSpanMeters(
        double south,
        double north,
        double west,
        double east,
        GeodeticPoint origin,
        GeographicLib.LocalCartesian cartesian)
    {
        double longitude = (west + east) / 2.0;
        Float3 southPosition = CreateScenePosition(new GeodeticPoint(south, longitude, origin.Altitude), origin, cartesian);
        Float3 northPosition = CreateScenePosition(new GeodeticPoint(north, longitude, origin.Altitude), origin, cartesian);
        return Math.Abs(northPosition.Z - southPosition.Z);
    }

    private static Float3 CreateScenePosition(
        GeodeticPoint point,
        GeodeticPoint origin,
        GeographicLib.LocalCartesian cartesian)
    {
        return SceneAxisMapper.CreatePosition(
            point.Latitude,
            point.Longitude,
            point.Altitude,
            origin.Latitude,
            origin.Longitude,
            origin.Altitude,
            cartesian);
    }

    private static TerrainTextureOverlay CreateThirdMeshOverlay(string meshCode)
    {
        (double south, double north, double west, double east) = GetMeshBounds(meshCode);
        return new TerrainTextureOverlay(
            PackageName: "dem",
            MeshCode: ThirdRegionalMeshCode.Parse(meshCode),
            UrlTemplate: $"https://terrain.example/{meshCode}/{{z}}/{{x}}/{{y}}.png",
            ZoomLevel: 17,
            GeographicBounds: new GeographicRectangle(south, north, west, east),
            MaxTextureSize: 512);
    }

    private static IReadOnlyList<GeodeticPoint> CreateVerticalQuadVertices(
        GeodeticPoint origin,
        double widthMeters,
        double heightMeters)
    {
        double longitudeDelta = widthMeters / (111320.0 * Math.Cos(origin.Latitude * (Math.PI / 180.0)));
        return
        [
            origin,
            new(origin.Latitude, origin.Longitude + longitudeDelta, origin.Altitude),
            new(origin.Latitude, origin.Longitude + longitudeDelta, origin.Altitude + heightMeters),
            new(origin.Latitude, origin.Longitude, origin.Altitude + heightMeters),
            origin,
        ];
    }

    private static TexturePayload CreateTexturePayload(string identity)
    {
        return new RawRgba32TexturePayload(1, 1, "sRGB", [255, 255, 255, 255], identity);
    }

    private static HashSet<ParsedSurface> GetculledSurfacesBeforeProjectionForTest(
        string packageName,
        IEnumerable<ParsedSurface> surfaces,
        GeodeticPoint cityObjectOrigin,
        GeographicLib.LocalCartesian cartesian)
    {
        ConstructionCityObjectDraft draft = ConstructionCityObjectDraft.FromParsedCityObject(CreateParsedCityObject(
            packageName,
            surfaces.ToArray(),
            CoordinateReferenceSystem.Parse("EPSG:4326")));
        return new HashSet<ParsedSurface>(
            CityGmlSurfaceProjectionPolicy.GetCulledSurfacesBeforeProjection(
                draft,
                cityObjectOrigin,
                cartesian),
            ReferenceEqualityComparer.Instance);
    }

    private static MaterialBinding[] CreateCommonMaterialBindingsForTest(
        ParsedCityObject cityObject,
        GeodeticPoint cityObjectOrigin,
        GeographicLib.LocalCartesian cartesian)
    {
        return CityGmlSurfaceMaterialResolver.CreateSharedCommonMaterialBindings(
            ConstructionCityObjectDraft.FromParsedCityObject(cityObject),
            cityObjectOrigin,
            cartesian,
            demTerrainTextureOverlay: null,
            new DefaultMaterialResolver(CommonMaterialCatalog.Create()));
    }

    private static bool IsBuildingFacadeMaterial(MaterialBinding material)
    {
        return material.Family is not null
            && BundledDefaultMaterialFamilies.BuildingFacadeFamilies.Contains(material.Family);
    }

    private static ImportedCityObject ProjectTerrainMeshModeCityObjectForTest(
        ParsedCityObject cityObject,
        GeodeticPoint globalOriginPoint,
        PlateauImportRequest request)
    {
        IReadOnlyList<MeshCodeBounds> requestedMeshCodeBounds =
            MeshCodeBounds.TryParse(request.MeshCode) is { } parsedRequestedMeshCodeBounds
                ? [parsedRequestedMeshCodeBounds]
                : [];

        return CityGmlParsedCityObjectProjection.ProjectTerrainMeshModeCityObject(
            GeneratedLod1RoofCityObjectFactory.CreateDraft(cityObject),
            demTerrainGridSamplingSource: null,
            globalOriginPoint,
            globalCartesian: null,
            demTerrainTextureOverlay: null,
            request,
            requestedMeshCodeBounds,
            new DefaultMaterialResolver(CommonMaterialCatalog.Create()),
            progressReporter: null,
            CancellationToken.None);
    }

    private static void AssertGeneratedUpperFacadeTrianglesFaceOutward(
        ImportedMesh mesh,
        MeshSubmesh submesh,
        double baseHeight)
    {
        int checkedTriangleCount = 0;
        for (int index = 0; index + 2 < submesh.TriangleVertexIndices.Count; index += 3)
        {
            MeshVertex first = mesh.Vertices[submesh.TriangleVertexIndices[index]];
            MeshVertex second = mesh.Vertices[submesh.TriangleVertexIndices[index + 1]];
            MeshVertex third = mesh.Vertices[submesh.TriangleVertexIndices[index + 2]];
            if (new[] { first, second, third }.Max(static vertex => vertex.Position.Y) <= baseHeight + 0.1)
            {
                continue;
            }

            Float3 normal = Normalize(Cross(
                Subtract(second.Position, first.Position),
                Subtract(third.Position, first.Position)));
            Float3 centroid = new(
                (first.Position.X + second.Position.X + third.Position.X) / 3.0,
                (first.Position.Y + second.Position.Y + third.Position.Y) / 3.0,
                (first.Position.Z + second.Position.Z + third.Position.Z) / 3.0);
            Float3 outward = new(centroid.X, 0.0, centroid.Z);
            if (Magnitude(outward) < 1e-6)
            {
                continue;
            }

            checkedTriangleCount++;
            Assert.True(
                Dot(new Float3(normal.X, 0.0, normal.Z), outward) > 0.0,
                $"Expected generated upper facade triangle to face outward. normal=({normal.X:F3},{normal.Y:F3},{normal.Z:F3}), centroid=({centroid.X:F3},{centroid.Y:F3},{centroid.Z:F3}).");
        }

        Assert.True(checkedTriangleCount > 0, "Expected at least one generated upper facade triangle to check.");
    }

    private static void AssertGeneratedUpperRoofTrianglesFaceUpward(
        ImportedMesh mesh,
        MeshSubmesh submesh,
        double baseHeight)
    {
        int checkedTriangleCount = 0;
        for (int index = 0; index + 2 < submesh.TriangleVertexIndices.Count; index += 3)
        {
            MeshVertex first = mesh.Vertices[submesh.TriangleVertexIndices[index]];
            MeshVertex second = mesh.Vertices[submesh.TriangleVertexIndices[index + 1]];
            MeshVertex third = mesh.Vertices[submesh.TriangleVertexIndices[index + 2]];
            if (new[] { first, second, third }.Max(static vertex => vertex.Position.Y) <= baseHeight + 0.1)
            {
                continue;
            }

            Float3 normal = Normalize(Cross(
                Subtract(second.Position, first.Position),
                Subtract(third.Position, first.Position)));
            checkedTriangleCount++;
            Assert.True(
                normal.Y > 0.0,
                $"Expected generated roof triangle to face upward. normal=({normal.X:F3},{normal.Y:F3},{normal.Z:F3}).");
        }

        Assert.True(checkedTriangleCount > 0, "Expected at least one generated upper roof triangle to check.");
    }

    private static Float3 Subtract(Float3 left, Float3 right)
        => new(left.X - right.X, left.Y - right.Y, left.Z - right.Z);

    private static Float3 Cross(Float3 left, Float3 right)
        => new(
            (left.Y * right.Z) - (left.Z * right.Y),
            (left.Z * right.X) - (left.X * right.Z),
            (left.X * right.Y) - (left.Y * right.X));

    private static double Dot(Float3 left, Float3 right)
        => (left.X * right.X) + (left.Y * right.Y) + (left.Z * right.Z);

    private static double Magnitude(Float3 vector)
        => Math.Sqrt(Dot(vector, vector));

    private static Float3 Normalize(Float3 vector)
    {
        double magnitude = Magnitude(vector);
        return new Float3(vector.X / magnitude, vector.Y / magnitude, vector.Z / magnitude);
    }

    private sealed class StubSceneSink : ISceneSink
    {
        public List<ImportedCityObject> CityObjects { get; } = [];

        public async Task<SceneImportExecutionResult> ExecuteAsync(
            SceneImportExecutionPlan plan,
            IAsyncEnumerable<ImportedObjectUnit> objectUnits,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _ = plan;
            await foreach (ImportedObjectUnit objectUnit in objectUnits.WithCancellation(cancellationToken))
            {
                CityObjects.AddRange(objectUnit.CityObjects);
            }

            return new SceneImportExecutionResult(["stub://resonite"], CityObjects.Count);
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }

}
