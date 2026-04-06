using System.Diagnostics.CodeAnalysis;

using Plateau.ResoniteLink.Application.Importing;
using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Tests.Application;

[SuppressMessage(
    "Reliability",
    "CA2000:Dispose objects before losing scope",
    Justification = "The tests intentionally hand ownership to PlateauImportService or helper methods.")]
public sealed class PlateauImportServiceTests
{
    [Fact]
    public async Task ExecuteAsyncBuildsNormalizedScene()
    {
        StubResoniteSceneBuilder sceneBuilder = new();
        PlateauImportService service = new(sceneBuilder);
        string fixturePath = TestData.GetFixturePath("LocalPlateauDataset");

        ImportExecutionResult result = await service.ExecuteAsync(
            new PlateauImportRequest(
                Dataset: " tokyo23ku ",
                MeshCode: " 53394525 ",
                SourceKind: DatasetSourceKind.Local,
                LocalSourcePath: fixturePath,
                ServerUri: null),
            workRoot: "runtime/resonite");
        CapturedResoniteScene scene = result.Metadata.ToScene(sceneBuilder.CityObjects);

        Assert.Equal("PLATEAU tokyo23ku 53394525", result.Metadata.WorldName);
        Assert.Equal("tokyo23ku", result.Metadata.Request.Dataset);
        Assert.Equal("53394525", result.Metadata.Request.MeshCode);
        Assert.Equal(["bldg"], result.Metadata.SourceDataset.PackageNames);
        Assert.Contains(
            "udx/bldg/53394525/plateau_tokyo23ku_bldg_53394525.gml",
            result.Metadata.SourceDataset.SourceFiles);
        Assert.Equal("PLATEAU Open Data Terms", result.Metadata.Attribution.DatasetLicense.LicenseName);
        Assert.Equal("https://www.mlit.go.jp/plateau/site-policy/", result.Metadata.Attribution.DatasetLicense.LicenseUrl);
        Assert.Contains("provide source attribution", result.Metadata.Attribution.DatasetLicense.CreditText, StringComparison.Ordinal);
        Assert.Empty(result.Metadata.Attribution.MaterialLicenses);
        Assert.Equal(2, scene.CityObjects.Count);
        ResoniteConstructionCityObject buildingOne = Assert.Single(
            scene.CityObjects,
            static cityObject => cityObject.DisplayName == "Building One");
        Assert.Equal("bldg", buildingOne.PackageName);
        Assert.Equal(2, buildingOne.Materials.Count);
        Assert.Contains(
            buildingOne.Materials,
            static material => material.TexturePath == "udx/bldg/53394525/appearance/roof.png");
        Assert.Contains(
            buildingOne.Materials,
            static material =>
                BundledDefaultMaterialFamilies.FacadeVariants.Contains(material.TexturePath!)
                && material.TextureSourceKind == ResoniteTextureSourceKind.Bundled
                && material.Projection == ResoniteMaterialProjection.Uv);
        Assert.Equal("stub://resonite", Assert.Single(result.Destinations));
    }

    [Fact]
    public async Task ExecuteAsyncBuildsCityObjectsAcrossPackagesAndKeepsDetailedModelTextures()
    {
        StubResoniteSceneBuilder sceneBuilder = new();
        PlateauImportService service = new(sceneBuilder);
        string fixturePath = TestData.GetFixturePath("LocalPlateauDatasetMixedObjects");

        ImportExecutionResult result = await service.ExecuteAsync(
            new PlateauImportRequest(
                Dataset: "tokyo23ku",
                MeshCode: "53394525",
                SourceKind: DatasetSourceKind.Local,
                LocalSourcePath: fixturePath,
                ServerUri: null),
            workRoot: "runtime/resonite");
        CapturedResoniteScene scene = result.Metadata.ToScene(sceneBuilder.CityObjects);

        Assert.Equal(["bldg", "dem", "luse", "tran"], result.Metadata.SourceDataset.PackageNames);
        Assert.Contains(
            "udx/tran/53394525/plateau_tokyo23ku_tran_53394525.gml",
            result.Metadata.SourceDataset.SourceFiles);
        Assert.Contains(
            "udx/luse/53394525/plateau_tokyo23ku_luse_53394525.gml",
            result.Metadata.SourceDataset.SourceFiles);
        Assert.Contains(
            "udx/dem/53394525/plateau_tokyo23ku_dem_53394525.gml",
            result.Metadata.SourceDataset.SourceFiles);
        Assert.Equal(5, scene.CityObjects.Count);

        ResoniteConstructionCityObject road = Assert.Single(
            scene.CityObjects,
            static cityObject => cityObject.DisplayName == "Road Segment One");
        Assert.Equal("tran", road.PackageName);
        ResoniteMaterialBinding texturedMaterial = Assert.Single(
            road.Materials,
            static material => material.TextureSourceKind == ResoniteTextureSourceKind.Dataset);
        Assert.Equal("udx/tran/53394525/appearance/road.png", texturedMaterial.TexturePath);
        Assert.Equal(ResoniteMaterialProjection.Uv, texturedMaterial.Projection);

        ResoniteConstructionCityObject landUse = Assert.Single(
            scene.CityObjects,
            static cityObject => cityObject.DisplayName == "Land Use One");
        Assert.Equal("luse", landUse.PackageName);
        Assert.All(
            landUse.Materials,
            static material =>
            {
                Assert.Equal(ResoniteMaterialType.Wireframe, material.MaterialType);
                Assert.Null(material.TexturePath);
                Assert.Equal(ResoniteTextureSourceKind.Bundled, material.TextureSourceKind);
                Assert.Equal(ResoniteMaterialProjection.Uv, material.Projection);
            });

        ResoniteConstructionCityObject relief = Assert.Single(
            scene.CityObjects,
            static cityObject => cityObject.DisplayName == "Relief One");
        Assert.Equal("dem", relief.PackageName);
        Assert.Equal(LocalCityGmlResonitePlanBuilder.DefaultDemTerrainTexturePath, Assert.Single(relief.Materials).TexturePath);
        Assert.Equal(ResoniteTextureSourceKind.Bundled, Assert.Single(relief.Materials).TextureSourceKind);
        Assert.Equal(ResoniteMaterialProjection.Uv, Assert.Single(relief.Materials).Projection);
        TerrainTextureOverlay demTerrainTexture = Assert.Single(result.Metadata.SourceDataset.TerrainTextureOverlays);
        Assert.Equal("dem", demTerrainTexture.PackageName);
        Assert.Equal(LocalCityGmlResonitePlanBuilder.DefaultDemTerrainTexturePath, demTerrainTexture.TexturePath);
        Assert.Equal(LocalCityGmlResonitePlanBuilder.DefaultDemTerrainTextureUrlTemplate, demTerrainTexture.UrlTemplate);
        Assert.Equal(LocalCityGmlResonitePlanBuilder.DefaultDemTerrainTextureZoomLevel, demTerrainTexture.ZoomLevel);
        Assert.Equal(LocalCityGmlResonitePlanBuilder.DefaultDemTerrainTextureMaxSize, demTerrainTexture.MaxTextureSize);
        Assert.All(relief.Mesh.Vertices, static vertex =>
        {
            Assert.InRange(vertex.UV0.X, 0.0, 1.0);
            Assert.InRange(vertex.UV0.Y, 0.0, 1.0);
        });
        Assert.Contains(relief.Mesh.Vertices, static vertex => Approximately(vertex.UV0.X, 0.0) && Approximately(vertex.UV0.Y, 0.0));
        Assert.Contains(relief.Mesh.Vertices, static vertex => Approximately(vertex.UV0.X, 0.0) && Approximately(vertex.UV0.Y, 1.0));
        Assert.Contains(relief.Mesh.Vertices, static vertex => Approximately(vertex.UV0.X, 1.0) && Approximately(vertex.UV0.Y, 0.500002));
        Assert.Single(relief.Mesh.Submeshes);
        Assert.Equal("dem", sceneBuilder.CityObjects[0].PackageName);
    }

    [Fact]
    public async Task ExecuteAsyncFiltersRequestedPackages()
    {
        StubResoniteSceneBuilder sceneBuilder = new();
        PlateauImportService service = new(sceneBuilder);
        string fixturePath = TestData.GetFixturePath("LocalPlateauDatasetMixedObjects");

        ImportExecutionResult result = await service.ExecuteAsync(
            new PlateauImportRequest(
                Dataset: "tokyo23ku",
                MeshCode: "53394525",
                SourceKind: DatasetSourceKind.Local,
                LocalSourcePath: fixturePath,
                ServerUri: null,
                PackageNames: ["tran", "waterbody", "dem", "tran"]),
            workRoot: "runtime/resonite");
        CapturedResoniteScene scene = result.Metadata.ToScene(sceneBuilder.CityObjects);

        Assert.Equal(["dem", "tran"], result.Metadata.SourceDataset.PackageNames);
        Assert.DoesNotContain(
            result.Metadata.SourceDataset.SourceFiles,
            static path => path.Contains("/bldg/", StringComparison.Ordinal));
        Assert.DoesNotContain(
            result.Metadata.SourceDataset.SourceFiles,
            static path => path.Contains("/luse/", StringComparison.Ordinal));
        Assert.Equal(2, scene.CityObjects.Count);
        Assert.All(
            scene.CityObjects,
            static cityObject => Assert.True(cityObject.PackageName is "dem" or "tran"));
    }

    [Fact]
    public async Task ExecuteAsyncBuildsSceneFromFlatUdxPackageLayout()
    {
        StubResoniteSceneBuilder sceneBuilder = new();
        PlateauImportService service = new(sceneBuilder);
        string fixturePath = TestData.GetFixturePath("LocalPlateauDatasetFlatUdx");

        ImportExecutionResult result = await service.ExecuteAsync(
            new PlateauImportRequest(
                Dataset: "tokyo23ku",
                MeshCode: "53394406",
                SourceKind: DatasetSourceKind.Local,
                LocalSourcePath: fixturePath,
                ServerUri: null),
            workRoot: "runtime/resonite");
        CapturedResoniteScene scene = result.Metadata.ToScene(sceneBuilder.CityObjects);

        Assert.Equal(["bldg"], result.Metadata.SourceDataset.PackageNames);
        Assert.Contains(
            "udx/bldg/53394406_bldg_6697_op2.gml",
            result.Metadata.SourceDataset.SourceFiles);

        ResoniteConstructionCityObject building = Assert.Single(scene.CityObjects);
        Assert.Equal("Flat Layout Building", building.DisplayName);
        Assert.Contains(
            building.Materials,
            static material => material.TexturePath == "udx/bldg/appearance/roof.png");
    }

    [Fact]
    public async Task ExecuteAsyncResolvesNestedLocalSourcePathFromAncestorDirectory()
    {
        using TemporaryDirectory sourceRoot = new();
        string datasetRoot = Path.Combine(
            sourceRoot.Path,
            "cache",
            "remote",
            "tokyo23ku",
            "53394525",
            "13100_tokyo23-ku_2022_citygml_1_2_op");
        CreateRuntimePackageFixture(
            datasetRoot,
            "bldg",
            "http://www.opengis.net/citygml/building/2.0",
            "Building",
            "Nested Source Building");

        StubResoniteSceneBuilder sceneBuilder = new();
        PlateauImportService service = new(sceneBuilder);

        ImportExecutionResult result = await service.ExecuteAsync(
            new PlateauImportRequest(
                Dataset: "tokyo23ku",
                MeshCode: "53394525",
                SourceKind: DatasetSourceKind.Local,
                LocalSourcePath: Path.Combine(sourceRoot.Path, "cache", "remote", "tokyo23ku", "53394525"),
                ServerUri: null),
            workRoot: "runtime/resonite");
        CapturedResoniteScene scene = result.Metadata.ToScene(sceneBuilder.CityObjects);

        Assert.Equal(["bldg"], result.Metadata.SourceDataset.PackageNames);
        Assert.Contains(
            "udx/bldg/53394525/plateau_tokyo23ku_bldg_53394525.gml",
            result.Metadata.SourceDataset.SourceFiles);
        Assert.Equal("Nested Source Building", Assert.Single(scene.CityObjects).DisplayName);
    }

    [Fact]
    public async Task ExecuteAsyncAlignsSupportedFlatPackagesToDemHeights()
    {
        using TemporaryDirectory datasetRoot = new();
        CreateRuntimeTerrainAlignmentFixture(datasetRoot.Path);

        StubResoniteSceneBuilder sceneBuilder = new();
        PlateauImportService service = new(sceneBuilder);

        ImportExecutionResult result = await service.ExecuteAsync(
            new PlateauImportRequest(
                Dataset: "tokyo23ku",
                MeshCode: "53394525",
                SourceKind: DatasetSourceKind.Local,
                LocalSourcePath: datasetRoot.Path,
                ServerUri: null),
            workRoot: "runtime/resonite");
        CapturedResoniteScene scene = result.Metadata.ToScene(sceneBuilder.CityObjects);

        ResoniteConstructionCityObject road = Assert.Single(
            scene.CityObjects,
            static cityObject => cityObject.DisplayName == "Aligned Road");
        Assert.Equal("tran", road.PackageName);
        Assert.Contains(road.Mesh.Vertices, static vertex => vertex.Position.Y >= -0.001 && vertex.Position.Y <= 0.001);
        Assert.Contains(road.Mesh.Vertices, static vertex => vertex.Position.Y >= 9.9 && vertex.Position.Y <= 10.1);
        Assert.True(
            road.Mesh.Vertices.Max(static vertex => vertex.Position.Y) - road.Mesh.Vertices.Min(static vertex => vertex.Position.Y) > 5.0,
            "Expected DEM sampling to make the aligned road non-flat.");

        ResoniteMaterialBinding roadMaterial = Assert.Single(road.Materials);
        Assert.Equal("udx/tran/53394525/appearance/marking.png", roadMaterial.TexturePath);
        Assert.Equal(ResoniteTextureSourceKind.Dataset, roadMaterial.TextureSourceKind);
        Assert.Equal(ResoniteMaterialProjection.Uv, roadMaterial.Projection);
        Assert.Equal(LocalCityGmlResonitePlanBuilder.DefaultTerrainAlignedMaterialDepthOffset, roadMaterial.DepthOffset);
    }

    [Fact]
    public async Task ExecuteAsyncLeavesUnsupportedPackagesUnchangedWhenDemExists()
    {
        using TemporaryDirectory datasetRoot = new();
        CreateRuntimeTerrainAlignmentFixture(datasetRoot.Path);

        StubResoniteSceneBuilder sceneBuilder = new();
        PlateauImportService service = new(sceneBuilder);

        ImportExecutionResult result = await service.ExecuteAsync(
            new PlateauImportRequest(
                Dataset: "tokyo23ku",
                MeshCode: "53394525",
                SourceKind: DatasetSourceKind.Local,
                LocalSourcePath: datasetRoot.Path,
                ServerUri: null),
            workRoot: "runtime/resonite");
        CapturedResoniteScene scene = result.Metadata.ToScene(sceneBuilder.CityObjects);

        ResoniteConstructionCityObject building = Assert.Single(
            scene.CityObjects,
            static cityObject => cityObject.DisplayName == "Raised Building");
        Assert.Equal("bldg", building.PackageName);
        Assert.All(
            building.Mesh.Vertices,
            static vertex => Assert.InRange(vertex.Position.Y, -0.001, 0.001));
        Assert.All(building.Materials, static material => Assert.Null(material.DepthOffset));
    }

    [Fact]
    public async Task ExecuteAsyncLeavesHighLodTransportationGeometryUnchangedWhenDemExists()
    {
        using TemporaryDirectory datasetRoot = new();
        CreateRuntimeHighLodTransportationFixture(datasetRoot.Path);

        StubResoniteSceneBuilder sceneBuilder = new();
        PlateauImportService service = new(sceneBuilder);

        ImportExecutionResult result = await service.ExecuteAsync(
            new PlateauImportRequest(
                Dataset: "tokyo23ku",
                MeshCode: "53394525",
                SourceKind: DatasetSourceKind.Local,
                LocalSourcePath: datasetRoot.Path,
                ServerUri: null),
            workRoot: "runtime/resonite");
        CapturedResoniteScene scene = result.Metadata.ToScene(sceneBuilder.CityObjects);

        ResoniteConstructionCityObject road = Assert.Single(
            scene.CityObjects,
            static cityObject => cityObject.DisplayName == "Precise Road");
        Assert.Equal("tran", road.PackageName);
        Assert.All(
            road.Mesh.Vertices,
            static vertex => Assert.InRange(vertex.Position.Y, -0.001, 0.001));
        Assert.All(road.Materials, static material => Assert.Null(material.DepthOffset));
    }

    [Fact]
    public async Task ExecuteAsyncGeneratesUvProjectedFallbackMaterialForTexturelessTransportation()
    {
        using TemporaryDirectory datasetRoot = new();
        CreateRuntimeTexturelessTransportationFixture(datasetRoot.Path);

        StubResoniteSceneBuilder sceneBuilder = new();
        PlateauImportService service = new(sceneBuilder);

        ImportExecutionResult result = await service.ExecuteAsync(
            new PlateauImportRequest(
                Dataset: "tokyo23ku",
                MeshCode: "53394525",
                SourceKind: DatasetSourceKind.Local,
                LocalSourcePath: datasetRoot.Path,
                ServerUri: null),
            workRoot: "runtime/resonite");
        CapturedResoniteScene scene = result.Metadata.ToScene(sceneBuilder.CityObjects);

        ResoniteConstructionCityObject road = Assert.Single(scene.CityObjects);
        Assert.Equal(2, road.Materials.Count);
        Assert.Equal("tran", road.PackageName);

        ResoniteMaterialBinding roadMaterial = Assert.Single(
            road.Materials,
            static material => material.MaterialType == ResoniteMaterialType.Standard);
        Assert.Equal(ResoniteTextureSourceKind.Bundled, roadMaterial.TextureSourceKind);
        Assert.Equal(ResoniteMaterialProjection.Triplanar, roadMaterial.Projection);

        ResoniteMaterialBinding markingMaterial = Assert.Single(
            road.Materials,
            static material => material.MaterialType == ResoniteMaterialType.VertexColor);
        Assert.Null(markingMaterial.TexturePath);
        Assert.Equal(ResoniteMaterialProjection.Uv, markingMaterial.Projection);
        Assert.Equal(LocalCityGmlResonitePlanBuilder.DefaultTerrainAlignedMaterialDepthOffset, markingMaterial.DepthOffset);
        Assert.Equal(1.0, markingMaterial.BaseColor.R, 6);
        Assert.Equal(1.0, markingMaterial.BaseColor.G, 6);
        Assert.Equal(1.0, markingMaterial.BaseColor.B, 6);

        int markingSubmeshIndex = Assert.Single(markingMaterial.SubmeshIndices);
        ResoniteMeshSubmesh markingSubmesh = Assert.Single(
            road.Mesh.Submeshes,
            submesh => submesh.Index == markingSubmeshIndex);
        Assert.All(
            markingSubmesh.TriangleVertexIndices,
            index =>
            {
                Assert.NotNull(road.Mesh.Vertices[index].Color);
                Assert.Equal(1.0, road.Mesh.Vertices[index].Color!.R, 6);
            });
    }

    [Fact]
    public async Task ExecuteAsyncIncludesParentMeshPackageFilesForEightDigitMeshCodes()
    {
        StubResoniteSceneBuilder sceneBuilder = new();
        PlateauImportService service = new(sceneBuilder);
        string fixturePath = TestData.GetFixturePath("LocalPlateauDatasetParentMeshPackages");

        ImportExecutionResult result = await service.ExecuteAsync(
            new PlateauImportRequest(
                Dataset: "tokyo23ku",
                MeshCode: "53394525",
                SourceKind: DatasetSourceKind.Local,
                LocalSourcePath: fixturePath,
                ServerUri: null),
            workRoot: "runtime/resonite");
        CapturedResoniteScene scene = result.Metadata.ToScene(sceneBuilder.CityObjects);

        Assert.Equal(["bldg", "dem", "luse", "tran"], result.Metadata.SourceDataset.PackageNames);
        Assert.Contains(
            "udx/tran/533945/plateau_tokyo23ku_tran_533945.gml",
            result.Metadata.SourceDataset.SourceFiles);
        Assert.Contains(
            "udx/luse/533945/plateau_tokyo23ku_luse_533945.gml",
            result.Metadata.SourceDataset.SourceFiles);
        Assert.Contains(
            "udx/dem/533945/plateau_tokyo23ku_dem_533945.gml",
            result.Metadata.SourceDataset.SourceFiles);

        Assert.Equal(4, scene.CityObjects.Count);
        Assert.DoesNotContain(
            scene.CityObjects,
            static cityObject => cityObject.DisplayName == "Outside Road");
        Assert.Contains(
            scene.CityObjects,
            static cityObject => cityObject.PackageName == "tran" && cityObject.DisplayName == "Parent Tile Road");
        Assert.Contains(
            scene.CityObjects,
            static cityObject => cityObject.PackageName == "luse" && cityObject.DisplayName == "Parent Tile Land Use");
        Assert.Contains(
            scene.CityObjects,
            static cityObject => cityObject.PackageName == "dem" && cityObject.DisplayName == "Parent Tile Relief");
    }

    [Fact]
    public async Task ExecuteAsyncFallsBackToDirectoryScopedMeshCodeWhenFileNameOmitsIt()
    {
        using TemporaryDirectory datasetRoot = new();
        CreateRuntimeDirectoryScopedDemFixture(datasetRoot.Path);

        StubResoniteSceneBuilder sceneBuilder = new();
        PlateauImportService service = new(sceneBuilder);

        ImportExecutionResult result = await service.ExecuteAsync(
            new PlateauImportRequest(
                Dataset: "yokohama",
                MeshCode: "53394525",
                SourceKind: DatasetSourceKind.Local,
                LocalSourcePath: datasetRoot.Path,
                ServerUri: null),
            workRoot: "runtime/resonite");
        CapturedResoniteScene scene = result.Metadata.ToScene(sceneBuilder.CityObjects);

        Assert.Equal(["bldg", "dem"], result.Metadata.SourceDataset.PackageNames);
        Assert.Contains(
            result.Metadata.SourceDataset.SourceFiles,
            static path => path == "udx/dem/53394525/plateau_yokohama_dem.gml");
        Assert.Contains(
            scene.CityObjects,
            static cityObject => cityObject.PackageName == "dem" && cityObject.DisplayName == "Directory Scoped Relief");
        Assert.Contains(
            scene.CityObjects,
            static cityObject => cityObject.PackageName == "bldg" && cityObject.DisplayName == "Directory Scoped Building");
    }

    [Fact]
    public async Task ExecuteAsyncSplitsDemTerrainIntoMultipleTextureBoundedChunks()
    {
        using TemporaryDirectory datasetRoot = new();
        CreateRuntimeWideDemFixture(datasetRoot.Path);
        StubResoniteSceneBuilder sceneBuilder = new();
        PlateauImportService service = new(sceneBuilder);

        ImportExecutionResult result = await service.ExecuteAsync(
            new PlateauImportRequest(
                Dataset: "tokyo23ku",
                MeshCode: "53394525",
                SourceKind: DatasetSourceKind.Local,
                LocalSourcePath: datasetRoot.Path,
                ServerUri: null),
            workRoot: "runtime/resonite");
        CapturedResoniteScene scene = result.Metadata.ToScene(sceneBuilder.CityObjects);

        TerrainTextureOverlay[] overlays = result.Metadata.SourceDataset.TerrainTextureOverlays
            .Where(static overlay => string.Equals(overlay.PackageName, "dem", StringComparison.Ordinal))
            .ToArray();
        Assert.True(overlays.Length > 1);
        Assert.All(overlays, static overlay => Assert.Equal(LocalCityGmlResonitePlanBuilder.DefaultDemTerrainTextureMaxSize, overlay.MaxTextureSize));

        ResoniteConstructionCityObject[] reliefChunks = scene.CityObjects
            .Where(static cityObject => cityObject.PackageName == "dem")
            .ToArray();
        Assert.True(reliefChunks.Length > 1);
        Assert.All(reliefChunks, static chunk =>
        {
            string texturePath = Assert.Single(chunk.Materials).TexturePath!;
            Assert.StartsWith(LocalCityGmlResonitePlanBuilder.DefaultDemTerrainTexturePath, texturePath, StringComparison.Ordinal);
            Assert.All(chunk.Mesh.Vertices, static vertex =>
            {
                Assert.InRange(vertex.UV0.X, 0.0, 1.0);
                Assert.InRange(vertex.UV0.Y, 0.0, 1.0);
            });
        });
    }

    [Fact]
    public async Task ExecuteAsyncSupportsOfficialPlateauCityObjectPackagePrefixes()
    {
        using TemporaryDirectory datasetRoot = new();
        CreateRuntimePackageFixture(datasetRoot.Path, "area", "urn:plateau:test:area", "Area", "Area One");
        CreateRuntimePackageFixture(datasetRoot.Path, "cons", "urn:plateau:test:cons", "OtherConstruction", "Construction One");
        CreateRuntimePackageFixture(datasetRoot.Path, "fld", "urn:plateau:test:fld", "FloodRisk", "Flood Risk One");
        CreateRuntimePackageFixture(datasetRoot.Path, "frn", "http://www.opengis.net/citygml/cityfurniture/2.0", "CityFurniture", "City Furniture One");
        CreateRuntimePackageFixture(datasetRoot.Path, "gen", "urn:plateau:test:gen", "GenericObject", "Generic One");
        CreateRuntimePackageFixture(datasetRoot.Path, "htd", "urn:plateau:test:htd", "HeightControlDistrict", "Height Control One");
        CreateRuntimePackageFixture(datasetRoot.Path, "ifld", "urn:plateau:test:ifld", "InlandFloodRisk", "Inland Flood One");
        CreateRuntimePackageFixture(datasetRoot.Path, "lsld", "urn:plateau:test:lsld", "LandSlideRisk", "Landslide Risk One");
        CreateRuntimePackageFixture(datasetRoot.Path, "rfld", "urn:plateau:test:rfld", "ReservoirFloodRisk", "Reservoir Flood One");
        CreateRuntimePackageFixture(datasetRoot.Path, "rwy", "http://www.opengis.net/citygml/transportation/2.0", "Railway", "Railway One");
        CreateRuntimePackageFixture(datasetRoot.Path, "squr", "http://www.opengis.net/citygml/transportation/2.0", "Square", "Square One");
        CreateRuntimePackageFixture(datasetRoot.Path, "tnm", "urn:plateau:test:tnm", "TsunamiRisk", "Tsunami Risk One");
        CreateRuntimePackageFixture(datasetRoot.Path, "trk", "http://www.opengis.net/citygml/transportation/2.0", "Track", "Track One");
        CreateRuntimePackageFixture(datasetRoot.Path, "veg", "http://www.opengis.net/citygml/vegetation/2.0", "SolitaryVegetationObject", "Vegetation One");
        CreateRuntimePackageFixture(datasetRoot.Path, "ubld", "urn:plateau:test:ubld", "UndergroundBuilding", "Underground Building One");
        CreateRuntimePackageFixture(datasetRoot.Path, "unf", "urn:plateau:test:unf", "UndergroundFacility", "Underground Facility One");
        CreateRuntimePackageFixture(datasetRoot.Path, "urf", "urn:plateau:test:urf", "UrbanPlanningDecision", "Urban Planning One");
        CreateRuntimePackageFixture(datasetRoot.Path, "brid", "http://www.opengis.net/citygml/bridge/2.0", "Bridge", "Bridge One");
        CreateRuntimePackageFixture(datasetRoot.Path, "tun", "http://www.opengis.net/citygml/tunnel/2.0", "Tunnel", "Tunnel One");
        CreateRuntimePackageFixture(datasetRoot.Path, "wtr", "http://www.opengis.net/citygml/waterbody/2.0", "WaterBody", "Water Body One");
        CreateRuntimePackageFixture(datasetRoot.Path, "wwy", "urn:plateau:test:wwy", "Waterway", "Waterway One");

        StubResoniteSceneBuilder sceneBuilder = new();
        PlateauImportService service = new(sceneBuilder);

        ImportExecutionResult result = await service.ExecuteAsync(
            new PlateauImportRequest(
                Dataset: "tokyo23ku",
                MeshCode: "53394525",
                SourceKind: DatasetSourceKind.Local,
                LocalSourcePath: datasetRoot.Path,
                ServerUri: null),
            workRoot: "runtime/resonite");
        CapturedResoniteScene scene = result.Metadata.ToScene(sceneBuilder.CityObjects);

        Assert.Equal(
            ["area", "brid", "cons", "fld", "frn", "gen", "htd", "ifld", "lsld", "rfld", "rwy", "squr", "tnm", "trk", "tun", "ubld", "unf", "urf", "veg", "wtr", "wwy"],
            result.Metadata.SourceDataset.PackageNames);
        Assert.Contains(scene.CityObjects, static cityObject => cityObject.PackageName == "area" && cityObject.DisplayName == "Area One");
        Assert.Contains(scene.CityObjects, static cityObject => cityObject.PackageName == "cons" && cityObject.DisplayName == "Construction One");
        Assert.Contains(scene.CityObjects, static cityObject => cityObject.PackageName == "fld" && cityObject.DisplayName == "Flood Risk One");
        Assert.Contains(scene.CityObjects, static cityObject => cityObject.PackageName == "frn" && cityObject.DisplayName == "City Furniture One");
        Assert.Contains(scene.CityObjects, static cityObject => cityObject.PackageName == "gen" && cityObject.DisplayName == "Generic One");
        Assert.Contains(scene.CityObjects, static cityObject => cityObject.PackageName == "htd" && cityObject.DisplayName == "Height Control One");
        Assert.Contains(scene.CityObjects, static cityObject => cityObject.PackageName == "ifld" && cityObject.DisplayName == "Inland Flood One");
        Assert.Contains(scene.CityObjects, static cityObject => cityObject.PackageName == "lsld" && cityObject.DisplayName == "Landslide Risk One");
        Assert.Contains(scene.CityObjects, static cityObject => cityObject.PackageName == "rfld" && cityObject.DisplayName == "Reservoir Flood One");
        Assert.Contains(scene.CityObjects, static cityObject => cityObject.PackageName == "rwy" && cityObject.DisplayName == "Railway One");
        Assert.Contains(scene.CityObjects, static cityObject => cityObject.PackageName == "squr" && cityObject.DisplayName == "Square One");
        Assert.Contains(scene.CityObjects, static cityObject => cityObject.PackageName == "tnm" && cityObject.DisplayName == "Tsunami Risk One");
        Assert.Contains(scene.CityObjects, static cityObject => cityObject.PackageName == "trk" && cityObject.DisplayName == "Track One");
        Assert.Contains(scene.CityObjects, static cityObject => cityObject.PackageName == "veg" && cityObject.DisplayName == "Vegetation One");
        Assert.Contains(scene.CityObjects, static cityObject => cityObject.PackageName == "ubld" && cityObject.DisplayName == "Underground Building One");
        Assert.Contains(scene.CityObjects, static cityObject => cityObject.PackageName == "unf" && cityObject.DisplayName == "Underground Facility One");
        Assert.Contains(scene.CityObjects, static cityObject => cityObject.PackageName == "urf" && cityObject.DisplayName == "Urban Planning One");
        Assert.Contains(scene.CityObjects, static cityObject => cityObject.PackageName == "brid" && cityObject.DisplayName == "Bridge One");
        Assert.Contains(scene.CityObjects, static cityObject => cityObject.PackageName == "tun" && cityObject.DisplayName == "Tunnel One");
        Assert.Contains(scene.CityObjects, static cityObject => cityObject.PackageName == "wtr" && cityObject.DisplayName == "Water Body One");
        Assert.Contains(scene.CityObjects, static cityObject => cityObject.PackageName == "wwy" && cityObject.DisplayName == "Waterway One");

        ResoniteConstructionCityObject vegetation = Assert.Single(
            scene.CityObjects,
            static cityObject => cityObject.PackageName == "veg" && cityObject.DisplayName == "Vegetation One");
        Assert.All(
            vegetation.Materials,
            static material =>
            {
                Assert.Equal(ResoniteMaterialType.Standard, material.MaterialType);
                Assert.Contains(material.TexturePath!, BundledDefaultMaterialFamilies.OtherVariants);
                Assert.Equal(ResoniteTextureSourceKind.Bundled, material.TextureSourceKind);
                Assert.Equal(ResoniteMaterialProjection.Triplanar, material.Projection);
            });
    }

    [Fact]
    public async Task ExecuteAsyncUsesVertexColorMaterialsForVegetationAndFallsBackToDefaultMaterialWithoutDiffuseColor()
    {
        using TemporaryDirectory datasetRoot = new();
        CreateRuntimeVegetationFixture(datasetRoot.Path);

        StubResoniteSceneBuilder sceneBuilder = new();
        PlateauImportService service = new(sceneBuilder);

        ImportExecutionResult result = await service.ExecuteAsync(
            new PlateauImportRequest(
                Dataset: "tokyo23ku",
                MeshCode: "53394525",
                SourceKind: DatasetSourceKind.Local,
                LocalSourcePath: datasetRoot.Path,
                ServerUri: null),
            workRoot: "runtime/resonite");
        CapturedResoniteScene scene = result.Metadata.ToScene(sceneBuilder.CityObjects);

        ResoniteConstructionCityObject vegetation = Assert.Single(scene.CityObjects);
        Assert.Equal("veg", vegetation.PackageName);
        Assert.Equal(2, vegetation.Materials.Count);
        Assert.Contains(
            vegetation.Materials,
            static material =>
                material.MaterialType == ResoniteMaterialType.VertexColor
                && material.TexturePath is null
                && material.Projection == ResoniteMaterialProjection.Uv
                && Approximately(material.BaseColor.R, 0.45)
                && Approximately(material.BaseColor.G, 0.28)
                && Approximately(material.BaseColor.B, 0.12));
        Assert.Contains(
            vegetation.Materials,
            static material =>
                material.MaterialType == ResoniteMaterialType.Standard
                && material.TexturePath is not null
                && BundledDefaultMaterialFamilies.OtherVariants.Contains(material.TexturePath)
                && material.Projection == ResoniteMaterialProjection.Triplanar);
    }

    [Fact]
    public async Task ExecuteAsyncTriangulatesConcaveLandUseWithoutOverfilling()
    {
        using TemporaryDirectory datasetRoot = new();
        CreateRuntimeComplexLandUseFixture(datasetRoot.Path);

        StubResoniteSceneBuilder sceneBuilder = new();
        PlateauImportService service = new(sceneBuilder);

        ImportExecutionResult result = await service.ExecuteAsync(
            new PlateauImportRequest(
                Dataset: "tokyo23ku",
                MeshCode: "53394525",
                SourceKind: DatasetSourceKind.Local,
                LocalSourcePath: datasetRoot.Path,
                ServerUri: null),
            workRoot: "runtime/resonite");
        CapturedResoniteScene scene = result.Metadata.ToScene(sceneBuilder.CityObjects);

        ResoniteConstructionCityObject cityObject = Assert.Single(
            scene.CityObjects,
            static candidate => candidate.DisplayName == "Concave Land Use");
        Assert.Equal("luse", cityObject.PackageName);
        Assert.Equal(7.0, ComputeMeshArea(cityObject.Mesh), 6);
    }

    [Fact]
    public async Task ExecuteAsyncTriangulatesLandUseWithInteriorRingsAsHoles()
    {
        using TemporaryDirectory datasetRoot = new();
        CreateRuntimeComplexLandUseFixture(datasetRoot.Path);

        StubResoniteSceneBuilder sceneBuilder = new();
        PlateauImportService service = new(sceneBuilder);

        ImportExecutionResult result = await service.ExecuteAsync(
            new PlateauImportRequest(
                Dataset: "tokyo23ku",
                MeshCode: "53394525",
                SourceKind: DatasetSourceKind.Local,
                LocalSourcePath: datasetRoot.Path,
                ServerUri: null),
            workRoot: "runtime/resonite");
        CapturedResoniteScene scene = result.Metadata.ToScene(sceneBuilder.CityObjects);

        ResoniteConstructionCityObject cityObject = Assert.Single(
            scene.CityObjects,
            static candidate => candidate.DisplayName == "Hole Land Use");
        Assert.Equal("luse", cityObject.PackageName);
        Assert.Equal(84.0, ComputeMeshArea(cityObject.Mesh), 6);
    }

    [Fact]
    public async Task ExecuteAsyncKeepsOnlyHighestLodGeometryPerCityObject()
    {
        using TemporaryDirectory datasetRoot = new();
        CreateRuntimeMultiLodFixture(datasetRoot.Path);

        StubResoniteSceneBuilder sceneBuilder = new();
        PlateauImportService service = new(sceneBuilder);

        ImportExecutionResult result = await service.ExecuteAsync(
            new PlateauImportRequest(
                Dataset: "tokyo23ku",
                MeshCode: "53394525",
                SourceKind: DatasetSourceKind.Local,
                LocalSourcePath: datasetRoot.Path,
                ServerUri: null),
            workRoot: "runtime/resonite");
        CapturedResoniteScene scene = result.Metadata.ToScene(sceneBuilder.CityObjects);

        ResoniteConstructionCityObject cityObject = Assert.Single(
            scene.CityObjects,
            static candidate => candidate.DisplayName == "Multi LOD Building");
        Assert.Equal("bldg", cityObject.PackageName);
        Assert.Equal(3, cityObject.LodLevel);
        Assert.Equal(25.0, ComputeMeshArea(cityObject.Mesh), 6);
    }

    [Fact]
    public async Task ExecuteAsyncKeepsResoniteTriangleWindingAlignedWithVertexNormals()
    {
        using TemporaryDirectory datasetRoot = new();
        CreateRuntimeTriangleFixture(datasetRoot.Path);

        StubResoniteSceneBuilder sceneBuilder = new();
        PlateauImportService service = new(sceneBuilder);

        ImportExecutionResult result = await service.ExecuteAsync(
            new PlateauImportRequest(
                Dataset: "tokyo23ku",
                MeshCode: "53394525",
                SourceKind: DatasetSourceKind.Local,
                LocalSourcePath: datasetRoot.Path,
                ServerUri: null),
            workRoot: "runtime/resonite");
        CapturedResoniteScene scene = result.Metadata.ToScene(sceneBuilder.CityObjects);

        ResoniteConstructionCityObject cityObject = Assert.Single(
            scene.CityObjects,
            static candidate => candidate.DisplayName == "Triangle Building");
        ResoniteMeshSubmesh submesh = Assert.Single(cityObject.Mesh.Submeshes);
        Assert.Equal([0, 2, 1], submesh.TriangleVertexIndices);

        ResoniteFloat3 faceNormal = ComputeTriangleNormal(cityObject.Mesh, submesh);
        Assert.True(faceNormal.Y > 0.0, $"Expected the Resonite-facing triangle normal to point upward after winding reversal, but normal was {faceNormal}.");
        Assert.All(
            cityObject.Mesh.Vertices,
            vertex => Assert.True(
                Dot(faceNormal, vertex.Normal) > 0.99,
                $"Expected vertex normal {vertex.Normal} to align with face normal {faceNormal}."));
    }

    [Fact]
    public async Task ExecuteAsyncRejectsLocalSourceWithoutInput()
    {
        StubResoniteSceneBuilder sceneBuilder = new();
        PlateauImportService service = new(sceneBuilder);

        PlateauImportValidationException exception = await Assert.ThrowsAsync<PlateauImportValidationException>(() =>
            service.ExecuteAsync(
                new PlateauImportRequest(
                    Dataset: "tokyo23ku",
                    MeshCode: "53394525",
                    SourceKind: DatasetSourceKind.Local,
                    LocalSourcePath: null,
                    ServerUri: null),
                workRoot: "runtime/resonite"));

        Assert.Contains(
            "The --local-source-path value is required when --source local is used.",
            exception.Errors);
    }

    [Fact]
    public async Task ExecuteAsyncFallsBackToBundledDefaultTextureWhenTextureFileIsMissing()
    {
        StubResoniteSceneBuilder sceneBuilder = new();
        PlateauImportService service = new(sceneBuilder);
        string fixturePath = TestData.GetFixturePath("LocalPlateauDatasetMissingTexture");

        ImportExecutionResult result = await service.ExecuteAsync(
            new PlateauImportRequest(
                Dataset: "tokyo23ku",
                MeshCode: "53394525",
                SourceKind: DatasetSourceKind.Local,
                LocalSourcePath: fixturePath,
                ServerUri: null),
            workRoot: "runtime/resonite");
        CapturedResoniteScene scene = result.Metadata.ToScene(sceneBuilder.CityObjects);

        ResoniteConstructionCityObject cityObject = Assert.Single(scene.CityObjects);
        ResoniteMaterialBinding material = Assert.Single(cityObject.Materials);
        Assert.Contains(material.TexturePath!, BundledDefaultMaterialFamilies.RoofVariants);
        Assert.Equal(ResoniteTextureSourceKind.Bundled, material.TextureSourceKind);
        Assert.Equal(ResoniteMaterialProjection.Triplanar, material.Projection);
        Assert.Equal(0.52, material.BaseColor.R, 6);
        Assert.Equal(0.62, material.BaseColor.G, 6);
        Assert.Equal(0.72, material.BaseColor.B, 6);
    }

    [Fact]
    public async Task ExecuteAsyncAssignsBundledDefaultTexturesByPackageCategory()
    {
        using TemporaryDirectory datasetRoot = new();
        CreateRuntimePackageFixture(datasetRoot.Path, "bldg", "http://www.opengis.net/citygml/building/2.0", "Building", "Building One");
        CreateRuntimePackageFixture(datasetRoot.Path, "tran", "http://www.opengis.net/citygml/transportation/2.0", "Road", "Road One");
        CreateRuntimePackageFixture(datasetRoot.Path, "area", "urn:plateau:test:area", "Area", "Area One");

        StubResoniteSceneBuilder sceneBuilder = new();
        PlateauImportService service = new(sceneBuilder);

        ImportExecutionResult result = await service.ExecuteAsync(
            new PlateauImportRequest(
                Dataset: "tokyo23ku",
                MeshCode: "53394525",
                SourceKind: DatasetSourceKind.Local,
                LocalSourcePath: datasetRoot.Path,
                ServerUri: null),
            workRoot: "runtime/resonite");
        CapturedResoniteScene scene = result.Metadata.ToScene(sceneBuilder.CityObjects);

        ResoniteConstructionCityObject building = Assert.Single(scene.CityObjects, static cityObject => cityObject.DisplayName == "Building One");
        Assert.All(
            building.Materials,
            static material =>
            {
                Assert.Equal(ResoniteMaterialType.Standard, material.MaterialType);
                Assert.Contains(material.TexturePath!, BundledDefaultMaterialFamilies.RoofVariants);
                Assert.Equal(ResoniteTextureSourceKind.Bundled, material.TextureSourceKind);
                Assert.Equal(ResoniteMaterialProjection.Triplanar, material.Projection);
            });

        ResoniteConstructionCityObject road = Assert.Single(scene.CityObjects, static cityObject => cityObject.DisplayName == "Road One");
        Assert.All(
            road.Materials,
            static material =>
            {
                Assert.Equal(ResoniteMaterialType.Standard, material.MaterialType);
                Assert.Contains(material.TexturePath!, BundledDefaultMaterialFamilies.RoadVariants);
                Assert.Equal(ResoniteTextureSourceKind.Bundled, material.TextureSourceKind);
                Assert.Equal(ResoniteMaterialProjection.Triplanar, material.Projection);
            });

        ResoniteConstructionCityObject area = Assert.Single(scene.CityObjects, static cityObject => cityObject.DisplayName == "Area One");
        Assert.All(
            area.Materials,
            static material =>
            {
                Assert.Equal(ResoniteMaterialType.Wireframe, material.MaterialType);
                Assert.Null(material.TexturePath);
                Assert.Equal(ResoniteTextureSourceKind.Bundled, material.TextureSourceKind);
                Assert.Equal(ResoniteMaterialProjection.Uv, material.Projection);
            });
    }

    [Fact]
    public async Task ExecuteAsyncUsesUvForUntexturedBuildingFacadesAndTriplanarForRoofs()
    {
        using TemporaryDirectory datasetRoot = new();
        CreateRuntimeUntexturedBuildingFixture(datasetRoot.Path);

        StubResoniteSceneBuilder sceneBuilder = new();
        PlateauImportService service = new(sceneBuilder);

        ImportExecutionResult result = await service.ExecuteAsync(
            new PlateauImportRequest(
                Dataset: "tokyo23ku",
                MeshCode: "53394525",
                SourceKind: DatasetSourceKind.Local,
                LocalSourcePath: datasetRoot.Path,
                ServerUri: null),
            workRoot: "runtime/resonite");
        CapturedResoniteScene scene = result.Metadata.ToScene(sceneBuilder.CityObjects);

        ResoniteConstructionCityObject building = Assert.Single(scene.CityObjects);
        Assert.DoesNotContain(
            building.Materials,
            static material => string.Equals(
                material.TexturePath,
                "default-materials/facade/PaintedPlaster012_2K-JPG_Color.jpg",
                StringComparison.Ordinal));
        Assert.Contains(
            building.Materials,
            static material =>
                BundledDefaultMaterialFamilies.FacadeVariants.Contains(material.TexturePath!)
                && material.Projection == ResoniteMaterialProjection.Uv);
        Assert.Contains(
            building.Materials,
            static material =>
                BundledDefaultMaterialFamilies.RoofVariants.Contains(material.TexturePath!)
                && material.Projection == ResoniteMaterialProjection.Triplanar);

        ResoniteMaterialBinding facadeMaterial = Assert.Single(
            building.Materials,
            static material =>
                BundledDefaultMaterialFamilies.FacadeVariants.Contains(material.TexturePath!)
                && material.Projection == ResoniteMaterialProjection.Uv);
        Assert.NotNull(facadeMaterial.TextureScale);
        Assert.Equal(
            BundledDefaultMaterialProfiles.GetTilesPerMeter(facadeMaterial.TexturePath!).X,
            facadeMaterial.TextureScale!.X,
            6);
        Assert.Equal(
            BundledDefaultMaterialProfiles.GetTilesPerMeter(facadeMaterial.TexturePath!).Y,
            facadeMaterial.TextureScale.Y,
            6);
        int facadeSubmeshIndex = Assert.Single(facadeMaterial.SubmeshIndices);
        ResoniteMeshSubmesh facadeSubmesh = Assert.Single(
            building.Mesh.Submeshes,
            submesh => submesh.Index == facadeSubmeshIndex);
        ResoniteFloat2[] facadeUvs = facadeSubmesh.TriangleVertexIndices
            .Select(index => building.Mesh.Vertices[index].UV0)
            .Distinct()
            .ToArray();

        Assert.Contains(facadeUvs, static uv => Approximately(uv.X, 0.0) && Approximately(uv.Y, 0.0));
        Assert.True(
            facadeUvs.Max(static uv => uv.X) - facadeUvs.Min(static uv => uv.X) >= 5.0 - 1e-4,
            "Expected facade UVs to span the wall width.");
        Assert.True(
            facadeUvs.Max(static uv => uv.Y) - facadeUvs.Min(static uv => uv.Y) >= 5.0 - 1e-4,
            "Expected facade UVs to span the wall height.");
    }

    [Fact]
    public async Task ExecuteAsyncUsesWallSurfaceSemanticForSlopedUntexturedBuildingWalls()
    {
        using TemporaryDirectory datasetRoot = new();
        CreateRuntimeThematicSurfaceBuildingFixture(datasetRoot.Path);

        StubResoniteSceneBuilder sceneBuilder = new();
        PlateauImportService service = new(sceneBuilder);

        ImportExecutionResult result = await service.ExecuteAsync(
            new PlateauImportRequest(
                Dataset: "tokyo23ku",
                MeshCode: "53394525",
                SourceKind: DatasetSourceKind.Local,
                LocalSourcePath: datasetRoot.Path,
                ServerUri: null),
            workRoot: "runtime/resonite");
        CapturedResoniteScene scene = result.Metadata.ToScene(sceneBuilder.CityObjects);

        ResoniteConstructionCityObject building = Assert.Single(scene.CityObjects);
        Assert.Contains(
            building.Materials,
            static material =>
                BundledDefaultMaterialFamilies.FacadeVariants.Contains(material.TexturePath!)
                && material.Projection == ResoniteMaterialProjection.Uv);
        Assert.Contains(
            building.Materials,
            static material =>
                BundledDefaultMaterialFamilies.RoofVariants.Contains(material.TexturePath!)
                && material.Projection == ResoniteMaterialProjection.Triplanar);
    }

    [Fact]
    public async Task ExecuteAsyncUsesSingleFacadeMaterialPerBuildingAcrossMultipleWalls()
    {
        using TemporaryDirectory datasetRoot = new();
        CreateRuntimeMultiWallBuildingFixture(datasetRoot.Path);

        StubResoniteSceneBuilder sceneBuilder = new();
        PlateauImportService service = new(sceneBuilder);

        ImportExecutionResult result = await service.ExecuteAsync(
            new PlateauImportRequest(
                Dataset: "tokyo23ku",
                MeshCode: "53394525",
                SourceKind: DatasetSourceKind.Local,
                LocalSourcePath: datasetRoot.Path,
                ServerUri: null),
            workRoot: "runtime/resonite");
        CapturedResoniteScene scene = result.Metadata.ToScene(sceneBuilder.CityObjects);

        ResoniteConstructionCityObject building = Assert.Single(scene.CityObjects);
        ResoniteMaterialBinding[] facadeMaterials = building.Materials
            .Where(static material =>
                BundledDefaultMaterialFamilies.FacadeVariants.Contains(material.TexturePath!)
                && material.Projection == ResoniteMaterialProjection.Uv)
            .ToArray();

        Assert.Single(facadeMaterials);
        Assert.DoesNotContain(
            facadeMaterials,
            static material => string.Equals(
                material.TexturePath,
                "default-materials/facade/PaintedPlaster012_2K-JPG_Color.jpg",
                StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExecuteAsyncSelectsBundledVariantDeterministicallyWithinFamily()
    {
        using TemporaryDirectory datasetRoot = new();
        CreateRuntimeUntexturedBuildingFixture(datasetRoot.Path);

        StubResoniteSceneBuilder firstSceneBuilder = new();
        StubResoniteSceneBuilder secondSceneBuilder = new();
        PlateauImportService firstService = new(firstSceneBuilder);
        PlateauImportService secondService = new(secondSceneBuilder);

        ImportExecutionResult first = await firstService.ExecuteAsync(
            new PlateauImportRequest(
                Dataset: "tokyo23ku",
                MeshCode: "53394525",
                SourceKind: DatasetSourceKind.Local,
                LocalSourcePath: datasetRoot.Path,
                ServerUri: null),
            workRoot: "runtime/resonite");
        ImportExecutionResult second = await secondService.ExecuteAsync(
            new PlateauImportRequest(
                Dataset: "tokyo23ku",
                MeshCode: "53394525",
                SourceKind: DatasetSourceKind.Local,
                LocalSourcePath: datasetRoot.Path,
                ServerUri: null),
            workRoot: "runtime/resonite");

        CapturedResoniteScene firstScene = first.Metadata.ToScene(firstSceneBuilder.CityObjects);
        CapturedResoniteScene secondScene = second.Metadata.ToScene(secondSceneBuilder.CityObjects);

        Assert.Equal(
            firstScene.CityObjects.SelectMany(static cityObject => cityObject.Materials).Select(static material => material.TexturePath),
            secondScene.CityObjects.SelectMany(static cityObject => cityObject.Materials).Select(static material => material.TexturePath));
    }

    [Fact]
    public async Task ExecuteAsyncResolvesServerSourceBeforeBuildingScene()
    {
        StubResoniteSceneBuilder sceneBuilder = new();
        string fixturePath = TestData.GetFixturePath("LocalPlateauDataset");
        RecordingDatasetSourceResolver resolver = new(
            new PlateauImportRequest(
                Dataset: "tokyo23ku",
                MeshCode: "53394525",
                SourceKind: DatasetSourceKind.Local,
                LocalSourcePath: fixturePath,
                ServerUri: null));
        PlateauImportService service = new(sceneBuilder, resolver);

        ImportExecutionResult result = await service.ExecuteAsync(
            new PlateauImportRequest(
                Dataset: "tokyo23ku",
                MeshCode: "53394525",
                SourceKind: DatasetSourceKind.Remote,
                LocalSourcePath: null,
                ServerUri: new Uri("https://search.ckan.jp/backend/api/", UriKind.Absolute)),
            workRoot: "runtime/resonite");

        Assert.Equal(DatasetSourceKind.Remote, Assert.Single(resolver.Requests).SourceKind);
        Assert.Equal(DatasetSourceKind.Local, result.Metadata.Request.SourceKind);
        Assert.Equal(fixturePath, result.Metadata.Request.LocalSourcePath);
    }

    private sealed class StubResoniteSceneBuilder : IResoniteSceneBuilder
    {
        public List<ResoniteConstructionCityObject> CityObjects { get; } = [];

        public Task BeginAsync(
            ResoniteConstructionMetadata metadata,
            string workRoot,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(metadata);
            ArgumentException.ThrowIfNullOrWhiteSpace(workRoot);
            return Task.CompletedTask;
        }

        public Task ProcessCityObjectAsync(
            ResoniteConstructionCityObject cityObject,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(cityObject);
            CityObjects.Add(cityObject);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<string>> CompleteAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<string>>(["stub://resonite"]);
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingDatasetSourceResolver(PlateauImportRequest resolvedRequest) : IPlateauDatasetSourceResolver
    {
        public List<PlateauImportRequest> Requests { get; } = [];

        public Task<PlateauImportRequest> ResolveAsync(
            PlateauImportRequest request,
            string workRoot,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(resolvedRequest);
        }
    }

    private static void CreateRuntimePackageFixture(
        string datasetRoot,
        string packageName,
        string packageNamespace,
        string cityObjectType,
        string displayName)
    {
        string packageDirectory = Path.Combine(datasetRoot, "udx", packageName, "53394525");
        Directory.CreateDirectory(packageDirectory);

        string xml =
            $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <core:CityModel xmlns:core="http://www.opengis.net/citygml/2.0" xmlns:gml="http://www.opengis.net/gml" xmlns:pkg="{packageNamespace}">
              <gml:boundedBy>
                <gml:Envelope srsName="http://www.opengis.net/def/crs/EPSG/0/6697" srsDimension="3">
                  <gml:lowerCorner>35.0000 139.0000 0</gml:lowerCorner>
                  <gml:upperCorner>35.0004 139.0004 4</gml:upperCorner>
                </gml:Envelope>
              </gml:boundedBy>
              <core:cityObjectMember>
                <pkg:{cityObjectType} gml:id="{packageName}-1">
                  <gml:name>{displayName}</gml:name>
                  <pkg:lod1MultiSurface>
                    <gml:MultiSurface>
                      <gml:surfaceMember>
                        <gml:Polygon gml:id="poly-{packageName}-1">
                          <gml:exterior>
                            <gml:LinearRing gml:id="ring-{packageName}-1">
                              <gml:posList>35.0000 139.0000 0 35.0003 139.0000 1 35.0003 139.0003 2 35.0000 139.0003 0 35.0000 139.0000 0</gml:posList>
                            </gml:LinearRing>
                          </gml:exterior>
                        </gml:Polygon>
                      </gml:surfaceMember>
                    </gml:MultiSurface>
                  </pkg:lod1MultiSurface>
                </pkg:{cityObjectType}>
              </core:cityObjectMember>
            </core:CityModel>
            """;

        File.WriteAllText(
            Path.Combine(packageDirectory, $"plateau_tokyo23ku_{packageName}_53394525.gml"),
            xml);
    }

    private static void CreateRuntimeComplexLandUseFixture(string datasetRoot)
    {
        string packageDirectory = Path.Combine(datasetRoot, "udx", "luse", "53394525");
        Directory.CreateDirectory(packageDirectory);

        string xml =
            """
            <?xml version="1.0" encoding="UTF-8"?>
            <core:CityModel xmlns:core="http://www.opengis.net/citygml/2.0" xmlns:gml="http://www.opengis.net/gml" xmlns:luse="http://www.opengis.net/citygml/landuse/2.0">
              <gml:boundedBy>
                <gml:Envelope srsDimension="3">
                  <gml:lowerCorner>0 0 0</gml:lowerCorner>
                  <gml:upperCorner>20 20 0</gml:upperCorner>
                </gml:Envelope>
              </gml:boundedBy>
              <core:cityObjectMember>
                <luse:LandUse gml:id="LND_concave">
                  <gml:name>Concave Land Use</gml:name>
                  <luse:lod1MultiSurface>
                    <gml:MultiSurface>
                      <gml:surfaceMember>
                        <gml:Polygon gml:id="poly-concave">
                          <gml:exterior>
                            <gml:LinearRing gml:id="ring-concave">
                              <gml:posList>0 0 0 4 0 0 4 1 0 1 1 0 1 4 0 0 4 0 0 0 0</gml:posList>
                            </gml:LinearRing>
                          </gml:exterior>
                        </gml:Polygon>
                      </gml:surfaceMember>
                    </gml:MultiSurface>
                  </luse:lod1MultiSurface>
                </luse:LandUse>
              </core:cityObjectMember>
              <core:cityObjectMember>
                <luse:LandUse gml:id="LND_hole">
                  <gml:name>Hole Land Use</gml:name>
                  <luse:lod1MultiSurface>
                    <gml:MultiSurface>
                      <gml:surfaceMember>
                        <gml:Polygon gml:id="poly-hole">
                          <gml:exterior>
                            <gml:LinearRing gml:id="ring-hole-outer">
                              <gml:posList>10 0 0 20 0 0 20 10 0 10 10 0 10 0 0</gml:posList>
                            </gml:LinearRing>
                          </gml:exterior>
                          <gml:interior>
                            <gml:LinearRing gml:id="ring-hole-inner">
                              <gml:posList>13 3 0 17 3 0 17 7 0 13 7 0 13 3 0</gml:posList>
                            </gml:LinearRing>
                          </gml:interior>
                        </gml:Polygon>
                      </gml:surfaceMember>
                    </gml:MultiSurface>
                  </luse:lod1MultiSurface>
                </luse:LandUse>
              </core:cityObjectMember>
            </core:CityModel>
            """;

        File.WriteAllText(
            Path.Combine(packageDirectory, "plateau_tokyo23ku_luse_53394525_complex.gml"),
            xml);
    }

    private static void CreateRuntimeVegetationFixture(string datasetRoot)
    {
        string packageDirectory = Path.Combine(datasetRoot, "udx", "veg", "53394525");
        Directory.CreateDirectory(packageDirectory);

        string xml =
            """
            <?xml version="1.0" encoding="UTF-8"?>
            <core:CityModel xmlns:app="http://www.opengis.net/citygml/appearance/2.0" xmlns:core="http://www.opengis.net/citygml/2.0" xmlns:gml="http://www.opengis.net/gml" xmlns:veg="http://www.opengis.net/citygml/vegetation/2.0">
              <gml:boundedBy>
                <gml:Envelope srsDimension="3">
                  <gml:lowerCorner>0 0 0</gml:lowerCorner>
                  <gml:upperCorner>10 10 10</gml:upperCorner>
                </gml:Envelope>
              </gml:boundedBy>
              <app:appearanceMember>
                <app:Appearance>
                  <app:surfaceDataMember>
                    <app:X3DMaterial>
                      <app:diffuseColor>0.45 0.28 0.12</app:diffuseColor>
                      <app:target uri="#poly-trunk" />
                    </app:X3DMaterial>
                  </app:surfaceDataMember>
                </app:Appearance>
              </app:appearanceMember>
              <core:cityObjectMember>
                <veg:SolitaryVegetationObject gml:id="veg-tree">
                  <gml:name>Vegetation Fixture</gml:name>
                  <veg:lod2MultiSurface>
                    <gml:MultiSurface>
                      <gml:surfaceMember>
                        <gml:Polygon gml:id="poly-trunk">
                          <gml:exterior>
                            <gml:LinearRing gml:id="ring-trunk">
                              <gml:posList>0 0 0 0 0 3 1 0 3 1 0 0 0 0 0</gml:posList>
                            </gml:LinearRing>
                          </gml:exterior>
                        </gml:Polygon>
                      </gml:surfaceMember>
                      <gml:surfaceMember>
                        <gml:Polygon gml:id="poly-leaf">
                          <gml:exterior>
                            <gml:LinearRing gml:id="ring-leaf">
                              <gml:posList>0 0 3 0 0 6 4 0 6 4 0 3 0 0 3</gml:posList>
                            </gml:LinearRing>
                          </gml:exterior>
                        </gml:Polygon>
                      </gml:surfaceMember>
                    </gml:MultiSurface>
                  </veg:lod2MultiSurface>
                </veg:SolitaryVegetationObject>
              </core:cityObjectMember>
            </core:CityModel>
            """;

        File.WriteAllText(
            Path.Combine(packageDirectory, "plateau_tokyo23ku_veg_53394525_colors.gml"),
            xml);
    }

    private static void CreateRuntimeWideDemFixture(string datasetRoot)
    {
        string packageDirectory = Path.Combine(datasetRoot, "udx", "dem", "53394525");
        Directory.CreateDirectory(packageDirectory);

        string xml =
            """
            <?xml version="1.0" encoding="UTF-8"?>
            <core:CityModel xmlns:core="http://www.opengis.net/citygml/2.0" xmlns:gml="http://www.opengis.net/gml" xmlns:dem="http://www.opengis.net/citygml/relief/2.0">
              <gml:boundedBy>
                <gml:Envelope srsDimension="3" srsName="EPSG:4326">
                  <gml:lowerCorner>35.0000 139.0000 0</gml:lowerCorner>
                  <gml:upperCorner>35.0100 139.0400 20</gml:upperCorner>
                </gml:Envelope>
              </gml:boundedBy>
              <core:cityObjectMember>
                <dem:ReliefFeature gml:id="dem-wide">
                  <gml:name>Wide Relief</gml:name>
                  <dem:reliefComponent>
                    <dem:TINRelief gml:id="dem-wide-component">
                      <dem:tin>
                        <gml:TriangulatedSurface>
                          <gml:trianglePatches>
                            <gml:Triangle gml:id="tri-dem-west">
                              <gml:exterior>
                                <gml:LinearRing gml:id="ring-dem-west">
                                  <gml:posList>35.0000 139.0000 0 35.0100 139.0000 5 35.0100 139.0180 10 35.0000 139.0000 0</gml:posList>
                                </gml:LinearRing>
                              </gml:exterior>
                            </gml:Triangle>
                            <gml:Triangle gml:id="tri-dem-east">
                              <gml:exterior>
                                <gml:LinearRing gml:id="ring-dem-east">
                                  <gml:posList>35.0000 139.0220 0 35.0100 139.0220 8 35.0100 139.0400 12 35.0000 139.0220 0</gml:posList>
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

        File.WriteAllText(
            Path.Combine(packageDirectory, "plateau_tokyo23ku_dem_53394525_wide.gml"),
            xml);
    }

    private static void CreateRuntimeDirectoryScopedDemFixture(string datasetRoot)
    {
        string buildingDirectory = Path.Combine(datasetRoot, "udx", "bldg", "53394525");
        Directory.CreateDirectory(buildingDirectory);
        File.WriteAllText(
            Path.Combine(buildingDirectory, "plateau_yokohama_bldg_53394525.gml"),
            """
            <?xml version="1.0" encoding="UTF-8"?>
            <core:CityModel xmlns:core="http://www.opengis.net/citygml/2.0" xmlns:gml="http://www.opengis.net/gml" xmlns:bldg="http://www.opengis.net/citygml/building/2.0">
              <gml:boundedBy>
                <gml:Envelope srsName="http://www.opengis.net/def/crs/EPSG/0/6697" srsDimension="3">
                  <gml:lowerCorner>35.4500 139.6300 0</gml:lowerCorner>
                  <gml:upperCorner>35.4505 139.6305 10</gml:upperCorner>
                </gml:Envelope>
              </gml:boundedBy>
              <core:cityObjectMember>
                <bldg:Building gml:id="bldg-dir-scope">
                  <gml:name>Directory Scoped Building</gml:name>
                  <bldg:lod1MultiSurface>
                    <gml:MultiSurface>
                      <gml:surfaceMember>
                        <gml:Polygon gml:id="poly-dir-scope-building">
                          <gml:exterior>
                            <gml:LinearRing gml:id="ring-dir-scope-building">
                              <gml:posList>35.4500 139.6300 0 35.4500 139.6303 0 35.4503 139.6300 0 35.4500 139.6300 0</gml:posList>
                            </gml:LinearRing>
                          </gml:exterior>
                        </gml:Polygon>
                      </gml:surfaceMember>
                    </gml:MultiSurface>
                  </bldg:lod1MultiSurface>
                </bldg:Building>
              </core:cityObjectMember>
            </core:CityModel>
            """);

        string demDirectory = Path.Combine(datasetRoot, "udx", "dem", "53394525");
        Directory.CreateDirectory(demDirectory);
        File.WriteAllText(
            Path.Combine(demDirectory, "plateau_yokohama_dem.gml"),
            """
            <?xml version="1.0" encoding="UTF-8"?>
            <core:CityModel xmlns:core="http://www.opengis.net/citygml/2.0" xmlns:gml="http://www.opengis.net/gml" xmlns:dem="http://www.opengis.net/citygml/relief/2.0">
              <gml:boundedBy>
                <gml:Envelope srsName="http://www.opengis.net/def/crs/EPSG/0/6697" srsDimension="3">
                  <gml:lowerCorner>35.4500 139.6300 5</gml:lowerCorner>
                  <gml:upperCorner>35.4505 139.6305 20</gml:upperCorner>
                </gml:Envelope>
              </gml:boundedBy>
              <core:cityObjectMember>
                <dem:ReliefFeature gml:id="dem-dir-scope">
                  <gml:name>Directory Scoped Relief</gml:name>
                  <dem:reliefComponent>
                    <dem:TINRelief gml:id="dem-dir-scope-component">
                      <dem:tin>
                        <gml:TriangulatedSurface srsName="http://www.opengis.net/def/crs/EPSG/0/6697" srsDimension="3">
                          <gml:trianglePatches>
                            <gml:Triangle gml:id="tri-dir-scope">
                              <gml:exterior>
                                <gml:LinearRing gml:id="ring-dir-scope">
                                  <gml:posList>35.4500 139.6300 5 35.4500 139.6305 10 35.4505 139.6300 15 35.4500 139.6300 5</gml:posList>
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
            """);
    }

    private static void CreateRuntimeTerrainAlignmentFixture(string datasetRoot)
    {
        string demDirectory = Path.Combine(datasetRoot, "udx", "dem", "53394525");
        Directory.CreateDirectory(demDirectory);
        File.WriteAllText(
            Path.Combine(demDirectory, "plateau_tokyo23ku_dem_53394525.gml"),
            """
            <?xml version="1.0" encoding="UTF-8"?>
            <core:CityModel xmlns:core="http://www.opengis.net/citygml/2.0" xmlns:gml="http://www.opengis.net/gml" xmlns:dem="http://www.opengis.net/citygml/relief/2.0">
              <gml:boundedBy>
                <gml:Envelope srsName="http://www.opengis.net/def/crs/EPSG/0/6697" srsDimension="3">
                  <gml:lowerCorner>35.0000 139.0000 10</gml:lowerCorner>
                  <gml:upperCorner>35.0010 139.0010 30</gml:upperCorner>
                </gml:Envelope>
              </gml:boundedBy>
              <core:cityObjectMember>
                <dem:ReliefFeature gml:id="dem-fit">
                  <gml:name>Alignment Relief</gml:name>
                  <dem:reliefComponent>
                    <dem:TINRelief gml:id="dem-fit-component">
                      <dem:tin>
                        <gml:TriangulatedSurface srsName="http://www.opengis.net/def/crs/EPSG/0/6697" srsDimension="3">
                          <gml:trianglePatches>
                            <gml:Triangle gml:id="tri-fit">
                              <gml:exterior>
                                <gml:LinearRing gml:id="ring-fit">
                                  <gml:posList>35.0000 139.0000 10 35.0000 139.0010 20 35.0010 139.0000 30 35.0000 139.0000 10</gml:posList>
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
            """);

        string tranDirectory = Path.Combine(datasetRoot, "udx", "tran", "53394525");
        Directory.CreateDirectory(Path.Combine(tranDirectory, "appearance"));
        File.WriteAllText(Path.Combine(tranDirectory, "appearance", "marking.png"), "fixture-marking-texture");
        File.WriteAllText(
            Path.Combine(tranDirectory, "plateau_tokyo23ku_tran_53394525.gml"),
            """
            <?xml version="1.0" encoding="UTF-8"?>
            <core:CityModel xmlns:app="http://www.opengis.net/citygml/appearance/2.0" xmlns:core="http://www.opengis.net/citygml/2.0" xmlns:gml="http://www.opengis.net/gml" xmlns:tran="http://www.opengis.net/citygml/transportation/2.0">
              <gml:boundedBy>
                <gml:Envelope srsName="http://www.opengis.net/def/crs/EPSG/0/6697" srsDimension="3">
                  <gml:lowerCorner>35.0000 139.0000 0</gml:lowerCorner>
                  <gml:upperCorner>35.0005 139.0005 0</gml:upperCorner>
                </gml:Envelope>
              </gml:boundedBy>
              <app:appearanceMember>
                <app:Appearance>
                  <app:surfaceDataMember>
                    <app:ParameterizedTexture>
                      <app:imageURI>appearance/marking.png</app:imageURI>
                      <app:target uri="#poly-road-fit">
                        <app:TexCoordList>
                          <app:textureCoordinates ring="#ring-road-fit">0 0 1 0 0 1 0 0</app:textureCoordinates>
                        </app:TexCoordList>
                      </app:target>
                    </app:ParameterizedTexture>
                  </app:surfaceDataMember>
                </app:Appearance>
              </app:appearanceMember>
              <core:cityObjectMember>
                <tran:Road gml:id="tran-fit">
                  <gml:name>Aligned Road</gml:name>
                  <tran:lod1MultiSurface>
                    <gml:MultiSurface>
                      <gml:surfaceMember>
                        <gml:Polygon gml:id="poly-road-fit">
                          <gml:exterior>
                            <gml:LinearRing gml:id="ring-road-fit">
                              <gml:posList>35.0000 139.0000 0 35.0000 139.0005 0 35.0005 139.0000 0 35.0000 139.0000 0</gml:posList>
                            </gml:LinearRing>
                          </gml:exterior>
                        </gml:Polygon>
                      </gml:surfaceMember>
                    </gml:MultiSurface>
                  </tran:lod1MultiSurface>
                </tran:Road>
              </core:cityObjectMember>
            </core:CityModel>
            """);

        string buildingDirectory = Path.Combine(datasetRoot, "udx", "bldg", "53394525");
        Directory.CreateDirectory(buildingDirectory);
        File.WriteAllText(
            Path.Combine(buildingDirectory, "plateau_tokyo23ku_bldg_53394525.gml"),
            """
            <?xml version="1.0" encoding="UTF-8"?>
            <core:CityModel xmlns:core="http://www.opengis.net/citygml/2.0" xmlns:gml="http://www.opengis.net/gml" xmlns:bldg="http://www.opengis.net/citygml/building/2.0">
              <gml:boundedBy>
                <gml:Envelope srsName="http://www.opengis.net/def/crs/EPSG/0/6697" srsDimension="3">
                  <gml:lowerCorner>35.0000 139.0000 0</gml:lowerCorner>
                  <gml:upperCorner>35.0005 139.0005 0</gml:upperCorner>
                </gml:Envelope>
              </gml:boundedBy>
              <core:cityObjectMember>
                <bldg:Building gml:id="bldg-fit">
                  <gml:name>Raised Building</gml:name>
                  <bldg:lod1MultiSurface>
                    <gml:MultiSurface>
                      <gml:surfaceMember>
                        <gml:Polygon gml:id="poly-building-fit">
                          <gml:exterior>
                            <gml:LinearRing gml:id="ring-building-fit">
                              <gml:posList>35.0000 139.0000 0 35.0000 139.0005 0 35.0005 139.0000 0 35.0000 139.0000 0</gml:posList>
                            </gml:LinearRing>
                          </gml:exterior>
                        </gml:Polygon>
                      </gml:surfaceMember>
                    </gml:MultiSurface>
                  </bldg:lod1MultiSurface>
                </bldg:Building>
              </core:cityObjectMember>
            </core:CityModel>
            """);
    }

    private static void CreateRuntimeHighLodTransportationFixture(string datasetRoot)
    {
        string demDirectory = Path.Combine(datasetRoot, "udx", "dem", "53394525");
        Directory.CreateDirectory(demDirectory);
        File.WriteAllText(
            Path.Combine(demDirectory, "plateau_tokyo23ku_dem_53394525.gml"),
            """
            <?xml version="1.0" encoding="UTF-8"?>
            <core:CityModel xmlns:core="http://www.opengis.net/citygml/2.0" xmlns:gml="http://www.opengis.net/gml" xmlns:dem="http://www.opengis.net/citygml/relief/2.0">
              <gml:boundedBy>
                <gml:Envelope srsName="http://www.opengis.net/def/crs/EPSG/0/6697" srsDimension="3">
                  <gml:lowerCorner>35.0000 139.0000 10</gml:lowerCorner>
                  <gml:upperCorner>35.0010 139.0010 30</gml:upperCorner>
                </gml:Envelope>
              </gml:boundedBy>
              <core:cityObjectMember>
                <dem:ReliefFeature gml:id="dem-precise-road">
                  <gml:name>Precise Road Relief</gml:name>
                  <dem:reliefComponent>
                    <dem:TINRelief gml:id="dem-precise-road-component">
                      <dem:tin>
                        <gml:TriangulatedSurface srsName="http://www.opengis.net/def/crs/EPSG/0/6697" srsDimension="3">
                          <gml:trianglePatches>
                            <gml:Triangle gml:id="tri-precise-road">
                              <gml:exterior>
                                <gml:LinearRing gml:id="ring-precise-road">
                                  <gml:posList>35.0000 139.0000 10 35.0000 139.0010 20 35.0010 139.0000 30 35.0000 139.0000 10</gml:posList>
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
            """);

        string tranDirectory = Path.Combine(datasetRoot, "udx", "tran", "53394525");
        Directory.CreateDirectory(tranDirectory);
        File.WriteAllText(
            Path.Combine(tranDirectory, "plateau_tokyo23ku_tran_53394525.gml"),
            """
            <?xml version="1.0" encoding="UTF-8"?>
            <core:CityModel xmlns:core="http://www.opengis.net/citygml/2.0" xmlns:gml="http://www.opengis.net/gml" xmlns:tran="http://www.opengis.net/citygml/transportation/2.0">
              <gml:boundedBy>
                <gml:Envelope srsName="http://www.opengis.net/def/crs/EPSG/0/6697" srsDimension="3">
                  <gml:lowerCorner>35.0000 139.0000 0</gml:lowerCorner>
                  <gml:upperCorner>35.0005 139.0005 0</gml:upperCorner>
                </gml:Envelope>
              </gml:boundedBy>
              <core:cityObjectMember>
                <tran:Road gml:id="tran-precise-road">
                  <gml:name>Precise Road</gml:name>
                  <tran:lod3MultiSurface>
                    <gml:MultiSurface>
                      <gml:surfaceMember>
                        <gml:Polygon gml:id="poly-precise-road">
                          <gml:exterior>
                            <gml:LinearRing gml:id="ring-precise-road">
                              <gml:posList>35.0000 139.0000 0 35.0000 139.0005 0 35.0005 139.0000 0 35.0000 139.0000 0</gml:posList>
                            </gml:LinearRing>
                          </gml:exterior>
                        </gml:Polygon>
                      </gml:surfaceMember>
                    </gml:MultiSurface>
                  </tran:lod3MultiSurface>
                </tran:Road>
              </core:cityObjectMember>
            </core:CityModel>
            """);
    }

    private static void CreateRuntimeTexturelessTransportationFixture(string datasetRoot)
    {
        string tranDirectory = Path.Combine(datasetRoot, "udx", "tran", "53394525");
        Directory.CreateDirectory(tranDirectory);
        File.WriteAllText(
            Path.Combine(tranDirectory, "plateau_tokyo23ku_tran_53394525.gml"),
            """
            <?xml version="1.0" encoding="UTF-8"?>
            <core:CityModel xmlns:core="http://www.opengis.net/citygml/2.0" xmlns:gml="http://www.opengis.net/gml" xmlns:tran="http://www.opengis.net/citygml/transportation/2.0">
              <gml:boundedBy>
                <gml:Envelope srsDimension="3">
                  <gml:lowerCorner>0 0 0</gml:lowerCorner>
                  <gml:upperCorner>8 0 2</gml:upperCorner>
                </gml:Envelope>
              </gml:boundedBy>
              <core:cityObjectMember>
                <tran:Road gml:id="tran-generated-uv">
                  <gml:name>Generated UV Road</gml:name>
                  <tran:lod2MultiSurface>
                    <gml:MultiSurface>
                              <gml:surfaceMember>
                        <gml:Polygon gml:id="poly-generated-uv-road">
                          <gml:exterior>
                            <gml:LinearRing gml:id="ring-generated-uv-road">
                              <gml:posList>0 0 0 8 0 0 8 2 0 0 2 0 0 0 0</gml:posList>
                            </gml:LinearRing>
                          </gml:exterior>
                        </gml:Polygon>
                      </gml:surfaceMember>
                    </gml:MultiSurface>
                  </tran:lod2MultiSurface>
                </tran:Road>
              </core:cityObjectMember>
            </core:CityModel>
            """);
    }

    private static void CreateRuntimeMultiLodFixture(string datasetRoot)
    {
        string packageDirectory = Path.Combine(datasetRoot, "udx", "bldg", "53394525");
        Directory.CreateDirectory(packageDirectory);

        string xml =
            """
            <?xml version="1.0" encoding="UTF-8"?>
            <core:CityModel xmlns:core="http://www.opengis.net/citygml/2.0" xmlns:gml="http://www.opengis.net/gml" xmlns:bldg="http://www.opengis.net/citygml/building/2.0">
              <gml:boundedBy>
                <gml:Envelope srsDimension="3">
                  <gml:lowerCorner>0 0 0</gml:lowerCorner>
                  <gml:upperCorner>10 10 0</gml:upperCorner>
                </gml:Envelope>
              </gml:boundedBy>
              <core:cityObjectMember>
                <bldg:Building gml:id="bldg-multi-lod">
                  <gml:name>Multi LOD Building</gml:name>
                  <bldg:lod2MultiSurface>
                    <gml:MultiSurface>
                      <gml:surfaceMember>
                        <gml:Polygon gml:id="poly-lod2">
                          <gml:exterior>
                            <gml:LinearRing gml:id="ring-lod2">
                              <gml:posList>0 0 0 10 0 0 10 10 0 0 10 0 0 0 0</gml:posList>
                            </gml:LinearRing>
                          </gml:exterior>
                        </gml:Polygon>
                      </gml:surfaceMember>
                    </gml:MultiSurface>
                  </bldg:lod2MultiSurface>
                  <bldg:lod3MultiSurface>
                    <gml:MultiSurface>
                      <gml:surfaceMember>
                        <gml:Polygon gml:id="poly-lod3">
                          <gml:exterior>
                            <gml:LinearRing gml:id="ring-lod3">
                              <gml:posList>0 0 0 5 0 0 5 5 0 0 5 0 0 0 0</gml:posList>
                            </gml:LinearRing>
                          </gml:exterior>
                        </gml:Polygon>
                      </gml:surfaceMember>
                    </gml:MultiSurface>
                  </bldg:lod3MultiSurface>
                </bldg:Building>
              </core:cityObjectMember>
            </core:CityModel>
            """;

        File.WriteAllText(
            Path.Combine(packageDirectory, "plateau_tokyo23ku_bldg_53394525_multi_lod.gml"),
            xml);
    }

    private static void CreateRuntimeUntexturedBuildingFixture(string datasetRoot)
    {
        string packageDirectory = Path.Combine(datasetRoot, "udx", "bldg", "53394525");
        Directory.CreateDirectory(packageDirectory);

        string xml =
            """
            <?xml version="1.0" encoding="UTF-8"?>
            <core:CityModel xmlns:core="http://www.opengis.net/citygml/2.0" xmlns:gml="http://www.opengis.net/gml" xmlns:bldg="http://www.opengis.net/citygml/building/2.0">
              <gml:boundedBy>
                <gml:Envelope srsDimension="3">
                  <gml:lowerCorner>0 0 0</gml:lowerCorner>
                  <gml:upperCorner>10 10 10</gml:upperCorner>
                </gml:Envelope>
              </gml:boundedBy>
              <core:cityObjectMember>
                <bldg:Building gml:id="bldg-uv-vs-triplanar">
                  <gml:name>Facade And Roof Building</gml:name>
                  <bldg:lod2MultiSurface>
                    <gml:MultiSurface>
                      <gml:surfaceMember>
                        <gml:Polygon gml:id="poly-wall">
                          <gml:exterior>
                            <gml:LinearRing gml:id="ring-wall">
                              <gml:posList>0 0 0 0 0 5 5 0 5 5 0 0 0 0 0</gml:posList>
                            </gml:LinearRing>
                          </gml:exterior>
                        </gml:Polygon>
                      </gml:surfaceMember>
                      <gml:surfaceMember>
                        <gml:Polygon gml:id="poly-roof">
                          <gml:exterior>
                            <gml:LinearRing gml:id="ring-roof">
                              <gml:posList>0 0 5 5 0 5 5 5 5 0 5 5 0 0 5</gml:posList>
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
            Path.Combine(packageDirectory, "plateau_tokyo23ku_bldg_53394525_untextured.gml"),
            xml);
    }

    private static void CreateRuntimeThematicSurfaceBuildingFixture(string datasetRoot)
    {
        string packageDirectory = Path.Combine(datasetRoot, "udx", "bldg", "53394525");
        Directory.CreateDirectory(packageDirectory);

        string xml =
            """
            <?xml version="1.0" encoding="UTF-8"?>
            <core:CityModel xmlns:core="http://www.opengis.net/citygml/2.0" xmlns:gml="http://www.opengis.net/gml" xmlns:bldg="http://www.opengis.net/citygml/building/2.0">
              <gml:boundedBy>
                <gml:Envelope srsDimension="3">
                  <gml:lowerCorner>0 0 0</gml:lowerCorner>
                  <gml:upperCorner>10 10 10</gml:upperCorner>
                </gml:Envelope>
              </gml:boundedBy>
              <core:cityObjectMember>
                <bldg:Building gml:id="bldg-thematic-surfaces">
                  <gml:name>Thematic Surface Building</gml:name>
                  <bldg:boundedBy>
                    <bldg:WallSurface gml:id="wall-1">
                      <bldg:lod2MultiSurface>
                        <gml:MultiSurface>
                          <gml:surfaceMember>
                            <gml:Polygon gml:id="poly-themed-wall">
                              <gml:exterior>
                                <gml:LinearRing gml:id="ring-themed-wall">
                                  <gml:posList>0 0 0 0 5 5 5 5 5 5 0 0 0 0 0</gml:posList>
                                </gml:LinearRing>
                              </gml:exterior>
                            </gml:Polygon>
                          </gml:surfaceMember>
                        </gml:MultiSurface>
                      </bldg:lod2MultiSurface>
                    </bldg:WallSurface>
                  </bldg:boundedBy>
                  <bldg:boundedBy>
                    <bldg:RoofSurface gml:id="roof-1">
                      <bldg:lod2MultiSurface>
                        <gml:MultiSurface>
                          <gml:surfaceMember>
                            <gml:Polygon gml:id="poly-themed-roof">
                              <gml:exterior>
                                <gml:LinearRing gml:id="ring-themed-roof">
                                  <gml:posList>0 5 5 5 5 5 5 10 5 0 10 5 0 5 5</gml:posList>
                                </gml:LinearRing>
                              </gml:exterior>
                            </gml:Polygon>
                          </gml:surfaceMember>
                        </gml:MultiSurface>
                      </bldg:lod2MultiSurface>
                    </bldg:RoofSurface>
                  </bldg:boundedBy>
                </bldg:Building>
              </core:cityObjectMember>
            </core:CityModel>
            """;

        File.WriteAllText(
            Path.Combine(packageDirectory, "plateau_tokyo23ku_bldg_53394525_thematic.gml"),
            xml);
    }

    private static void CreateRuntimeMultiWallBuildingFixture(string datasetRoot)
    {
        string packageDirectory = Path.Combine(datasetRoot, "udx", "bldg", "53394525");
        Directory.CreateDirectory(packageDirectory);

        string xml =
            """
            <?xml version="1.0" encoding="UTF-8"?>
            <core:CityModel xmlns:core="http://www.opengis.net/citygml/2.0" xmlns:gml="http://www.opengis.net/gml" xmlns:bldg="http://www.opengis.net/citygml/building/2.0">
              <gml:boundedBy>
                <gml:Envelope srsDimension="3">
                  <gml:lowerCorner>0 0 0</gml:lowerCorner>
                  <gml:upperCorner>10 10 10</gml:upperCorner>
                </gml:Envelope>
              </gml:boundedBy>
              <core:cityObjectMember>
                <bldg:Building gml:id="bldg-multi-wall">
                  <gml:name>Multi Wall Building</gml:name>
                  <bldg:lod2MultiSurface>
                    <gml:MultiSurface>
                      <gml:surfaceMember>
                        <gml:Polygon gml:id="poly-wall-a">
                          <gml:exterior>
                            <gml:LinearRing gml:id="ring-wall-a">
                              <gml:posList>0 0 0 0 0 5 5 0 5 5 0 0 0 0 0</gml:posList>
                            </gml:LinearRing>
                          </gml:exterior>
                        </gml:Polygon>
                      </gml:surfaceMember>
                      <gml:surfaceMember>
                        <gml:Polygon gml:id="poly-wall-b">
                          <gml:exterior>
                            <gml:LinearRing gml:id="ring-wall-b">
                              <gml:posList>5 0 0 5 0 5 10 0 5 10 0 0 5 0 0</gml:posList>
                            </gml:LinearRing>
                          </gml:exterior>
                        </gml:Polygon>
                      </gml:surfaceMember>
                      <gml:surfaceMember>
                        <gml:Polygon gml:id="poly-roof-a">
                          <gml:exterior>
                            <gml:LinearRing gml:id="ring-roof-a">
                              <gml:posList>0 0 5 10 0 5 10 10 5 0 10 5 0 0 5</gml:posList>
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
            Path.Combine(packageDirectory, "plateau_tokyo23ku_bldg_53394525_multiwall.gml"),
            xml);
    }

    private static void CreateRuntimeTriangleFixture(string datasetRoot)
    {
        string packageDirectory = Path.Combine(datasetRoot, "udx", "bldg", "53394525");
        Directory.CreateDirectory(packageDirectory);

        string xml =
            """
            <?xml version="1.0" encoding="UTF-8"?>
            <core:CityModel xmlns:core="http://www.opengis.net/citygml/2.0" xmlns:gml="http://www.opengis.net/gml" xmlns:bldg="http://www.opengis.net/citygml/building/2.0">
              <gml:boundedBy>
                <gml:Envelope srsDimension="3">
                  <gml:lowerCorner>0 0 0</gml:lowerCorner>
                  <gml:upperCorner>1 1 0</gml:upperCorner>
                </gml:Envelope>
              </gml:boundedBy>
              <core:cityObjectMember>
                <bldg:Building gml:id="bldg-triangle">
                  <gml:name>Triangle Building</gml:name>
                  <bldg:lod1MultiSurface>
                    <gml:MultiSurface>
                      <gml:surfaceMember>
                        <gml:Polygon gml:id="poly-triangle">
                          <gml:exterior>
                            <gml:LinearRing gml:id="ring-triangle">
                              <gml:posList>0 0 0 1 0 0 0 1 0 0 0 0</gml:posList>
                            </gml:LinearRing>
                          </gml:exterior>
                        </gml:Polygon>
                      </gml:surfaceMember>
                    </gml:MultiSurface>
                  </bldg:lod1MultiSurface>
                </bldg:Building>
              </core:cityObjectMember>
            </core:CityModel>
            """;

        File.WriteAllText(
            Path.Combine(packageDirectory, "plateau_tokyo23ku_bldg_53394525_triangle.gml"),
            xml);
    }

    private static double ComputeMeshArea(ResoniteImportedMesh mesh)
    {
        double area = 0.0;

        foreach (ResoniteMeshSubmesh submesh in mesh.Submeshes)
        {
            for (int index = 0; index + 2 < submesh.TriangleVertexIndices.Count; index += 3)
            {
                ResoniteFloat3 position0 = mesh.Vertices[submesh.TriangleVertexIndices[index]].Position;
                ResoniteFloat3 position1 = mesh.Vertices[submesh.TriangleVertexIndices[index + 1]].Position;
                ResoniteFloat3 position2 = mesh.Vertices[submesh.TriangleVertexIndices[index + 2]].Position;

                double ax = position1.X - position0.X;
                double ay = position1.Y - position0.Y;
                double az = position1.Z - position0.Z;
                double bx = position2.X - position0.X;
                double by = position2.Y - position0.Y;
                double bz = position2.Z - position0.Z;

                double crossX = (ay * bz) - (az * by);
                double crossY = (az * bx) - (ax * bz);
                double crossZ = (ax * by) - (ay * bx);
                area += Math.Sqrt((crossX * crossX) + (crossY * crossY) + (crossZ * crossZ)) / 2.0;
            }
        }

        return area;
    }

    private static ResoniteFloat3 ComputeTriangleNormal(ResoniteImportedMesh mesh, ResoniteMeshSubmesh submesh)
    {
        int index0 = submesh.TriangleVertexIndices[0];
        int index1 = submesh.TriangleVertexIndices[1];
        int index2 = submesh.TriangleVertexIndices[2];
        ResoniteFloat3 position0 = mesh.Vertices[index0].Position;
        ResoniteFloat3 position1 = mesh.Vertices[index1].Position;
        ResoniteFloat3 position2 = mesh.Vertices[index2].Position;

        double ax = position1.X - position0.X;
        double ay = position1.Y - position0.Y;
        double az = position1.Z - position0.Z;
        double bx = position2.X - position0.X;
        double by = position2.Y - position0.Y;
        double bz = position2.Z - position0.Z;

        return new ResoniteFloat3(
            (ay * bz) - (az * by),
            (az * bx) - (ax * bz),
            (ax * by) - (ay * bx));
    }

    private static double Dot(ResoniteFloat3 left, ResoniteFloat3 right)
    {
        return (left.X * right.X) + (left.Y * right.Y) + (left.Z * right.Z);
    }

    internal sealed record CapturedResoniteScene(
        ResoniteConstructionMetadata Metadata,
        IReadOnlyList<ResoniteConstructionCityObject> CityObjects);

    private static bool Approximately(double actual, double expected)
    {
        return Math.Abs(actual - expected) < 1e-4;
    }
}

internal static class PlateauImportServiceTestSceneExtensions
{
    public static PlateauImportServiceTests.CapturedResoniteScene ToScene(
        this ResoniteConstructionMetadata metadata,
        IReadOnlyList<ResoniteConstructionCityObject> cityObjects)
    {
        return new PlateauImportServiceTests.CapturedResoniteScene(metadata, cityObjects);
    }
}
