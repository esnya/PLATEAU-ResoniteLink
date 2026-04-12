using System.Diagnostics.CodeAnalysis;
using System.IO.Compression;
using System.Reflection;

using Plateau.ResoniteLink.Application.Importing;
using Plateau.ResoniteLink.Domain.Importing;

using SharpCompress.Common;
using SharpCompress.Writers.SevenZip;

namespace Plateau.ResoniteLink.Tests.Application;

[SuppressMessage(
    "Reliability",
    "CA2000:Dispose objects before losing scope",
    Justification = "The tests intentionally hand ownership to PlateauImportService or helper methods.")]
[Trait("Category", "Slow")]
public sealed class PlateauImportServiceTests
{
    private static readonly HttpClient SharedDatasetSourceResolverHttpClient = new();

    private static PlateauImportService CreateService(
        IResoniteSceneBuilder sceneBuilder,
        IPlateauDatasetSourceResolver? datasetSourceResolver = null,
        IResoniteConstructionSourceFactory? constructionSourceFactory = null,
        Action<string>? progressReporter = null)
    {
        return new PlateauImportService(
            sceneBuilder,
            datasetSourceResolver ?? new CkanPlateauDatasetSourceResolver(SharedDatasetSourceResolverHttpClient),
            constructionSourceFactory ?? CreateConstructionSourceFactory(),
            progressReporter);
    }

    private static LocalCityGmlConstructionSourceFactory CreateConstructionSourceFactory()
    {
        return new LocalCityGmlConstructionSourceFactory(
            new LocalCityGmlDocumentReader(),
            new LocalCityGmlConstructionComposer(
                new LocalCityGmlGeometryProjector(new DefaultMaterialResolver())));
    }

    [Fact]
    public async Task ExecuteAsyncBuildsNormalizedScene()
    {
        StubResoniteSceneBuilder sceneBuilder = new();
        PlateauImportService service = CreateService(sceneBuilder);
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
    public async Task ExecuteAsyncUsesDatasetScopedWorkRoot()
    {
        StubResoniteSceneBuilder sceneBuilder = new();
        RecordingDatasetSourceResolver datasetSourceResolver = new(
            new PlateauImportRequest(
                Dataset: "tokyo23ku",
                MeshCode: "53394525",
                SourceKind: DatasetSourceKind.Local,
                LocalSourcePath: "/resolved/source",
                ServerUri: null));
        PlateauImportService service = new(
            sceneBuilder,
            datasetSourceResolver,
            constructionSourceFactory: new RecordingConstructionSourceFactory(CreateStubConstructionSource()));
        using TemporaryDirectory workRoot = new();

        await service.ExecuteAsync(
            new PlateauImportRequest(
                Dataset: "tokyo23ku",
                MeshCode: "53394525",
                SourceKind: DatasetSourceKind.Remote,
                LocalSourcePath: null,
                ServerUri: new Uri("https://example.invalid/source.zip", UriKind.Absolute)),
            workRoot.Path);

        string expectedDatasetRoot = Path.Combine(workRoot.Path, "tokyo23ku");
        Assert.Equal(expectedDatasetRoot, Assert.Single(datasetSourceResolver.WorkRoots));
        Assert.Equal(expectedDatasetRoot, Assert.Single(sceneBuilder.BeginWorkRoots));
    }

    [Fact]
    public async Task ExecuteAsyncPrewarmsCommonMaterialsDuringStreaming()
    {
        StubResoniteSceneBuilder sceneBuilder = new();
        RecordingDatasetSourceResolver datasetSourceResolver = new(
            new PlateauImportRequest(
                Dataset: "tokyo23ku",
                MeshCode: "53394525",
                SourceKind: DatasetSourceKind.Local,
                LocalSourcePath: "/resolved/source",
                ServerUri: null));
        PlateauImportService service = new(
            sceneBuilder,
            datasetSourceResolver,
            constructionSourceFactory: new RecordingConstructionSourceFactory(CreateStubConstructionSource()));

        await service.ExecuteAsync(
            new PlateauImportRequest(
                Dataset: "tokyo23ku",
                MeshCode: "53394525",
                SourceKind: DatasetSourceKind.Remote,
                LocalSourcePath: null,
                ServerUri: new Uri("https://example.invalid/source.zip", UriKind.Absolute)),
            workRoot: "runtime/resonite");

        ResoniteMaterialBinding commonMaterial = Assert.Single(sceneBuilder.CommonMaterials);
        Assert.Equal("stub-material", commonMaterial.MaterialKey);
        Assert.Equal(ResoniteMaterialAssetScope.Common, commonMaterial.AssetScope);
    }

    [Fact]
    public async Task ExecuteAsyncBuildsNormalizedSceneFromZipArchive()
    {
        using TemporaryDirectory archiveRoot = new();
        string archivePath = Path.Combine(archiveRoot.Path, "local-dataset.zip");
        CreateZipArchiveFromDirectory(TestData.GetFixturePath("LocalPlateauDataset"), archivePath);

        StubResoniteSceneBuilder sceneBuilder = new();
        PlateauImportService service = CreateService(sceneBuilder);

        ImportExecutionResult result = await service.ExecuteAsync(
            new PlateauImportRequest(
                Dataset: "tokyo23ku",
                MeshCode: "53394525",
                SourceKind: DatasetSourceKind.Local,
                LocalSourcePath: archivePath,
                ServerUri: null),
            workRoot: "runtime/resonite");

        CapturedResoniteScene scene = result.Metadata.ToScene(sceneBuilder.CityObjects);
        Assert.Equal(["bldg"], result.Metadata.SourceDataset.PackageNames);
        Assert.Contains(
            "udx/bldg/53394525/plateau_tokyo23ku_bldg_53394525.gml",
            result.Metadata.SourceDataset.SourceFiles);
        Assert.Equal(2, scene.CityObjects.Count);
        Assert.Contains(
            scene.CityObjects,
            static cityObject => cityObject.DisplayName == "Building One");
    }

    [Fact]
    public async Task ExecuteAsyncBuildsNormalizedSceneFromSevenZipArchive()
    {
        using TemporaryDirectory archiveRoot = new();
        string archivePath = Path.Combine(archiveRoot.Path, "local-dataset.7z");
        CreateSevenZipArchiveFromDirectory(TestData.GetFixturePath("LocalPlateauDataset"), archivePath);

        StubResoniteSceneBuilder sceneBuilder = new();
        PlateauImportService service = CreateService(sceneBuilder);

        ImportExecutionResult result = await service.ExecuteAsync(
            new PlateauImportRequest(
                Dataset: "tokyo23ku",
                MeshCode: "53394525",
                SourceKind: DatasetSourceKind.Local,
                LocalSourcePath: archivePath,
                ServerUri: null),
            workRoot: "runtime/resonite");

        CapturedResoniteScene scene = result.Metadata.ToScene(sceneBuilder.CityObjects);
        Assert.Equal(["bldg"], result.Metadata.SourceDataset.PackageNames);
        Assert.Contains(
            "udx/bldg/53394525/plateau_tokyo23ku_bldg_53394525.gml",
            result.Metadata.SourceDataset.SourceFiles);
        Assert.Equal(2, scene.CityObjects.Count);
        Assert.Contains(
            scene.CityObjects,
            static cityObject => cityObject.DisplayName == "Building One");
    }

    [Fact]
    public async Task ExecuteAsyncBuildsCityObjectsAcrossPackagesAndKeepsDetailedModelTextures()
    {
        StubResoniteSceneBuilder sceneBuilder = new();
        PlateauImportService service = CreateService(sceneBuilder);
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
        Assert.StartsWith(
            LocalCityGmlResonitePlanBuilder.DefaultDemTerrainTexturePath,
            Assert.Single(relief.Materials).TexturePath,
            StringComparison.Ordinal);
        Assert.Equal(ResoniteTextureSourceKind.Bundled, Assert.Single(relief.Materials).TextureSourceKind);
        Assert.Equal(ResoniteMaterialProjection.Uv, Assert.Single(relief.Materials).Projection);
        TerrainTextureOverlay demTerrainTexture = Assert.Single(result.Metadata.SourceDataset.TerrainTextureOverlays);
        Assert.Equal("dem", demTerrainTexture.PackageName);
        Assert.StartsWith(
            LocalCityGmlResonitePlanBuilder.DefaultDemTerrainTexturePath,
            demTerrainTexture.TexturePath,
            StringComparison.Ordinal);
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
        Assert.Contains(sceneBuilder.CityObjects, static cityObject => cityObject.PackageName == "bldg");
        Assert.Contains(sceneBuilder.CityObjects, static cityObject => cityObject.PackageName == "dem");
    }

    [Fact]
    public async Task ExecuteAsyncSupportsRegexMeshCodeSelection()
    {
        StubResoniteSceneBuilder sceneBuilder = new();
        PlateauImportService service = CreateService(sceneBuilder);
        string fixturePath = TestData.GetFixturePath("LocalPlateauDatasetMixedObjects");

        ImportExecutionResult result = await service.ExecuteAsync(
            new PlateauImportRequest(
                Dataset: "tokyo23ku",
                MeshCode: "5339452[56]",
                SourceKind: DatasetSourceKind.Local,
                LocalSourcePath: fixturePath,
                ServerUri: null,
                PackageNames: ["bldg", "dem"]),
            workRoot: "runtime/resonite");

        Assert.Equal("5339452[56]", result.Metadata.Request.MeshCode);
        Assert.Equal(["bldg", "dem"], result.Metadata.SourceDataset.PackageNames);
        Assert.Contains(
            "udx/bldg/53394525/plateau_tokyo23ku_bldg_53394525.gml",
            result.Metadata.SourceDataset.SourceFiles);
        Assert.Contains(
            "udx/dem/53394525/plateau_tokyo23ku_dem_53394525.gml",
            result.Metadata.SourceDataset.SourceFiles);
        Assert.Equal(35.6875, result.Metadata.LocalOrigin.Latitude, precision: 4);
        Assert.Equal(139.69375, result.Metadata.LocalOrigin.Longitude, precision: 4);
    }

    [Fact]
    public async Task ExecuteAsyncNormalizesWhitespacePaddedLocalPathAndPackageMaps()
    {
        StubResoniteSceneBuilder sceneBuilder = new();
        PlateauImportService service = CreateService(sceneBuilder);
        string fixturePath = TestData.GetFixturePath("LocalPlateauDatasetMixedObjects");

        ImportExecutionResult result = await service.ExecuteAsync(
            new PlateauImportRequest(
                Dataset: "tokyo23ku",
                MeshCode: "53394525",
                SourceKind: DatasetSourceKind.Local,
                LocalSourcePath: $"  {fixturePath}  ",
                ServerUri: null,
                PackageNames: [" waterbody ", " tran "],
                ExcludeLodLevelsByPackage: new Dictionary<string, IReadOnlySet<int>>
                {
                    ["waterbody"] = new HashSet<int> { 1 },
                },
                PackagePatterns: new Dictionary<string, string>
                {
                    ["waterbody"] = "*Water*",
                }),
            workRoot: "runtime/resonite");

        Assert.Equal(["wtr", "tran"], result.Metadata.Request.PackageNames);
        Assert.NotNull(result.Metadata.Request.ExcludeLodLevelsByPackage);
        Assert.True(result.Metadata.Request.ExcludeLodLevelsByPackage.ContainsKey("wtr"));
        Assert.NotNull(result.Metadata.Request.PackagePatterns);
        Assert.Equal("*Water*", result.Metadata.Request.PackagePatterns["wtr"]);
    }

    [Fact]
    public async Task ExecuteAsyncFiltersRequestedPackages()
    {
        StubResoniteSceneBuilder sceneBuilder = new();
        PlateauImportService service = CreateService(sceneBuilder);
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
        PlateauImportService service = CreateService(sceneBuilder);
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
            "cached-archive",
            "13100_tokyo23-ku_2022_citygml_1_2_op");
        CreateRuntimePackageFixture(
            datasetRoot,
            "bldg",
            "http://www.opengis.net/citygml/building/2.0",
            "Building",
            "Nested Source Building");

        StubResoniteSceneBuilder sceneBuilder = new();
        PlateauImportService service = CreateService(sceneBuilder);

        ImportExecutionResult result = await service.ExecuteAsync(
            new PlateauImportRequest(
                Dataset: "tokyo23ku",
                MeshCode: "53394525",
                SourceKind: DatasetSourceKind.Local,
                LocalSourcePath: Path.Combine(sourceRoot.Path, "cache", "remote", "tokyo23ku", "cached-archive"),
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
        PlateauImportService service = CreateService(sceneBuilder);

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
        PlateauImportService service = CreateService(sceneBuilder);

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
        PlateauImportService service = CreateService(sceneBuilder);

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
    public async Task ExecuteAsyncSubdividesLowLodTransportationBeforeTerrainAlignment()
    {
        using TemporaryDirectory datasetRoot = new();
        CreateRuntimeSegmentedTerrainAlignmentFixture(datasetRoot.Path);

        StubResoniteSceneBuilder sceneBuilder = new();
        PlateauImportService service = CreateService(sceneBuilder);

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
            static cityObject => cityObject.DisplayName == "Segmented Aligned Road");
        Assert.Equal("tran", road.PackageName);
        Assert.True(road.Mesh.Vertices.Count > 6);
        Assert.Contains(road.Mesh.Vertices, static vertex => vertex.Position.Y >= -0.001 && vertex.Position.Y <= 0.001);
        Assert.Contains(road.Mesh.Vertices, static vertex => vertex.Position.Y >= 19.9 && vertex.Position.Y <= 20.1);
        Assert.Contains(road.Mesh.Vertices, static vertex => vertex.Position.Y >= 9.0 && vertex.Position.Y <= 11.0);

        ResoniteMaterialBinding roadMaterial = Assert.Single(road.Materials);
        Assert.Equal("udx/tran/53394525/appearance/segment.png", roadMaterial.TexturePath);
        Assert.Equal(LocalCityGmlResonitePlanBuilder.DefaultTerrainAlignedMaterialDepthOffset, roadMaterial.DepthOffset);
    }

    [Fact]
    public async Task ExecuteAsyncSubdividesLowLodSquareTransportationBeforeTerrainAlignment()
    {
        using TemporaryDirectory datasetRoot = new();
        CreateRuntimeSegmentedSquareTerrainAlignmentFixture(datasetRoot.Path);

        StubResoniteSceneBuilder sceneBuilder = new();
        PlateauImportService service = CreateService(sceneBuilder);

        ImportExecutionResult result = await service.ExecuteAsync(
            new PlateauImportRequest(
                Dataset: "tokyo23ku",
                MeshCode: "53394525",
                SourceKind: DatasetSourceKind.Local,
                LocalSourcePath: datasetRoot.Path,
                ServerUri: null),
            workRoot: "runtime/resonite");
        CapturedResoniteScene scene = result.Metadata.ToScene(sceneBuilder.CityObjects);

        ResoniteConstructionCityObject square = Assert.Single(
            scene.CityObjects,
            static cityObject => cityObject.DisplayName == "Segmented Aligned Square");
        Assert.Equal("squr", square.PackageName);
        Assert.True(square.Mesh.Vertices.Count > 6);
        Assert.Contains(square.Mesh.Vertices, static vertex => vertex.Position.Y >= -0.001 && vertex.Position.Y <= 0.001);
        Assert.Contains(square.Mesh.Vertices, static vertex => vertex.Position.Y >= 19.9 && vertex.Position.Y <= 20.1);
        Assert.Contains(square.Mesh.Vertices, static vertex => vertex.Position.Y >= 9.0 && vertex.Position.Y <= 11.0);

        ResoniteMaterialBinding squareMaterial = Assert.Single(square.Materials);
        Assert.Equal("udx/squr/53394525/appearance/segment-square.png", squareMaterial.TexturePath);
        Assert.Equal(LocalCityGmlResonitePlanBuilder.DefaultTerrainAlignedMaterialDepthOffset, squareMaterial.DepthOffset);
    }

    [Fact]
    public async Task ExecuteAsyncUsesWidthAwareSubdivisionDensityForLowLodTransportation()
    {
        using TemporaryDirectory datasetRoot = new();
        CreateRuntimeSegmentDensityComparisonFixture(datasetRoot.Path);

        StubResoniteSceneBuilder sceneBuilder = new();
        PlateauImportService service = CreateService(sceneBuilder);

        ImportExecutionResult result = await service.ExecuteAsync(
            new PlateauImportRequest(
                Dataset: "tokyo23ku",
                MeshCode: "53394525",
                SourceKind: DatasetSourceKind.Local,
                LocalSourcePath: datasetRoot.Path,
                ServerUri: null),
            workRoot: "runtime/resonite");
        CapturedResoniteScene scene = result.Metadata.ToScene(sceneBuilder.CityObjects);

        ResoniteConstructionCityObject narrowRoad = Assert.Single(
            scene.CityObjects,
            static cityObject => cityObject.DisplayName == "Narrow Segmented Road");
        ResoniteConstructionCityObject wideRoad = Assert.Single(
            scene.CityObjects,
            static cityObject => cityObject.DisplayName == "Wide Segmented Road");

        Assert.True(
            narrowRoad.Mesh.Vertices.Count > wideRoad.Mesh.Vertices.Count,
            $"Expected narrower road to be subdivided more densely. narrow={narrowRoad.Mesh.Vertices.Count}, wide={wideRoad.Mesh.Vertices.Count}");
    }

    [Fact]
    public async Task ExecuteAsyncDensifiesSkewedLowLodRoadUsingContourStyleSubdivision()
    {
        using TemporaryDirectory datasetRoot = new();
        CreateRuntimeSkewedTransportationFixture(datasetRoot.Path);

        StubResoniteSceneBuilder sceneBuilder = new();
        PlateauImportService service = CreateService(sceneBuilder);

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
            static cityObject => cityObject.DisplayName == "Skewed Segmented Road");
        Assert.True(
            road.Mesh.Vertices.Count > 6,
            $"Expected skewed road to be densified before tessellation. vertices={road.Mesh.Vertices.Count}");

        double minHeight = road.Mesh.Vertices.Min(static vertex => vertex.Position.Y);
        double maxHeight = road.Mesh.Vertices.Max(static vertex => vertex.Position.Y);
        Assert.InRange(minHeight, -0.1, 0.1);
        Assert.True(maxHeight >= 12.0, $"Expected skewed road to retain multiple sampled elevations. maxHeight={maxHeight:F3}");
    }

    [Fact]
    public async Task ExecuteAsyncAvoidsLargeLongitudinalHeightStepsOnNarrowLowLodRoad()
    {
        using TemporaryDirectory datasetRoot = new();
        CreateRuntimeSegmentDensityComparisonFixture(datasetRoot.Path);

        StubResoniteSceneBuilder sceneBuilder = new();
        PlateauImportService service = CreateService(sceneBuilder);

        ImportExecutionResult result = await service.ExecuteAsync(
            new PlateauImportRequest(
                Dataset: "tokyo23ku",
                MeshCode: "53394525",
                SourceKind: DatasetSourceKind.Local,
                LocalSourcePath: datasetRoot.Path,
                ServerUri: null),
            workRoot: "runtime/resonite");
        CapturedResoniteScene scene = result.Metadata.ToScene(sceneBuilder.CityObjects);

        ResoniteConstructionCityObject narrowRoad = Assert.Single(
            scene.CityObjects,
            static cityObject => cityObject.DisplayName == "Narrow Segmented Road");
        double[] heights = narrowRoad.Mesh.Vertices
            .Select(static vertex => Math.Round(vertex.Position.Y, 3))
            .Distinct()
            .OrderBy(static height => height)
            .ToArray();
        Assert.True(heights.Length >= 6, $"Expected multiple height samples along the road, but got {heights.Length}.");

        double maxStep = 0.0;
        for (int index = 1; index < heights.Length; index++)
        {
            maxStep = Math.Max(maxStep, heights[index] - heights[index - 1]);
        }

        Assert.True(maxStep <= 3.5, $"Detected a large longitudinal height step: {maxStep:F3}m");
    }

    [Fact]
    public async Task ExecuteAsyncKeepsLowLodRoadVerticesWithinDemHeightEnvelope()
    {
        using TemporaryDirectory datasetRoot = new();
        CreateRuntimeSegmentDensityComparisonFixture(datasetRoot.Path);

        StubResoniteSceneBuilder sceneBuilder = new();
        PlateauImportService service = CreateService(sceneBuilder);

        ImportExecutionResult result = await service.ExecuteAsync(
            new PlateauImportRequest(
                Dataset: "tokyo23ku",
                MeshCode: "53394525",
                SourceKind: DatasetSourceKind.Local,
                LocalSourcePath: datasetRoot.Path,
                ServerUri: null),
            workRoot: "runtime/resonite");
        CapturedResoniteScene scene = result.Metadata.ToScene(sceneBuilder.CityObjects);

        ResoniteConstructionCityObject narrowRoad = Assert.Single(
            scene.CityObjects,
            static cityObject => cityObject.DisplayName == "Narrow Segmented Road");
        ResoniteConstructionCityObject wideRoad = Assert.Single(
            scene.CityObjects,
            static cityObject => cityObject.DisplayName == "Wide Segmented Road");

        Assert.All(
            narrowRoad.Mesh.Vertices,
            static vertex => Assert.InRange(vertex.Position.Y, -0.1, 20.1));
        Assert.All(
            wideRoad.Mesh.Vertices,
            static vertex => Assert.InRange(vertex.Position.Y, -0.1, 20.1));
    }

    [Fact]
    public async Task ExecuteAsyncInterpolatesUnsampledRoadVerticesWhenDemCoverageIsPartial()
    {
        using TemporaryDirectory datasetRoot = new();
        CreateRuntimePartialDemCoverageRoadFixture(datasetRoot.Path);

        StubResoniteSceneBuilder sceneBuilder = new();
        PlateauImportService service = CreateService(sceneBuilder);

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
            static cityObject => cityObject.DisplayName == "Partial Coverage Road");
        double maxHeight = road.Mesh.Vertices.Max(static vertex => vertex.Position.Y);
        double minHeight = road.Mesh.Vertices.Min(static vertex => vertex.Position.Y);
        Assert.InRange(minHeight, -0.1, 1.0);
        Assert.InRange(maxHeight, -0.1, 20.1);
    }

    [Fact]
    public async Task ExecuteAsyncGeneratesUvProjectedFallbackMaterialForTexturelessTransportation()
    {
        using TemporaryDirectory datasetRoot = new();
        CreateRuntimeTexturelessTransportationFixture(datasetRoot.Path);

        StubResoniteSceneBuilder sceneBuilder = new();
        PlateauImportService service = CreateService(sceneBuilder);

        ImportExecutionResult result = await service.ExecuteAsync(
            new PlateauImportRequest(
                Dataset: "tokyo23ku",
                MeshCode: "53394525",
                SourceKind: DatasetSourceKind.Local,
                LocalSourcePath: datasetRoot.Path,
                ServerUri: null),
            workRoot: "runtime/resonite");
        CapturedResoniteScene scene = result.Metadata.ToScene(sceneBuilder.CityObjects);

        Assert.Equal(2, scene.CityObjects.Count);
        ResoniteConstructionCityObject road = Assert.Single(
            scene.CityObjects,
            static cityObject => cityObject.DisplayName == "Generated UV Road");
        Assert.Single(road.Materials);
        Assert.Equal("tran", road.PackageName);

        ResoniteMaterialBinding roadMaterial = Assert.Single(
            road.Materials,
            static material => material.MaterialType == ResoniteMaterialType.Standard);
        Assert.Equal(ResoniteTextureSourceKind.Bundled, roadMaterial.TextureSourceKind);
        Assert.Equal(ResoniteMaterialProjection.Uv, roadMaterial.Projection);
        double roadUSpan = road.Mesh.Vertices.Max(static vertex => vertex.UV0.X)
            - road.Mesh.Vertices.Min(static vertex => vertex.UV0.X);
        double roadVSpan = road.Mesh.Vertices.Max(static vertex => vertex.UV0.Y)
            - road.Mesh.Vertices.Min(static vertex => vertex.UV0.Y);
        Assert.True(roadUSpan > roadVSpan);

        ResoniteConstructionCityObject marking = Assert.Single(
            scene.CityObjects,
            static cityObject => cityObject.DisplayName == "Generated UV Road Marking");
        Assert.False(marking.CollisionEnabled);
        ResoniteMaterialBinding markingMaterial = Assert.Single(marking.Materials);
        Assert.Equal(ResoniteMaterialType.VertexColor, markingMaterial.MaterialType);
        Assert.Null(markingMaterial.TexturePath);
        Assert.Equal(ResoniteMaterialProjection.Uv, markingMaterial.Projection);
        Assert.Equal(LocalCityGmlResonitePlanBuilder.DefaultTerrainAlignedMaterialDepthOffset, markingMaterial.DepthOffset);

        int markingSubmeshIndex = Assert.Single(markingMaterial.SubmeshIndices);
        ResoniteMeshSubmesh markingSubmesh = Assert.Single(
            marking.Mesh.Submeshes,
            submesh => submesh.Index == markingSubmeshIndex);
        Assert.All(
            markingSubmesh.TriangleVertexIndices,
            index =>
            {
                Assert.NotNull(marking.Mesh.Vertices[index].Color);
                Assert.Equal(1.0, marking.Mesh.Vertices[index].Color!.R, 6);
            });
    }

    [Fact]
    public async Task ExecuteAsyncGeneratesPathAlignedUvFallbackMaterialForTexturelessWaterway()
    {
        using TemporaryDirectory datasetRoot = new();
        CreateRuntimeTexturelessWaterwayFixture(datasetRoot.Path);

        StubResoniteSceneBuilder sceneBuilder = new();
        PlateauImportService service = CreateService(sceneBuilder);

        ImportExecutionResult result = await service.ExecuteAsync(
            new PlateauImportRequest(
                Dataset: "tokyo23ku",
                MeshCode: "53394525",
                SourceKind: DatasetSourceKind.Local,
                LocalSourcePath: datasetRoot.Path,
                ServerUri: null),
            workRoot: "runtime/resonite");
        CapturedResoniteScene scene = result.Metadata.ToScene(sceneBuilder.CityObjects);

        ResoniteConstructionCityObject waterway = Assert.Single(
            scene.CityObjects,
            static cityObject => cityObject.DisplayName == "Generated UV Waterway");
        ResoniteMaterialBinding waterwayMaterial = Assert.Single(waterway.Materials);
        Assert.Equal(ResoniteMaterialProjection.Uv, waterwayMaterial.Projection);
        double waterwayUSpan = waterway.Mesh.Vertices.Max(static vertex => vertex.UV0.X)
            - waterway.Mesh.Vertices.Min(static vertex => vertex.UV0.X);
        double waterwayVSpan = waterway.Mesh.Vertices.Max(static vertex => vertex.UV0.Y)
            - waterway.Mesh.Vertices.Min(static vertex => vertex.UV0.Y);
        Assert.True(waterwayUSpan > waterwayVSpan);
    }

    [Fact]
    public async Task ExecuteAsyncSegmentsGeneratedRoadMarkingsAlongRoadDirectionOnSlopedTransportation()
    {
        using TemporaryDirectory datasetRoot = new();
        CreateRuntimeSlopedTransportationFixture(datasetRoot.Path);

        StubResoniteSceneBuilder sceneBuilder = new();
        PlateauImportService service = CreateService(sceneBuilder);

        ImportExecutionResult result = await service.ExecuteAsync(
            new PlateauImportRequest(
                Dataset: "tokyo23ku",
                MeshCode: "53394525",
                SourceKind: DatasetSourceKind.Local,
                LocalSourcePath: datasetRoot.Path,
                ServerUri: null),
            workRoot: "runtime/resonite");
        CapturedResoniteScene scene = result.Metadata.ToScene(sceneBuilder.CityObjects);

        ResoniteConstructionCityObject marking = Assert.Single(
            scene.CityObjects,
            static cityObject => cityObject.DisplayName == "Segmented Slope Road Marking");
        Assert.False(marking.CollisionEnabled);
        Assert.True(marking.Mesh.Vertices.Count > 6);
        Assert.All(
            marking.Mesh.Vertices,
            static vertex => Assert.InRange(vertex.Position.Y, -0.001, 4.001));
    }

    [Fact]
    public async Task ExecuteAsyncIncludesParentMeshPackageFilesForEightDigitMeshCodes()
    {
        StubResoniteSceneBuilder sceneBuilder = new();
        PlateauImportService service = CreateService(sceneBuilder);
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

        Assert.Equal(5, scene.CityObjects.Count);
        Assert.DoesNotContain(
            scene.CityObjects,
            static cityObject => cityObject.DisplayName == "Outside Road");
        Assert.Contains(
            scene.CityObjects,
            static cityObject => cityObject.PackageName == "tran" && cityObject.DisplayName == "Parent Tile Road");
        Assert.Contains(
            scene.CityObjects,
            static cityObject => cityObject.PackageName == "tran" && cityObject.DisplayName == "Parent Tile Road Marking");
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
        PlateauImportService service = CreateService(sceneBuilder);

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
        PlateauImportService service = CreateService(sceneBuilder);

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
    public async Task ExecuteAsyncKeepsSplitDemHeightMapBoundarySamplesAlignedAcrossChunks()
    {
        using TemporaryDirectory datasetRoot = new();
        CreateRuntimeSplitBoundaryDemFixture(datasetRoot.Path);
        StubResoniteSceneBuilder sceneBuilder = new();
        PlateauImportService service = CreateService(sceneBuilder);

        ImportExecutionResult heightMapResult = await service.ExecuteAsync(
            new PlateauImportRequest(
                Dataset: "tokyo23ku",
                MeshCode: "53394525",
                SourceKind: DatasetSourceKind.Local,
                LocalSourcePath: datasetRoot.Path,
                ServerUri: null,
                PackageNames: ["dem"],
                DemTerrainMode: DemTerrainMode.HeightMap,
                DemHeightmapMetersPerVertex: 10.0,
                DemHeightmapMaxResolution: 64),
            workRoot: "runtime/resonite-heightmap");
        CapturedResoniteScene heightMapScene = heightMapResult.Metadata.ToScene(sceneBuilder.CityObjects.ToArray());

        ResoniteConstructionCityObject[] demChunks = heightMapScene.CityObjects
            .Where(static cityObject => string.Equals(cityObject.PackageName, "dem", StringComparison.Ordinal))
            .OrderBy(static cityObject => cityObject.DisplayName, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(2, demChunks.Length);
        ResoniteHeightMapGridGeometry westGeometry = Assert.IsType<ResoniteHeightMapGridGeometry>(demChunks[0].Geometry);
        ResoniteHeightMapGridGeometry eastGeometry = Assert.IsType<ResoniteHeightMapGridGeometry>(demChunks[1].Geometry);

        Assert.Equal(westGeometry.Height, eastGeometry.Height);
        Assert.True(
            Math.Abs(demChunks[0].Transform.Position.X - demChunks[1].Transform.Position.X) > 1e-6
            || Math.Abs(demChunks[0].Transform.Position.Z - demChunks[1].Transform.Position.Z) > 1e-6,
            "Split DEM heightmap chunks must not collapse onto the same slot X/Z.");
    }

    [Fact]
    public async Task ExecuteAsyncTilesSplitDemHeightMapChunkBoundsWithoutHorizontalOverlapOrGap()
    {
        using TemporaryDirectory datasetRoot = new();
        CreateRuntimeStraddledSplitBoundaryDemFixture(datasetRoot.Path);
        StubResoniteSceneBuilder sceneBuilder = new();
        PlateauImportService service = CreateService(sceneBuilder);

        ImportExecutionResult heightMapResult = await service.ExecuteAsync(
            new PlateauImportRequest(
                Dataset: "tokyo23ku",
                MeshCode: "53394525",
                SourceKind: DatasetSourceKind.Local,
                LocalSourcePath: datasetRoot.Path,
                ServerUri: null,
                PackageNames: ["dem"],
                DemTerrainMode: DemTerrainMode.HeightMap,
                DemHeightmapMetersPerVertex: 10.0,
                DemHeightmapMaxResolution: 64),
            workRoot: "runtime/resonite-heightmap");
        CapturedResoniteScene heightMapScene = heightMapResult.Metadata.ToScene(sceneBuilder.CityObjects.ToArray());

        ResoniteConstructionCityObject[] demChunks = heightMapScene.CityObjects
            .Where(static cityObject => string.Equals(cityObject.PackageName, "dem", StringComparison.Ordinal))
            .OrderBy(static cityObject => cityObject.DisplayName, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(2, demChunks.Length);
        ResoniteHeightMapGridGeometry westGeometry = Assert.IsType<ResoniteHeightMapGridGeometry>(demChunks[0].Geometry);
        ResoniteHeightMapGridGeometry eastGeometry = Assert.IsType<ResoniteHeightMapGridGeometry>(demChunks[1].Geometry);
        double westMaxX = demChunks[0].Transform.Position.X + (westGeometry.Size.X / 2.0);
        double eastMinX = demChunks[1].Transform.Position.X - (eastGeometry.Size.X / 2.0);
        Assert.True(
            Math.Abs(westMaxX - eastMinX) <= 1e-3,
            $"Split DEM heightmap chunks must tile on the overlay boundary without X overlap/gap. westMaxX={westMaxX:F6}, eastMinX={eastMinX:F6}, delta={westMaxX - eastMinX:F6}");
    }

    [Fact]
    public async Task ExecuteAsyncUsesDifferentGeneratedDemTexturePathsForDifferentRequestedMeshSelectors()
    {
        string firstTexturePath = CreateGeneratedDemTexturePathForMeshCode("53394525");
        string secondTexturePath = CreateGeneratedDemTexturePathForMeshCode("53394526");

        Assert.StartsWith(LocalCityGmlResonitePlanBuilder.DefaultDemTerrainTexturePath, firstTexturePath, StringComparison.Ordinal);
        Assert.StartsWith(LocalCityGmlResonitePlanBuilder.DefaultDemTerrainTexturePath, secondTexturePath, StringComparison.Ordinal);
        Assert.NotEqual(firstTexturePath, secondTexturePath);
    }

    [Fact]
    public async Task ExecuteAsyncUsesIndependentGeneratedDemTexturesForSplitHeightMapChunks()
    {
        using TemporaryDirectory datasetRoot = new();
        CreateRuntimeStraddledSplitBoundaryDemFixture(datasetRoot.Path);
        StubResoniteSceneBuilder sceneBuilder = new();
        PlateauImportService service = CreateService(sceneBuilder);

        ImportExecutionResult heightMapResult = await service.ExecuteAsync(
            new PlateauImportRequest(
                Dataset: "tokyo23ku",
                MeshCode: "53394525",
                SourceKind: DatasetSourceKind.Local,
                LocalSourcePath: datasetRoot.Path,
                ServerUri: null,
                PackageNames: ["dem"],
                DemTerrainMode: DemTerrainMode.HeightMap,
                DemHeightmapMetersPerVertex: 10.0,
                DemHeightmapMaxResolution: 64),
            workRoot: "runtime/resonite-heightmap");
        CapturedResoniteScene heightMapScene = heightMapResult.Metadata.ToScene(sceneBuilder.CityObjects.ToArray());

        ResoniteConstructionCityObject[] demChunks = heightMapScene.CityObjects
            .Where(static cityObject => string.Equals(cityObject.PackageName, "dem", StringComparison.Ordinal))
            .OrderBy(static cityObject => cityObject.DisplayName, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(2, demChunks.Length);

        ResoniteMaterialBinding westMaterial = Assert.Single(
            demChunks[0].Materials,
            static material => material.TexturePath is not null
                && material.TexturePath.StartsWith(
                    LocalCityGmlResonitePlanBuilder.DefaultDemTerrainTexturePath,
                    StringComparison.Ordinal));
        ResoniteMaterialBinding eastMaterial = Assert.Single(
            demChunks[1].Materials,
            static material => material.TexturePath is not null
                && material.TexturePath.StartsWith(
                    LocalCityGmlResonitePlanBuilder.DefaultDemTerrainTexturePath,
                    StringComparison.Ordinal));

        Assert.NotNull(westMaterial.TextureScale);
        Assert.NotNull(westMaterial.TextureOffset);
        Assert.NotNull(eastMaterial.TextureScale);
        Assert.NotNull(eastMaterial.TextureOffset);
        Assert.NotEqual(westMaterial.TexturePath, eastMaterial.TexturePath);
        Assert.Equal(1.0, westMaterial.TextureScale!.X, 6);
        Assert.Equal(1.0, westMaterial.TextureScale.Y, 6);
        Assert.Equal(0.0, westMaterial.TextureOffset!.X, 6);
        Assert.Equal(0.0, westMaterial.TextureOffset.Y, 6);
        Assert.Equal(1.0, eastMaterial.TextureScale!.X, 6);
        Assert.Equal(1.0, eastMaterial.TextureScale.Y, 6);
        Assert.Equal(0.0, eastMaterial.TextureOffset!.X, 6);
        Assert.Equal(0.0, eastMaterial.TextureOffset.Y, 6);
    }

    [Fact]
    public async Task ExecuteAsyncFallsBackToSeaLevelOutsideDemTrianglesInHeightMapMode()
    {
        using TemporaryDirectory datasetRoot = new();
        CreateRuntimeSingleTriangleDemFixture(datasetRoot.Path);
        StubResoniteSceneBuilder sceneBuilder = new();
        PlateauImportService service = CreateService(sceneBuilder);

        ImportExecutionResult heightMapResult = await service.ExecuteAsync(
            new PlateauImportRequest(
                Dataset: "tokyo23ku",
                MeshCode: "53394525",
                SourceKind: DatasetSourceKind.Local,
                LocalSourcePath: datasetRoot.Path,
                ServerUri: null,
                PackageNames: ["dem"],
                DemTerrainMode: DemTerrainMode.HeightMap,
                DemHeightmapMetersPerVertex: 10.0,
                DemHeightmapMaxResolution: 64),
            workRoot: "runtime/resonite-heightmap");
        CapturedResoniteScene heightMapScene = heightMapResult.Metadata.ToScene(sceneBuilder.CityObjects.ToArray());

        ResoniteConstructionCityObject demChunk = Assert.Single(
            heightMapScene.CityObjects,
            static cityObject => string.Equals(cityObject.PackageName, "dem", StringComparison.Ordinal));
        ResoniteHeightMapGridGeometry geometry = Assert.IsType<ResoniteHeightMapGridGeometry>(demChunk.Geometry);

        double minX = demChunk.Transform.Position.X - (geometry.Size.X / 2.0);
        double minZ = demChunk.Transform.Position.Z - (geometry.Size.Y / 2.0);
        double outsideHeight0 = GetNearestHeightMapWorldHeight(
            demChunk,
            geometry,
            minX + (geometry.Size.X * 0.10),
            minZ + (geometry.Size.Y * 0.10));
        double outsideHeight1 = GetNearestHeightMapWorldHeight(
            demChunk,
            geometry,
            minX + (geometry.Size.X * 0.20),
            minZ + (geometry.Size.Y * 0.20));
        double insideHeight = GetNearestHeightMapWorldHeight(
            demChunk,
            geometry,
            minX + (geometry.Size.X * 0.90),
            minZ + (geometry.Size.Y * 0.90));

        Assert.InRange(Math.Abs(outsideHeight0 - outsideHeight1), 0.0, 1e-6);
        Assert.True(
            outsideHeight0 < insideHeight,
            $"Expected heightmap samples outside the DEM triangle to fall below the interior terrain surface. outside={outsideHeight0:F6}, inside={insideHeight:F6}");
    }

    [Fact]
    public void ExtendBoundaryConnectedMissingHeightMapBandsPreservesSeaLevelForBoundaryConnectedRuns()
    {
        double[] localHeights =
        [
            0.0, 0.0, 10.0, 20.0, 30.0,
            0.0, 0.0, 12.0, 22.0, 32.0,
            0.0, 0.0, 14.0, 24.0, 34.0,
            0.0, 0.0, 16.0, 26.0, 36.0,
            0.0, 0.0, 18.0, 28.0, 38.0,
        ];
        bool[] sampledInsideTriangles =
        [
            false, false, true, true, true,
            false, false, true, true, true,
            false, false, true, true, true,
            false, false, true, true, true,
            false, false, true, true, true,
        ];

        ExtendBoundaryConnectedMissingHeightMapBands(localHeights, sampledInsideTriangles, width: 5, height: 5);

        Assert.Equal(0.0, localHeights[0]);
        Assert.Equal(0.0, localHeights[5]);
        Assert.Equal(0.0, localHeights[10]);
        Assert.Equal(0.0, localHeights[15]);
        Assert.Equal(0.0, localHeights[20]);
        Assert.Equal(0.0, localHeights[6]);
        Assert.Equal(0.0, localHeights[11]);
        Assert.Equal(0.0, localHeights[16]);
    }

    [Fact]
    public void ExtendBoundaryConnectedMissingHeightMapBandsPreservesInteriorHoles()
    {
        double[] localHeights =
        [
            10.0, 11.0, 12.0, 13.0,
            20.0, 0.0, 0.0, 23.0,
            30.0, 0.0, 0.0, 33.0,
            40.0, 41.0, 42.0, 43.0,
        ];
        bool[] sampledInsideTriangles =
        [
            true, true, true, true,
            true, false, false, true,
            true, false, false, true,
            true, true, true, true,
        ];

        ExtendBoundaryConnectedMissingHeightMapBands(localHeights, sampledInsideTriangles, width: 4, height: 4);

        Assert.Equal(0.0, localHeights[5]);
        Assert.Equal(0.0, localHeights[6]);
        Assert.Equal(0.0, localHeights[9]);
        Assert.Equal(0.0, localHeights[10]);
    }

    [Fact]
    public void ExtendBoundaryConnectedMissingHeightMapBandsDoesNotPropagateSingleHighEdgeSampleAcrossEmptyRows()
    {
        double[] localHeights =
        [
            0.0, 0.0, 100.0, 100.0,
            0.0, 0.0, 10.0, 10.0,
            0.0, 0.0, 0.0, 0.0,
            0.0, 0.0, 0.0, 0.0,
        ];
        bool[] sampledInsideTriangles =
        [
            false, false, true, true,
            false, false, true, true,
            false, false, false, false,
            false, false, false, false,
        ];

        ExtendBoundaryConnectedMissingHeightMapBands(localHeights, sampledInsideTriangles, width: 4, height: 4);

        Assert.Equal(100.0, localHeights[0]);
        Assert.Equal(10.0, localHeights[4]);
        Assert.Equal(10.0, localHeights[8]);
        Assert.Equal(10.0, localHeights[12]);
    }

    [Fact]
    public void ExtendBoundaryConnectedMissingHeightMapBandsDoesNotLiftSeaLevelBoundaryTowardHighInteriorSamples()
    {
        double[] localHeights =
        [
            0.0, 0.0, 100.0, 100.0, 100.0,
            0.0, 0.0, 10.0, 10.0, 10.0,
            0.0, 0.0, 10.0, 10.0, 10.0,
            0.0, 0.0, 10.0, 10.0, 10.0,
            0.0, 0.0, 10.0, 10.0, 10.0,
        ];
        bool[] sampledInsideTriangles =
        [
            false, false, true, true, true,
            false, false, true, true, true,
            false, false, true, true, true,
            false, false, true, true, true,
            false, false, true, true, true,
        ];

        ExtendBoundaryConnectedMissingHeightMapBands(localHeights, sampledInsideTriangles, width: 5, height: 5);

        Assert.Equal(0.0, localHeights[0]);
        Assert.Equal(0.0, localHeights[5]);
        Assert.Equal(0.0, localHeights[10]);
        Assert.Equal(0.0, localHeights[15]);
        Assert.Equal(0.0, localHeights[20]);
        Assert.Equal(0.0, localHeights[6]);
        Assert.Equal(0.0, localHeights[11]);
        Assert.Equal(0.0, localHeights[16]);
    }

    [Fact]
    public async Task ExecuteAsyncKeepsSplitDemHeightMapBoundaryHeightsAlignedAcrossChunks()
    {
        using TemporaryDirectory datasetRoot = new();
        CreateRuntimeSplitBoundaryDemFixture(datasetRoot.Path);
        StubResoniteSceneBuilder sceneBuilder = new();
        PlateauImportService service = CreateService(sceneBuilder);

        ImportExecutionResult heightMapResult = await service.ExecuteAsync(
            new PlateauImportRequest(
                Dataset: "tokyo23ku",
                MeshCode: "53394525",
                SourceKind: DatasetSourceKind.Local,
                LocalSourcePath: datasetRoot.Path,
                ServerUri: null,
                PackageNames: ["dem"],
                DemTerrainMode: DemTerrainMode.HeightMap,
                DemHeightmapMetersPerVertex: 10.0,
                DemHeightmapMaxResolution: 64),
            workRoot: "runtime/resonite-heightmap");
        CapturedResoniteScene heightMapScene = heightMapResult.Metadata.ToScene(sceneBuilder.CityObjects.ToArray());

        ResoniteConstructionCityObject[] demChunks = heightMapScene.CityObjects
            .Where(static cityObject => string.Equals(cityObject.PackageName, "dem", StringComparison.Ordinal))
            .OrderBy(static cityObject => cityObject.DisplayName, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(2, demChunks.Length);
        ResoniteHeightMapGridGeometry westGeometry = Assert.IsType<ResoniteHeightMapGridGeometry>(demChunks[0].Geometry);
        ResoniteHeightMapGridGeometry eastGeometry = Assert.IsType<ResoniteHeightMapGridGeometry>(demChunks[1].Geometry);
        Assert.Equal(westGeometry.Height, eastGeometry.Height);

        const double tolerance = 1e-9;
        for (int row = 0; row < westGeometry.Height; row++)
        {
            double westWorldHeight = demChunks[0].Transform.Position.Y
                - westGeometry.MaxHeight
                + westGeometry.HeightSamples[(row * westGeometry.Width) + (westGeometry.Width - 1)];
            double eastWorldHeight = demChunks[1].Transform.Position.Y
                - eastGeometry.MaxHeight
                + eastGeometry.HeightSamples[row * eastGeometry.Width];

            Assert.InRange(Math.Abs(westWorldHeight - eastWorldHeight), 0.0, tolerance);
        }
    }

    [Fact]
    public void AlignAdjacentDemHeightMapChunkBoundariesPreservesChunkWorldHeightsWhenBoundaryAveragingRaisesMaximum()
    {
        ResoniteConstructionCityObject leftChunk = CreateHeightMapChunk(
            "left",
            positionX: 0.0,
            positionY: 25.0,
            positionZ: 0.0,
            width: 2,
            height: 2,
            sizeX: 10.0,
            sizeY: 10.0,
            minHeight: 10.0,
            maxHeight: 25.0,
            heightSamples: [10.0, 20.0, 10.0, 25.0]);
        ResoniteConstructionCityObject rightChunk = CreateHeightMapChunk(
            "right",
            positionX: 10.0,
            positionY: 45.0,
            positionZ: 0.0,
            width: 2,
            height: 2,
            sizeX: 10.0,
            sizeY: 10.0,
            minHeight: 30.0,
            maxHeight: 45.0,
            heightSamples: [45.0, 30.0, 35.0, 30.0]);

        ResoniteConstructionCityObject[] alignedChunks = AlignAdjacentDemHeightMapChunkBoundaries(leftChunk, rightChunk);

        ResoniteConstructionCityObject alignedLeftChunk = alignedChunks
            .OrderBy(static chunk => chunk.Transform.Position.X)
            .First();
        ResoniteHeightMapGridGeometry alignedLeftGeometry = Assert.IsType<ResoniteHeightMapGridGeometry>(alignedLeftChunk.Geometry);

        Assert.Equal(32.5, alignedLeftChunk.Transform.Position.Y, 6);
        Assert.Equal(10.0, GetHeightMapWorldHeight(alignedLeftChunk, alignedLeftGeometry, row: 0, column: 0), 6);
        Assert.Equal(32.5, GetHeightMapWorldHeight(alignedLeftChunk, alignedLeftGeometry, row: 0, column: 1), 6);
        Assert.Equal(30.0, GetHeightMapWorldHeight(alignedLeftChunk, alignedLeftGeometry, row: 1, column: 1), 6);
    }

    [Fact]
    public void AlignAdjacentDemHeightMapChunkBoundariesIgnoresSeaLevelFallbackWhenNeighborHasMeasuredBoundaryHeights()
    {
        ResoniteConstructionCityObject leftChunk = CreateHeightMapChunk(
            "left",
            positionX: 0.0,
            positionY: 40.0,
            positionZ: 0.0,
            width: 2,
            height: 2,
            sizeX: 10.0,
            sizeY: 10.0,
            minHeight: 20.0,
            maxHeight: 40.0,
            heightSamples: [30.0, 40.0, 20.0, 30.0]);
        ResoniteConstructionCityObject rightChunk = CreateHeightMapChunk(
            "right",
            positionX: 10.0,
            positionY: 20.0,
            positionZ: 0.0,
            width: 2,
            height: 2,
            sizeX: 10.0,
            sizeY: 10.0,
            minHeight: 0.0,
            maxHeight: 20.0,
            heightSamples: [0.0, 20.0, 0.0, 20.0]);

        ResoniteConstructionCityObject[] alignedChunks = AlignAdjacentDemHeightMapChunkBoundaries(leftChunk, rightChunk);

        ResoniteConstructionCityObject alignedLeftChunk = alignedChunks
            .OrderBy(static chunk => chunk.Transform.Position.X)
            .First();
        ResoniteConstructionCityObject alignedRightChunk = alignedChunks
            .OrderBy(static chunk => chunk.Transform.Position.X)
            .Last();
        ResoniteHeightMapGridGeometry alignedLeftGeometry = Assert.IsType<ResoniteHeightMapGridGeometry>(alignedLeftChunk.Geometry);
        ResoniteHeightMapGridGeometry alignedRightGeometry = Assert.IsType<ResoniteHeightMapGridGeometry>(alignedRightChunk.Geometry);

        Assert.Equal(40.0, GetHeightMapWorldHeight(alignedLeftChunk, alignedLeftGeometry, row: 0, column: 1), 6);
        Assert.Equal(30.0, GetHeightMapWorldHeight(alignedLeftChunk, alignedLeftGeometry, row: 1, column: 1), 6);
        Assert.Equal(40.0, GetHeightMapWorldHeight(alignedRightChunk, alignedRightGeometry, row: 0, column: 0), 6);
        Assert.Equal(30.0, GetHeightMapWorldHeight(alignedRightChunk, alignedRightGeometry, row: 1, column: 0), 6);
    }

    [Fact]
    public void AlignAdjacentDemHeightMapChunkBoundariesReconcilesSharedCornerAcrossTwoByTwoChunks()
    {
        ResoniteConstructionCityObject northWestChunk = CreateHeightMapChunk(
            "north-west",
            positionX: 0.0,
            positionY: 10.0,
            positionZ: -10.0,
            width: 2,
            height: 2,
            sizeX: 10.0,
            sizeY: 10.0,
            minHeight: 0.0,
            maxHeight: 10.0,
            heightSamples: [1.0, 2.0, 3.0, 10.0]);
        ResoniteConstructionCityObject northEastChunk = CreateHeightMapChunk(
            "north-east",
            positionX: 10.0,
            positionY: 20.0,
            positionZ: -10.0,
            width: 2,
            height: 2,
            sizeX: 10.0,
            sizeY: 10.0,
            minHeight: 4.0,
            maxHeight: 20.0,
            heightSamples: [20.0, 5.0, 6.0, 7.0]);
        ResoniteConstructionCityObject southWestChunk = CreateHeightMapChunk(
            "south-west",
            positionX: 0.0,
            positionY: 30.0,
            positionZ: 0.0,
            width: 2,
            height: 2,
            sizeX: 10.0,
            sizeY: 10.0,
            minHeight: 8.0,
            maxHeight: 30.0,
            heightSamples: [8.0, 9.0, 10.0, 30.0]);
        ResoniteConstructionCityObject southEastChunk = CreateHeightMapChunk(
            "south-east",
            positionX: 10.0,
            positionY: 40.0,
            positionZ: 0.0,
            width: 2,
            height: 2,
            sizeX: 10.0,
            sizeY: 10.0,
            minHeight: 11.0,
            maxHeight: 40.0,
            heightSamples: [40.0, 11.0, 12.0, 13.0]);

        ResoniteConstructionCityObject[] alignedChunks = AlignAdjacentDemHeightMapChunkBoundaries(
            northWestChunk,
            northEastChunk,
            southWestChunk,
            southEastChunk);

        double sharedCenterX = 5.0;
        double sharedCenterZ = -5.0;
        double[] sharedCornerHeights = alignedChunks
            .Select(chunk =>
            {
                ResoniteHeightMapGridGeometry geometry = Assert.IsType<ResoniteHeightMapGridGeometry>(chunk.Geometry);
                return GetCornerHeightNearestPoint(chunk, geometry, sharedCenterX, sharedCenterZ);
            })
            .ToArray();

        Assert.All(sharedCornerHeights, height => Assert.Equal(sharedCornerHeights[0], height, 6));
    }

    [Fact]
    public async Task ExecuteAsyncKeepsSplitDemBoundaryVerticesAlignedAcrossChunks()
    {
        using TemporaryDirectory datasetRoot = new();
        CreateRuntimeSplitBoundaryDemFixture(datasetRoot.Path);
        StubResoniteSceneBuilder sceneBuilder = new();
        PlateauImportService service = CreateService(sceneBuilder);

        ImportExecutionResult result = await service.ExecuteAsync(
            new PlateauImportRequest(
                Dataset: "tokyo23ku",
                MeshCode: "53394525",
                SourceKind: DatasetSourceKind.Local,
                LocalSourcePath: datasetRoot.Path,
                ServerUri: null),
            workRoot: "runtime/resonite");
        CapturedResoniteScene scene = result.Metadata.ToScene(sceneBuilder.CityObjects);

        ResoniteConstructionCityObject[] reliefChunks = scene.CityObjects
            .Where(static cityObject => cityObject.PackageName == "dem")
            .OrderBy(static cityObject => cityObject.DisplayName, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(2, reliefChunks.Length);

        ResoniteFloat3[] westBoundaryVertices = CollectWorldVertices(reliefChunks[0])
            .Where(static vertex => Approximately(vertex.UvX, 1.0))
            .Select(static vertex => vertex.Position)
            .OrderBy(static vertex => vertex.Y)
            .ToArray();
        ResoniteFloat3[] eastBoundaryVertices = CollectWorldVertices(reliefChunks[1])
            .Where(static vertex => Approximately(vertex.UvX, 0.0))
            .Select(static vertex => vertex.Position)
            .OrderBy(static vertex => vertex.Y)
            .ToArray();

        Assert.Equal(2, westBoundaryVertices.Length);
        Assert.Equal(2, eastBoundaryVertices.Length);

        const double tolerance = 1e-6;
        for (int index = 0; index < westBoundaryVertices.Length; index++)
        {
            Assert.InRange(Math.Abs(westBoundaryVertices[index].X - eastBoundaryVertices[index].X), 0.0, tolerance);
            Assert.InRange(Math.Abs(westBoundaryVertices[index].Y - eastBoundaryVertices[index].Y), 0.0, tolerance);
            Assert.InRange(Math.Abs(westBoundaryVertices[index].Z - eastBoundaryVertices[index].Z), 0.0, tolerance);
        }
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
        PlateauImportService service = CreateService(sceneBuilder);

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
                Assert.Null(material.TexturePath);
                Assert.Equal(ResoniteTextureSourceKind.Bundled, material.TextureSourceKind);
                Assert.Equal(ResoniteMaterialProjection.Uv, material.Projection);
                Assert.Equal(0.32, material.BaseColor.R, 6);
                Assert.Equal(0.58, material.BaseColor.G, 6);
                Assert.Equal(0.24, material.BaseColor.B, 6);
            });
    }

    [Fact]
    public async Task ExecuteAsyncUsesVertexColorMaterialsForVegetationAndFallsBackToGreenPbsMaterialWithoutDiffuseColor()
    {
        using TemporaryDirectory datasetRoot = new();
        CreateRuntimeVegetationFixture(datasetRoot.Path);

        StubResoniteSceneBuilder sceneBuilder = new();
        PlateauImportService service = CreateService(sceneBuilder);

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
                && material.TexturePath is null
                && material.Projection == ResoniteMaterialProjection.Uv
                && Approximately(material.BaseColor.R, 0.32)
                && Approximately(material.BaseColor.G, 0.58)
                && Approximately(material.BaseColor.B, 0.24));
    }

    [Fact]
    public async Task ExecuteAsyncTriangulatesConcaveLandUseWithoutOverfilling()
    {
        using TemporaryDirectory datasetRoot = new();
        CreateRuntimeComplexLandUseFixture(datasetRoot.Path);

        StubResoniteSceneBuilder sceneBuilder = new();
        PlateauImportService service = CreateService(sceneBuilder);

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
        PlateauImportService service = CreateService(sceneBuilder);

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
        PlateauImportService service = CreateService(sceneBuilder);

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
    public async Task ExecuteAsyncUsesMeshCodeCenterForGeographicLocalOrigin()
    {
        StubResoniteSceneBuilder sceneBuilder = new();
        PlateauImportService service = CreateService(sceneBuilder);
        string fixturePath = TestData.GetFixturePath("LocalPlateauDatasetParentMeshPackages");

        ImportExecutionResult result = await service.ExecuteAsync(
            new PlateauImportRequest(
                Dataset: "tokyo23ku",
                MeshCode: "53394525",
                SourceKind: DatasetSourceKind.Local,
                LocalSourcePath: fixturePath,
                ServerUri: null),
            workRoot: "runtime/resonite");

        Assert.True(Approximately(result.Metadata.LocalOrigin.Latitude, 35.6875));
        Assert.True(Approximately(result.Metadata.LocalOrigin.Longitude, 139.69375));
        Assert.True(Approximately(result.Metadata.LocalOrigin.Altitude, 0.0));
    }

    [Fact]
    public async Task ExecuteAsyncUsesMatchedMeshCodesForRegexLocalOrigin()
    {
        StubResoniteSceneBuilder sceneBuilder = new();
        PlateauImportService service = CreateService(sceneBuilder);
        string fixturePath = TestData.GetFixturePath("LocalPlateauDataset");

        ImportExecutionResult result = await service.ExecuteAsync(
            new PlateauImportRequest(
                Dataset: "tokyo23ku",
                MeshCode: "5339452[5]",
                SourceKind: DatasetSourceKind.Local,
                LocalSourcePath: fixturePath,
                ServerUri: null),
            workRoot: "runtime/resonite");

        Assert.True(Approximately(result.Metadata.LocalOrigin.Latitude, 35.6875));
        Assert.True(Approximately(result.Metadata.LocalOrigin.Longitude, 139.69375));
        Assert.True(Approximately(result.Metadata.LocalOrigin.Altitude, 0.0));
    }

    [Fact]
    public async Task ExecuteAsyncUsesSourceMatchedMeshCodeForParentMeshPackageCityObjects()
    {
        StubResoniteSceneBuilder sceneBuilder = new();
        PlateauImportService service = CreateService(sceneBuilder);
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

        Assert.Contains(
            scene.CityObjects,
            static cityObject =>
                string.Equals(cityObject.PackageName, "dem", StringComparison.OrdinalIgnoreCase)
                && string.Equals(cityObject.ActualMeshCode, "533945", StringComparison.Ordinal)
                && !string.IsNullOrWhiteSpace(cityObject.SourceObjectKey));
        Assert.Contains(
            scene.CityObjects,
            static cityObject =>
                string.Equals(cityObject.PackageName, "tran", StringComparison.OrdinalIgnoreCase)
                && string.Equals(cityObject.ActualMeshCode, "533945", StringComparison.Ordinal)
                && !string.IsNullOrWhiteSpace(cityObject.SourceObjectKey));
        Assert.DoesNotContain(
            scene.CityObjects,
            static cityObject =>
                string.Equals(cityObject.PackageName, "bldg", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(cityObject.ActualMeshCode, "53394525", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExecuteAsyncKeepsResoniteTriangleWindingAlignedWithVertexNormals()
    {
        using TemporaryDirectory datasetRoot = new();
        CreateRuntimeTriangleFixture(datasetRoot.Path);

        StubResoniteSceneBuilder sceneBuilder = new();
        PlateauImportService service = CreateService(sceneBuilder);

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
        PlateauImportService service = CreateService(sceneBuilder);

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
    public async Task ExecuteAsyncGuidesUserToArchiveWhenDatasetRootDirectoryIsPassedAsLocalSourcePath()
    {
        using TemporaryDirectory datasetRoot = new();
        string archivePath = Path.Combine(datasetRoot.Path, "source-archive-123456789abc.zip");
        await File.WriteAllTextAsync(archivePath, string.Empty);

        StubResoniteSceneBuilder sceneBuilder = new();
        PlateauImportService service = CreateService(sceneBuilder);

        PlateauImportValidationException exception = await Assert.ThrowsAsync<PlateauImportValidationException>(() =>
            service.ExecuteAsync(
                new PlateauImportRequest(
                    Dataset: "14100-yokohama-shi",
                    MeshCode: "53391570",
                    SourceKind: DatasetSourceKind.Local,
                    LocalSourcePath: datasetRoot.Path,
                    ServerUri: null),
                workRoot: "runtime/resonite"));

        string message = Assert.Single(exception.Errors);
        Assert.Contains($"mesh code '53391570' in '{datasetRoot.Path}'", message, StringComparison.Ordinal);
        Assert.Contains($"'{archivePath}'", message, StringComparison.Ordinal);
        Assert.Contains("contains udx/<package>/<mesh-code>/", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsyncRejectsUnsupportedPackagePatternWithValidationException()
    {
        StubResoniteSceneBuilder sceneBuilder = new();
        PlateauImportService service = CreateService(sceneBuilder);
        string fixturePath = TestData.GetFixturePath("LocalPlateauDataset");

        PlateauImportValidationException exception = await Assert.ThrowsAsync<PlateauImportValidationException>(() =>
            service.ExecuteAsync(
                new PlateauImportRequest(
                    Dataset: "tokyo23ku",
                    MeshCode: "53394525",
                    SourceKind: DatasetSourceKind.Local,
                    LocalSourcePath: fixturePath,
                    ServerUri: null,
                    PackagePatterns: new Dictionary<string, string>
                    {
                        ["unknown"] = "*Road*",
                    }),
                workRoot: "runtime/resonite"));

        Assert.Contains(
            exception.Errors,
            error => error.Contains("Unsupported package name(s): unknown.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExecuteAsyncRejectsNullStringFieldsWithValidationException()
    {
        StubResoniteSceneBuilder sceneBuilder = new();
        PlateauImportService service = CreateService(sceneBuilder);
        string fixturePath = TestData.GetFixturePath("LocalPlateauDataset");

        PlateauImportValidationException exception = await Assert.ThrowsAsync<PlateauImportValidationException>(() =>
            service.ExecuteAsync(
                new PlateauImportRequest(
                    Dataset: null!,
                    MeshCode: null!,
                    SourceKind: DatasetSourceKind.Local,
                    LocalSourcePath: fixturePath,
                    ServerUri: null),
                workRoot: "runtime/resonite"));

        Assert.Contains("The dataset value is required.", exception.Errors);
        Assert.Contains("The mesh code value is required.", exception.Errors);
    }

    [Fact]
    public async Task ExecuteAsyncRejectsDuplicateNormalizedPackageMapKeysWithValidationException()
    {
        StubResoniteSceneBuilder sceneBuilder = new();
        PlateauImportService service = CreateService(sceneBuilder);
        string fixturePath = TestData.GetFixturePath("LocalPlateauDataset");

        PlateauImportValidationException exception = await Assert.ThrowsAsync<PlateauImportValidationException>(() =>
            service.ExecuteAsync(
                new PlateauImportRequest(
                    Dataset: "tokyo23ku",
                    MeshCode: "53394525",
                    SourceKind: DatasetSourceKind.Local,
                    LocalSourcePath: fixturePath,
                    ServerUri: null,
                    ExcludeLodLevelsByPackage: new Dictionary<string, IReadOnlySet<int>>
                    {
                        ["tran"] = new HashSet<int> { 1 },
                        [" TRAN "] = new HashSet<int> { 2 },
                    },
                    PackagePatterns: new Dictionary<string, string>
                    {
                        ["waterbody"] = "*Water*",
                        ["wtr"] = "*River*",
                    }),
                workRoot: "runtime/resonite"));

        Assert.Contains(
            exception.Errors,
            error => error.Contains(
                "The ExcludeLodLevelsByPackage value contains duplicate package keys after normalization: tran.",
                StringComparison.Ordinal));
        Assert.Contains(
            exception.Errors,
            error => error.Contains(
                "The PackagePatterns value contains duplicate package keys after normalization: wtr.",
                StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExecuteAsyncFallsBackToBundledDefaultTextureWhenTextureFileIsMissing()
    {
        StubResoniteSceneBuilder sceneBuilder = new();
        PlateauImportService service = CreateService(sceneBuilder);
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
        Assert.Equal(
            BundledDefaultMaterialProfiles.GetTilesPerMeter(material.TexturePath!).X,
            material.TextureScale!.X,
            6);
        Assert.Equal(
            BundledDefaultMaterialProfiles.GetTilesPerMeter(material.TexturePath!).Y,
            material.TextureScale!.Y,
            6);
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
        CreateRuntimePackageFixture(datasetRoot.Path, "frn", "http://www.opengis.net/citygml/cityfurniture/2.0", "CityFurniture", "City Furniture One");
        CreateRuntimePackageFixture(datasetRoot.Path, "area", "urn:plateau:test:area", "Area", "Area One");

        StubResoniteSceneBuilder sceneBuilder = new();
        PlateauImportService service = CreateService(sceneBuilder);

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
                Assert.Equal(
                    BundledDefaultMaterialProfiles.GetTilesPerMeter(material.TexturePath!).X,
                    material.TextureScale!.X,
                    6);
                Assert.Equal(
                    BundledDefaultMaterialProfiles.GetTilesPerMeter(material.TexturePath!).Y,
                    material.TextureScale!.Y,
                    6);
            });

        ResoniteConstructionCityObject road = Assert.Single(scene.CityObjects, static cityObject => cityObject.DisplayName == "Road One");
        Assert.All(
            road.Materials,
            static material =>
            {
                Assert.Equal(ResoniteMaterialType.Standard, material.MaterialType);
                Assert.Contains(material.TexturePath!, BundledDefaultMaterialFamilies.RoadVariants);
                Assert.Equal(ResoniteTextureSourceKind.Bundled, material.TextureSourceKind);
                Assert.Equal(ResoniteMaterialProjection.Uv, material.Projection);
            });

        ResoniteConstructionCityObject cityFurniture = Assert.Single(scene.CityObjects, static cityObject => cityObject.DisplayName == "City Furniture One");
        Assert.All(
            cityFurniture.Materials,
            static material =>
            {
                Assert.Equal(ResoniteMaterialType.Standard, material.MaterialType);
                Assert.Contains(material.TexturePath!, BundledDefaultMaterialFamilies.CityFurnitureVariants);
                Assert.Equal(ResoniteTextureSourceKind.Bundled, material.TextureSourceKind);
                Assert.Equal(ResoniteMaterialProjection.Triplanar, material.Projection);
                Assert.Equal(BundledDefaultMaterialFamilies.CityFurniture, material.Family);
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
                Assert.Null(material.Family);
            });
    }

    [Fact]
    public async Task ExecuteAsyncKeepsCityFurnitureFallbackMaterialKeysDistinctFromOtherFallbackPackages()
    {
        using TemporaryDirectory datasetRoot = new();
        CreateRuntimePackageFixture(datasetRoot.Path, "frn", "http://www.opengis.net/citygml/cityfurniture/2.0", "CityFurniture", "City Furniture One");
        CreateRuntimePackageFixture(datasetRoot.Path, "cons", "urn:plateau:test:cons", "OtherConstruction", "Construction One");

        StubResoniteSceneBuilder sceneBuilder = new();
        PlateauImportService service = CreateService(sceneBuilder);

        ImportExecutionResult result = await service.ExecuteAsync(
            new PlateauImportRequest(
                Dataset: "tokyo23ku",
                MeshCode: "53394525",
                SourceKind: DatasetSourceKind.Local,
                LocalSourcePath: datasetRoot.Path,
                ServerUri: null),
            workRoot: "runtime/resonite");
        CapturedResoniteScene scene = result.Metadata.ToScene(sceneBuilder.CityObjects);

        ResoniteMaterialBinding cityFurnitureMaterial = Assert.Single(
            Assert.Single(scene.CityObjects, static cityObject => cityObject.DisplayName == "City Furniture One").Materials);
        ResoniteMaterialBinding otherMaterial = Assert.Single(
            Assert.Single(scene.CityObjects, static cityObject => cityObject.DisplayName == "Construction One").Materials);

        Assert.Contains(cityFurnitureMaterial.TexturePath!, BundledDefaultMaterialFamilies.CityFurnitureVariants);
        Assert.Contains(otherMaterial.TexturePath!, BundledDefaultMaterialFamilies.OtherVariants);
        Assert.Equal(BundledDefaultMaterialFamilies.CityFurniture, cityFurnitureMaterial.Family);
        Assert.Equal(BundledDefaultMaterialFamilies.Other, otherMaterial.Family);
        Assert.NotEqual(cityFurnitureMaterial.MaterialKey, otherMaterial.MaterialKey);
        Assert.Contains("|family:city-furniture|", cityFurnitureMaterial.MaterialKey, StringComparison.Ordinal);
        Assert.Contains("|family:other|", otherMaterial.MaterialKey, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsyncUsesUvForUntexturedBuildingFacadesAndTriplanarForRoofs()
    {
        using TemporaryDirectory datasetRoot = new();
        CreateRuntimeUntexturedBuildingFixture(datasetRoot.Path);

        StubResoniteSceneBuilder sceneBuilder = new();
        PlateauImportService service = CreateService(sceneBuilder);

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
        PlateauImportService service = CreateService(sceneBuilder);

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
        PlateauImportService service = CreateService(sceneBuilder);

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

        int facadeSubmeshIndex = Assert.Single(facadeMaterials[0].SubmeshIndices);
        ResoniteMeshSubmesh facadeSubmesh = Assert.Single(
            building.Mesh.Submeshes,
            submesh => submesh.Index == facadeSubmeshIndex);
        ((double, double, double) Position, (double, double)[] Uvs)[] duplicatePositionUvs = facadeSubmesh.TriangleVertexIndices
            .Select(index => building.Mesh.Vertices[index])
            .GroupBy(
                static vertex => (
                    Round(vertex.Position.X),
                    Round(vertex.Position.Y),
                    Round(vertex.Position.Z)))
            .Select(group => (
                Position: group.Key,
                Uvs: group
                    .Select(static vertex => (Round(vertex.UV0.X), Round(vertex.UV0.Y)))
                    .Distinct()
                    .ToArray()))
            .Where(static entry => entry.Uvs.Length > 1)
            .ToArray();

        Assert.Empty(duplicatePositionUvs);
    }

    [Fact]
    public async Task ExecuteAsyncSelectsBundledVariantDeterministicallyWithinFamily()
    {
        using TemporaryDirectory datasetRoot = new();
        CreateRuntimeUntexturedBuildingFixture(datasetRoot.Path);

        StubResoniteSceneBuilder firstSceneBuilder = new();
        StubResoniteSceneBuilder secondSceneBuilder = new();
        PlateauImportService firstService = CreateService(firstSceneBuilder);
        PlateauImportService secondService = CreateService(secondSceneBuilder);

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
        PlateauImportService service = CreateService(sceneBuilder, datasetSourceResolver: resolver);

        ImportExecutionResult result = await service.ExecuteAsync(
            new PlateauImportRequest(
                Dataset: "tokyo23ku",
                MeshCode: "53394525",
                SourceKind: DatasetSourceKind.Remote,
                LocalSourcePath: null,
                ServerUri: new Uri("https://example.invalid/tokyo23ku-citygml.zip", UriKind.Absolute)),
            workRoot: "runtime/resonite");

        Assert.Equal(DatasetSourceKind.Remote, Assert.Single(resolver.Requests).SourceKind);
        Assert.Equal(DatasetSourceKind.Local, result.Metadata.Request.SourceKind);
        Assert.Equal(fixturePath, result.Metadata.Request.LocalSourcePath);
        Assert.NotNull(sceneBuilder.LastEnsureConnectedRequest);
        Assert.Equal(DatasetSourceKind.Remote, sceneBuilder.LastEnsureConnectedRequest!.SourceKind);
        Assert.Null(sceneBuilder.LastEnsureConnectedRequest.LocalSourcePath);
    }

    [Fact]
    public async Task ExecuteAsyncReportsTimestampFreePhaseLogsThroughProgressReporter()
    {
        StubResoniteSceneBuilder sceneBuilder = new();
        string fixturePath = TestData.GetFixturePath("LocalPlateauDataset");
        List<string> progressMessages = [];
        PlateauImportService service = CreateService(sceneBuilder, progressReporter: progressMessages.Add);

        await service.ExecuteAsync(
            new PlateauImportRequest(
                Dataset: "tokyo23ku",
                MeshCode: "53394525",
                SourceKind: DatasetSourceKind.Local,
                LocalSourcePath: fixturePath,
                ServerUri: null),
            workRoot: "runtime/resonite");

        Assert.Contains(progressMessages, static message => message.StartsWith("[import][debug] Resolved dataset source", StringComparison.Ordinal));
        Assert.Contains(progressMessages, static message => message.StartsWith("[import][debug] Scene builder connection check completed", StringComparison.Ordinal));
        Assert.Contains(progressMessages, static message => message.Contains(" Scanned ", StringComparison.Ordinal));
        Assert.Contains(progressMessages, static message => message.Contains(" Parsed ", StringComparison.Ordinal));
        Assert.Contains(progressMessages, static message => message.StartsWith("[import][debug] Prepared construction source", StringComparison.Ordinal));
        Assert.Contains(progressMessages, static message => string.Equals(message, "[import][info] Starting live scene initialization.", StringComparison.Ordinal));
        Assert.Contains(progressMessages, static message => message.StartsWith("[import][info] Streamed ", StringComparison.Ordinal));
        Assert.Contains(progressMessages, static message => message.StartsWith("[import][debug] Scene builder completion finished", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExecuteAsyncUsesInjectedConstructionSourceFactory()
    {
        StubResoniteSceneBuilder sceneBuilder = new();
        RecordingConstructionSourceFactory constructionSourceFactory = new(CreateStubConstructionSource());
        RecordingDatasetSourceResolver datasetSourceResolver = new(
            new PlateauImportRequest(
                Dataset: "tokyo23ku",
                MeshCode: "53394525",
                SourceKind: DatasetSourceKind.Local,
                LocalSourcePath: "/resolved/source",
                ServerUri: null));
        PlateauImportService service = new(
            sceneBuilder,
            datasetSourceResolver,
            constructionSourceFactory: constructionSourceFactory);

        ImportExecutionResult result = await service.ExecuteAsync(
            new PlateauImportRequest(
                Dataset: " tokyo23ku ",
                MeshCode: " 53394525 ",
                SourceKind: DatasetSourceKind.Remote,
                LocalSourcePath: null,
                ServerUri: new Uri("https://example.invalid/source.zip", UriKind.Absolute)),
            workRoot: "runtime/resonite");

        PlateauImportRequest factoryRequest = Assert.Single(constructionSourceFactory.Requests);
        Assert.Equal("/resolved/source", factoryRequest.LocalSourcePath);
        Assert.Equal("tokyo23ku", factoryRequest.Dataset);
        Assert.Equal("53394525", factoryRequest.MeshCode);
        Assert.Equal("stub://resonite", Assert.Single(result.Destinations));
        Assert.Single(sceneBuilder.CityObjects);
    }

    [Fact]
    public async Task ExecuteAsyncEnsuresSceneBuilderConnectionBeforeConstructionSourceBootstrap()
    {
        List<string> callOrder = [];
        StubResoniteSceneBuilder sceneBuilder = new()
        {
            OnEnsureConnected = () => callOrder.Add("connect"),
            OnBegin = () => callOrder.Add("begin"),
        };

        RecordingConstructionSourceFactory constructionSourceFactory = new(
            CreateStubConstructionSource(),
            onCreate: () => callOrder.Add("create-source"));
        RecordingDatasetSourceResolver datasetSourceResolver = new(
            new PlateauImportRequest(
                Dataset: "tokyo23ku",
                MeshCode: "53394525",
                SourceKind: DatasetSourceKind.Local,
                LocalSourcePath: "/resolved/source",
                ServerUri: null));
        PlateauImportService service = new(
            sceneBuilder,
            datasetSourceResolver,
            constructionSourceFactory: constructionSourceFactory);

        await service.ExecuteAsync(
            new PlateauImportRequest(
                Dataset: "tokyo23ku",
                MeshCode: "53394525",
                SourceKind: DatasetSourceKind.Remote,
                LocalSourcePath: null,
                ServerUri: new Uri("https://example.invalid/source.zip", UriKind.Absolute)),
            workRoot: "runtime/resonite");

        Assert.Equal(["connect", "create-source", "begin"], callOrder);
        Assert.Equal(1, sceneBuilder.EnsureConnectedCallCount);
    }

    [Fact]
    public async Task ExecuteAsyncDisposesSceneBuilderWhenConstructionSourceBootstrapFailsAfterConnect()
    {
        StubResoniteSceneBuilder sceneBuilder = new();
        RecordingDatasetSourceResolver datasetSourceResolver = new(
            new PlateauImportRequest(
                Dataset: "tokyo23ku",
                MeshCode: "53394525",
                SourceKind: DatasetSourceKind.Local,
                LocalSourcePath: "/resolved/source",
                ServerUri: null));
        FailingConstructionSourceFactory constructionSourceFactory = new(new InvalidOperationException("bootstrap failed"));
        PlateauImportService service = new(
            sceneBuilder,
            datasetSourceResolver,
            constructionSourceFactory: constructionSourceFactory);

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.ExecuteAsync(
                new PlateauImportRequest(
                    Dataset: "tokyo23ku",
                    MeshCode: "53394525",
                    SourceKind: DatasetSourceKind.Remote,
                    LocalSourcePath: null,
                    ServerUri: new Uri("https://example.invalid/source.zip", UriKind.Absolute)),
                workRoot: "runtime/resonite"));

        Assert.Equal("bootstrap failed", exception.Message);
        Assert.Equal(1, sceneBuilder.EnsureConnectedCallCount);
        Assert.Equal(1, sceneBuilder.DisposeCallCount);
    }

    [Fact]
    public async Task ExecuteAsyncStopsBeforeConstructionSourceBootstrapWhenConnectionCheckFails()
    {
        List<string> callOrder = [];
        StubResoniteSceneBuilder sceneBuilder = new()
        {
            OnEnsureConnected = () => callOrder.Add("connect"),
            OnBegin = () => callOrder.Add("begin"),
            EnsureConnectedException = new InvalidOperationException("connect failed"),
        };
        RecordingDatasetSourceResolver datasetSourceResolver = new(
            new PlateauImportRequest(
                Dataset: "tokyo23ku",
                MeshCode: "53394525",
                SourceKind: DatasetSourceKind.Local,
                LocalSourcePath: "/resolved/source",
                ServerUri: null));
        RecordingConstructionSourceFactory constructionSourceFactory = new(
            CreateStubConstructionSource(),
            onCreate: () => callOrder.Add("create-source"));
        PlateauImportService service = new(
            sceneBuilder,
            datasetSourceResolver,
            constructionSourceFactory: constructionSourceFactory);

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.ExecuteAsync(
                new PlateauImportRequest(
                    Dataset: "tokyo23ku",
                    MeshCode: "53394525",
                    SourceKind: DatasetSourceKind.Remote,
                    LocalSourcePath: null,
                    ServerUri: new Uri("https://example.invalid/source.zip", UriKind.Absolute)),
                workRoot: "runtime/resonite"));

        Assert.Equal("connect failed", exception.Message);
        Assert.Equal(["connect"], callOrder);
        Assert.Empty(constructionSourceFactory.Requests);
        Assert.Equal(1, sceneBuilder.EnsureConnectedCallCount);
        Assert.Equal(1, sceneBuilder.DisposeCallCount);
    }

    private sealed class StubResoniteSceneBuilder : IResoniteSceneBuilder
    {
        public List<ResoniteConstructionCityObject> CityObjects { get; } = [];
        public List<ResoniteMaterialBinding> CommonMaterials { get; } = [];
        public List<string> BeginWorkRoots { get; } = [];
        public int EnsureConnectedCallCount { get; private set; }
        public int DisposeCallCount { get; private set; }
        public PlateauImportRequest? LastEnsureConnectedRequest { get; private set; }
        public Action? OnEnsureConnected { get; set; }
        public Action? OnBegin { get; set; }
        public Exception? EnsureConnectedException { get; set; }

        public Task EnsureConnectedAsync(
            PlateauImportRequest request,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            EnsureConnectedCallCount++;
            LastEnsureConnectedRequest = request;
            OnEnsureConnected?.Invoke();
            if (EnsureConnectedException is not null)
            {
                throw EnsureConnectedException;
            }

            return Task.CompletedTask;
        }

        public Task BeginAsync(
            ResoniteConstructionMetadata metadata,
            string workRoot,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(metadata);
            ArgumentException.ThrowIfNullOrWhiteSpace(workRoot);
            BeginWorkRoots.Add(workRoot);
            OnBegin?.Invoke();
            return Task.CompletedTask;
        }

        public Task PrepareCommonMaterialAsync(
            ResoniteMaterialBinding material,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(material);
            CommonMaterials.Add(material);
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
            DisposeCallCount++;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingDatasetSourceResolver(PlateauImportRequest resolvedRequest) : IPlateauDatasetSourceResolver
    {
        public List<PlateauImportRequest> Requests { get; } = [];
        public List<string> WorkRoots { get; } = [];

        public Task<ValidatedPlateauImportRequest> ResolveAsync(
            ValidatedPlateauImportRequest request,
            string workRoot,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request.ToImportRequest());
            WorkRoots.Add(workRoot);

            ValidatedPlateauImportSource resolvedSource = resolvedRequest.Source switch
            {
                PlateauLocalImportSource localSource => new ValidatedPlateauLocalImportSource(localSource.LocalSourcePath!),
                PlateauRemoteImportSource remoteSource => new ValidatedPlateauRemoteImportSource(remoteSource.ServerUri!),
                _ => throw new InvalidOperationException($"Unsupported source kind '{resolvedRequest.SourceKind}'."),
            };

            return Task.FromResult(
                new ValidatedPlateauImportRequest(
                    resolvedRequest.Dataset,
                    resolvedRequest.MeshCode,
                    request.MeshCodePattern,
                    resolvedSource,
                    resolvedRequest.PackageNames,
                    resolvedRequest.GlobalExcludeLodLevels,
                    resolvedRequest.ExcludeLodLevelsByPackage,
                    resolvedRequest.PackagePatterns,
                    resolvedRequest.IncludeMarkingAlways,
                    resolvedRequest.DemTerrainMode,
                    resolvedRequest.DemHeightmapMetersPerVertex,
                    resolvedRequest.DemHeightmapMaxResolution));
        }
    }

    private sealed class RecordingConstructionSourceFactory(
        IResoniteConstructionSource source,
        Action? onCreate = null)
        : IResoniteConstructionSourceFactory
    {
        public List<PlateauImportRequest> Requests { get; } = [];

        public Task<IResoniteConstructionSource> CreateAsync(
            PlateauImportRequest request,
            Action<string>? progressReporter = null,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            onCreate?.Invoke();
            progressReporter?.Invoke("[import] Parsed 1 city object from stub source in 0.000s.");
            return Task.FromResult(source);
        }
    }

    private sealed class FailingConstructionSourceFactory(Exception exception)
        : IResoniteConstructionSourceFactory
    {
        public Task<IResoniteConstructionSource> CreateAsync(
            PlateauImportRequest request,
            Action<string>? progressReporter = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromException<IResoniteConstructionSource>(exception);
        }
    }

    private sealed class StubConstructionSource(ResoniteConstructionMetadata metadata, IReadOnlyList<ResoniteConstructionCityObject> cityObjects)
        : IResoniteConstructionSource
    {
        public ResoniteConstructionMetadata Metadata { get; } = metadata;

        public async IAsyncEnumerable<ResoniteMaterialBinding> ReadCommonMaterialsAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            foreach (ResoniteMaterialBinding material in cityObjects
                         .SelectMany(static cityObject => cityObject.Materials)
                         .Where(static material => material.AssetScope == ResoniteMaterialAssetScope.Common)
                         .GroupBy(static material => material.MaterialKey, StringComparer.Ordinal)
                         .Select(static group => group.First()))
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return material;
                await Task.Yield();
            }
        }

        public IEnumerable<ResoniteConstructionCityObject> ReadCityObjects()
        {
            return cityObjects;
        }

        public async IAsyncEnumerable<ResoniteConstructionCityObject> ReadCityObjectsAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            foreach (ResoniteConstructionCityObject cityObject in cityObjects)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return cityObject;
                await Task.Yield();
            }
        }
    }

    private static StubConstructionSource CreateStubConstructionSource()
    {
        ResoniteConstructionCityObject cityObject = new(
            SlotKey: "stub-city-object",
            DisplayName: "Stub City Object",
            PackageName: "bldg",
            ActualMeshCode: "53394525",
            LodLevel: 1,
            CollisionEnabled: true,
            Transform: new ResoniteTransform(
                Position: new ResoniteFloat3(0.0, 0.0, 0.0)),
            Mesh: new ResoniteImportedMesh(
                Vertices:
                [
                    new ResoniteMeshVertex(
                        new ResoniteFloat3(0.0, 0.0, 0.0),
                        new ResoniteFloat3(0.0, 1.0, 0.0),
                        new ResoniteFloat2(0.0, 0.0)),
                    new ResoniteMeshVertex(
                        new ResoniteFloat3(1.0, 0.0, 0.0),
                        new ResoniteFloat3(0.0, 1.0, 0.0),
                        new ResoniteFloat2(1.0, 0.0)),
                    new ResoniteMeshVertex(
                        new ResoniteFloat3(0.0, 0.0, 1.0),
                        new ResoniteFloat3(0.0, 1.0, 0.0),
                        new ResoniteFloat2(0.0, 1.0)),
                ],
                Submeshes:
                [
                    new ResoniteMeshSubmesh(0, "default", [0, 1, 2]),
                ]),
            Materials:
            [
                new ResoniteMaterialBinding(
                    MaterialKey: "stub-material",
                    BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
                    MaterialType: ResoniteMaterialType.VertexColor,
                    TexturePath: null,
                    TextureSourceKind: ResoniteTextureSourceKind.Bundled,
                    Projection: ResoniteMaterialProjection.Uv,
                    DepthOffset: null,
                    TextureScale: null,
                    SubmeshIndices: [0],
                    AssetScope: ResoniteMaterialAssetScope.Common),
            ]);

        return new StubConstructionSource(
            new ResoniteConstructionMetadata(
                SchemaVersion: "3.0",
                WorldName: "Stub World",
                Request: new PlateauImportRequest(
                    Dataset: "tokyo23ku",
                    MeshCode: "53394525",
                    SourceKind: DatasetSourceKind.Local,
                    LocalSourcePath: "/resolved/source",
                    ServerUri: null),
                SourceDataset: new PlateauSourceDataset(
                    PackageNames: ["bldg"],
                    SourceFiles: ["udx/bldg/53394525/stub.gml"],
                    TerrainTextureOverlays: []),
                Attribution: new ResoniteAttribution(
                    DatasetLicense: new ResoniteLicenseComponentMetadata(
                        RequireCredit: true,
                        CreditText: "stub",
                        LicenseName: "stub",
                        LicenseUrl: "https://example.invalid/license"),
                    MaterialLicenses: []),
                LocalOrigin: new ResoniteLocalOrigin(35.0, 139.0, 0.0)),
            [cityObject]);
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

    private static void CreateRuntimeSplitBoundaryDemFixture(string datasetRoot)
    {
        string packageDirectory = Path.Combine(datasetRoot, "udx", "dem", "53394525");
        Directory.CreateDirectory(packageDirectory);

        const double westLongitude = 139.0000;
        const double eastLongitude = 139.0400;
        double splitLongitude = WebMercatorTileMath.PixelXToLongitude(
            WebMercatorTileMath.LongitudeToPixelX(westLongitude, LocalCityGmlResonitePlanBuilder.DefaultDemTerrainTextureZoomLevel)
            + LocalCityGmlResonitePlanBuilder.DefaultDemTerrainTextureMaxSize,
            LocalCityGmlResonitePlanBuilder.DefaultDemTerrainTextureZoomLevel);
        string xml =
            $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <core:CityModel xmlns:core="http://www.opengis.net/citygml/2.0" xmlns:gml="http://www.opengis.net/gml" xmlns:dem="http://www.opengis.net/citygml/relief/2.0">
              <gml:boundedBy>
                <gml:Envelope srsDimension="3" srsName="EPSG:4326">
                  <gml:lowerCorner>35.0000 {westLongitude:F12} 0</gml:lowerCorner>
                  <gml:upperCorner>35.0010 {eastLongitude:F12} 20</gml:upperCorner>
                </gml:Envelope>
              </gml:boundedBy>
              <core:cityObjectMember>
                <dem:ReliefFeature gml:id="dem-split-boundary">
                  <gml:name>Split Boundary Relief</gml:name>
                  <dem:reliefComponent>
                    <dem:TINRelief gml:id="dem-split-boundary-component">
                      <dem:tin>
                        <gml:TriangulatedSurface>
                          <gml:trianglePatches>
                            <gml:Triangle gml:id="tri-dem-boundary-west">
                              <gml:exterior>
                                <gml:LinearRing gml:id="ring-dem-boundary-west">
                                  <gml:posList>35.0000 {splitLongitude:F12} 5 35.0010 {westLongitude:F12} 10 35.0010 {splitLongitude:F12} 20 35.0000 {splitLongitude:F12} 5</gml:posList>
                                </gml:LinearRing>
                              </gml:exterior>
                            </gml:Triangle>
                            <gml:Triangle gml:id="tri-dem-boundary-east">
                              <gml:exterior>
                                <gml:LinearRing gml:id="ring-dem-boundary-east">
                                  <gml:posList>35.0000 {splitLongitude:F12} 5 35.0010 {splitLongitude:F12} 20 35.0010 {eastLongitude:F12} 25 35.0000 {splitLongitude:F12} 5</gml:posList>
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
            Path.Combine(packageDirectory, "plateau_tokyo23ku_dem_53394525_split_boundary.gml"),
            xml);
    }

    private static void CreateRuntimeSingleTriangleDemFixture(string datasetRoot)
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
                  <gml:upperCorner>35.0010 139.0010 20</gml:upperCorner>
                </gml:Envelope>
              </gml:boundedBy>
              <core:cityObjectMember>
                <dem:ReliefFeature gml:id="dem-single-triangle">
                  <gml:name>Single Triangle Relief</gml:name>
                  <dem:reliefComponent>
                    <dem:TINRelief gml:id="dem-single-triangle-component">
                      <dem:tin>
                        <gml:TriangulatedSurface>
                          <gml:trianglePatches>
                            <gml:Triangle gml:id="tri-dem-single">
                              <gml:exterior>
                                <gml:LinearRing gml:id="ring-dem-single">
                                  <gml:posList>35.0000 139.0010 5 35.0010 139.0000 10 35.0010 139.0010 20 35.0000 139.0010 5</gml:posList>
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
            Path.Combine(packageDirectory, "plateau_tokyo23ku_dem_53394525_single_triangle.gml"),
            xml);
    }

    private static ResoniteConstructionCityObject[] AlignAdjacentDemHeightMapChunkBoundaries(
        params ResoniteConstructionCityObject[] cityObjects)
    {
        MethodInfo method = typeof(LocalCityGmlResonitePlanBuilder).GetMethod(
            "AlignAdjacentDemHeightMapChunkBoundaries",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Expected private heightmap boundary alignment helper.");

        return (ResoniteConstructionCityObject[])method.Invoke(null, [cityObjects])!;
    }

    private static void ExtendBoundaryConnectedMissingHeightMapBands(
        double[] localHeights,
        bool[] sampledInsideTriangles,
        int width,
        int height)
    {
        MethodInfo method = typeof(LocalCityGmlResonitePlanBuilder).GetMethod(
            "ExtendBoundaryConnectedMissingHeightSamples",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Expected private boundary-connected heightmap fill helper.");

        method.Invoke(null, [localHeights, sampledInsideTriangles, width, height]);
    }

    private static ResoniteConstructionCityObject CreateHeightMapChunk(
        string displayName,
        double positionX,
        double positionY,
        double positionZ,
        int width,
        int height,
        double sizeX,
        double sizeY,
        double minHeight,
        double maxHeight,
        IReadOnlyList<double> heightSamples)
    {
        return new ResoniteConstructionCityObject(
            SlotKey: displayName,
            DisplayName: displayName,
            PackageName: "dem",
            ActualMeshCode: "53394525",
            LodLevel: 1,
            Transform: new ResoniteTransform(new ResoniteFloat3(positionX, positionY, positionZ)),
            Geometry: new ResoniteHeightMapGridGeometry(
                Width: width,
                Height: height,
                Size: new ResoniteFloat2(sizeX, sizeY),
                MinHeight: minHeight,
                MaxHeight: maxHeight,
                HeightSamples: heightSamples),
            Materials: []);
    }

    private static double GetHeightMapWorldHeight(
        ResoniteConstructionCityObject chunk,
        ResoniteHeightMapGridGeometry geometry,
        int row,
        int column)
    {
        return chunk.Transform.Position.Y
            - geometry.MaxHeight
            + geometry.HeightSamples[(row * geometry.Width) + column];
    }

    private static double GetCornerHeightNearestPoint(
        ResoniteConstructionCityObject chunk,
        ResoniteHeightMapGridGeometry geometry,
        double targetX,
        double targetZ)
    {
        int[] rows = [0, geometry.Height - 1];
        int[] columns = [0, geometry.Width - 1];
        double minX = chunk.Transform.Position.X - (geometry.Size.X / 2.0);
        double minZ = chunk.Transform.Position.Z - (geometry.Size.Y / 2.0);
        double stepX = geometry.Width <= 1 ? 0.0 : geometry.Size.X / (geometry.Width - 1);
        double stepZ = geometry.Height <= 1 ? 0.0 : geometry.Size.Y / (geometry.Height - 1);

        return rows
            .SelectMany(row => columns.Select(column =>
            {
                double x = minX + (column * stepX);
                double z = minZ + (row * stepZ);
                double distanceSquared = Math.Pow(x - targetX, 2.0) + Math.Pow(z - targetZ, 2.0);
                double height = GetHeightMapWorldHeight(chunk, geometry, row, column);
                return (distanceSquared, height);
            }))
            .OrderBy(static candidate => candidate.distanceSquared)
            .First()
            .height;
    }

    private static double GetNearestHeightMapWorldHeight(
        ResoniteConstructionCityObject chunk,
        ResoniteHeightMapGridGeometry geometry,
        double targetX,
        double targetZ)
    {
        double minX = chunk.Transform.Position.X - (geometry.Size.X / 2.0);
        double minZ = chunk.Transform.Position.Z - (geometry.Size.Y / 2.0);
        double stepX = geometry.Width <= 1 ? 0.0 : geometry.Size.X / (geometry.Width - 1);
        double stepZ = geometry.Height <= 1 ? 0.0 : geometry.Size.Y / (geometry.Height - 1);

        return Enumerable.Range(0, geometry.Height)
            .SelectMany(row => Enumerable.Range(0, geometry.Width).Select(column =>
            {
                double x = minX + (column * stepX);
                double z = minZ + (row * stepZ);
                double distanceSquared = Math.Pow(x - targetX, 2.0) + Math.Pow(z - targetZ, 2.0);
                double height = GetHeightMapWorldHeight(chunk, geometry, row, column);
                return (distanceSquared, height);
            }))
            .OrderBy(static candidate => candidate.distanceSquared)
            .First()
            .height;
    }

    private static void CreateRuntimeStraddledSplitBoundaryDemFixture(string datasetRoot)
    {
        string packageDirectory = Path.Combine(datasetRoot, "udx", "dem", "53394525");
        Directory.CreateDirectory(packageDirectory);

        const double westLongitude = 139.0000;
        const double eastLongitude = 139.0400;
        const double straddleDeltaLongitude = 0.00005;
        double splitLongitude = WebMercatorTileMath.PixelXToLongitude(
            WebMercatorTileMath.LongitudeToPixelX(westLongitude, LocalCityGmlResonitePlanBuilder.DefaultDemTerrainTextureZoomLevel)
            + LocalCityGmlResonitePlanBuilder.DefaultDemTerrainTextureMaxSize,
            LocalCityGmlResonitePlanBuilder.DefaultDemTerrainTextureZoomLevel);

        string xml =
            $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <core:CityModel xmlns:core="http://www.opengis.net/citygml/2.0" xmlns:gml="http://www.opengis.net/gml" xmlns:dem="http://www.opengis.net/citygml/relief/2.0">
              <gml:boundedBy>
                <gml:Envelope srsDimension="3" srsName="EPSG:4326">
                  <gml:lowerCorner>35.0000 {westLongitude:F12} 0</gml:lowerCorner>
                  <gml:upperCorner>35.0100 {eastLongitude:F12} 20</gml:upperCorner>
                </gml:Envelope>
              </gml:boundedBy>
              <core:cityObjectMember>
                <dem:ReliefFeature gml:id="dem-straddled-split-boundary">
                  <gml:name>Straddled Split Boundary Relief</gml:name>
                  <dem:reliefComponent>
                    <dem:TINRelief gml:id="dem-straddled-split-boundary-component">
                      <dem:tin>
                        <gml:TriangulatedSurface>
                          <gml:trianglePatches>
                            <gml:Triangle gml:id="tri-dem-straddled-west">
                              <gml:exterior>
                                <gml:LinearRing gml:id="ring-dem-straddled-west">
                                  <gml:posList>35.0000 {(splitLongitude + straddleDeltaLongitude):F12} 5 35.0100 {westLongitude:F12} 10 35.0100 {(splitLongitude - straddleDeltaLongitude):F12} 20 35.0000 {(splitLongitude + straddleDeltaLongitude):F12} 5</gml:posList>
                                </gml:LinearRing>
                              </gml:exterior>
                            </gml:Triangle>
                            <gml:Triangle gml:id="tri-dem-straddled-east">
                              <gml:exterior>
                                <gml:LinearRing gml:id="ring-dem-straddled-east">
                                  <gml:posList>35.0000 {(splitLongitude - straddleDeltaLongitude):F12} 5 35.0100 {(splitLongitude + straddleDeltaLongitude):F12} 20 35.0100 {eastLongitude:F12} 25 35.0000 {(splitLongitude - straddleDeltaLongitude):F12} 5</gml:posList>
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
            Path.Combine(packageDirectory, "plateau_tokyo23ku_dem_53394525_straddled_split_boundary.gml"),
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

    private static void CreateRuntimeSegmentedTerrainAlignmentFixture(string datasetRoot)
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
                  <gml:upperCorner>35.0003 139.0020 30</gml:upperCorner>
                </gml:Envelope>
              </gml:boundedBy>
              <core:cityObjectMember>
                <dem:ReliefFeature gml:id="dem-segmented-fit">
                  <gml:name>Segmented Alignment Relief</gml:name>
                  <dem:reliefComponent>
                    <dem:TINRelief gml:id="dem-segmented-fit-component">
                      <dem:tin>
                        <gml:TriangulatedSurface srsName="http://www.opengis.net/def/crs/EPSG/0/6697" srsDimension="3">
                          <gml:trianglePatches>
                            <gml:Triangle gml:id="tri-segmented-fit-0">
                              <gml:exterior>
                                <gml:LinearRing gml:id="ring-segmented-fit-0">
                                  <gml:posList>35.0000 139.0000 10 35.0000 139.0020 30 35.0003 139.0000 10 35.0000 139.0000 10</gml:posList>
                                </gml:LinearRing>
                              </gml:exterior>
                            </gml:Triangle>
                            <gml:Triangle gml:id="tri-segmented-fit-1">
                              <gml:exterior>
                                <gml:LinearRing gml:id="ring-segmented-fit-1">
                                  <gml:posList>35.0003 139.0000 10 35.0000 139.0020 30 35.0003 139.0020 30 35.0003 139.0000 10</gml:posList>
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
        File.WriteAllText(Path.Combine(tranDirectory, "appearance", "segment.png"), "fixture-segment-texture");
        File.WriteAllText(
            Path.Combine(tranDirectory, "plateau_tokyo23ku_tran_53394525.gml"),
            """
            <?xml version="1.0" encoding="UTF-8"?>
            <core:CityModel xmlns:app="http://www.opengis.net/citygml/appearance/2.0" xmlns:core="http://www.opengis.net/citygml/2.0" xmlns:gml="http://www.opengis.net/gml" xmlns:tran="http://www.opengis.net/citygml/transportation/2.0">
              <gml:boundedBy>
                <gml:Envelope srsName="http://www.opengis.net/def/crs/EPSG/0/6697" srsDimension="3">
                  <gml:lowerCorner>35.0000 139.0000 0</gml:lowerCorner>
                  <gml:upperCorner>35.0003 139.0020 0</gml:upperCorner>
                </gml:Envelope>
              </gml:boundedBy>
              <app:appearanceMember>
                <app:Appearance>
                  <app:surfaceDataMember>
                    <app:ParameterizedTexture>
                      <app:imageURI>appearance/segment.png</app:imageURI>
                      <app:target uri="#poly-road-segmented-fit">
                        <app:TexCoordList>
                          <app:textureCoordinates ring="#ring-road-segmented-fit">0 0 4 0 4 1 0 1 0 0</app:textureCoordinates>
                        </app:TexCoordList>
                      </app:target>
                    </app:ParameterizedTexture>
                  </app:surfaceDataMember>
                </app:Appearance>
              </app:appearanceMember>
              <core:cityObjectMember>
                <tran:Road gml:id="tran-segmented-fit">
                  <gml:name>Segmented Aligned Road</gml:name>
                  <tran:lod1MultiSurface>
                    <gml:MultiSurface>
                      <gml:surfaceMember>
                        <gml:Polygon gml:id="poly-road-segmented-fit">
                          <gml:exterior>
                            <gml:LinearRing gml:id="ring-road-segmented-fit">
                              <gml:posList>35.0000 139.0000 0 35.0000 139.0020 0 35.0003 139.0020 0 35.0003 139.0000 0 35.0000 139.0000 0</gml:posList>
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
    }

    private static void CreateRuntimeSegmentedSquareTerrainAlignmentFixture(string datasetRoot)
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
                  <gml:upperCorner>35.0003 139.0020 30</gml:upperCorner>
                </gml:Envelope>
              </gml:boundedBy>
              <core:cityObjectMember>
                <dem:ReliefFeature gml:id="dem-segmented-square-fit">
                  <gml:name>Segmented Square Alignment Relief</gml:name>
                  <dem:reliefComponent>
                    <dem:TINRelief gml:id="dem-segmented-square-fit-component">
                      <dem:tin>
                        <gml:TriangulatedSurface srsName="http://www.opengis.net/def/crs/EPSG/0/6697" srsDimension="3">
                          <gml:trianglePatches>
                            <gml:Triangle gml:id="tri-segmented-square-fit-0">
                              <gml:exterior>
                                <gml:LinearRing gml:id="ring-segmented-square-fit-0">
                                  <gml:posList>35.0000 139.0000 10 35.0000 139.0020 30 35.0003 139.0000 10 35.0000 139.0000 10</gml:posList>
                                </gml:LinearRing>
                              </gml:exterior>
                            </gml:Triangle>
                            <gml:Triangle gml:id="tri-segmented-square-fit-1">
                              <gml:exterior>
                                <gml:LinearRing gml:id="ring-segmented-square-fit-1">
                                  <gml:posList>35.0003 139.0000 10 35.0000 139.0020 30 35.0003 139.0020 30 35.0003 139.0000 10</gml:posList>
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

        string squrDirectory = Path.Combine(datasetRoot, "udx", "squr", "53394525");
        Directory.CreateDirectory(Path.Combine(squrDirectory, "appearance"));
        File.WriteAllText(Path.Combine(squrDirectory, "appearance", "segment-square.png"), "fixture-segment-square-texture");
        File.WriteAllText(
            Path.Combine(squrDirectory, "plateau_tokyo23ku_squr_53394525.gml"),
            """
            <?xml version="1.0" encoding="UTF-8"?>
            <core:CityModel xmlns:app="http://www.opengis.net/citygml/appearance/2.0" xmlns:core="http://www.opengis.net/citygml/2.0" xmlns:gml="http://www.opengis.net/gml" xmlns:tran="http://www.opengis.net/citygml/transportation/2.0">
              <gml:boundedBy>
                <gml:Envelope srsName="http://www.opengis.net/def/crs/EPSG/0/6697" srsDimension="3">
                  <gml:lowerCorner>35.0000 139.0000 0</gml:lowerCorner>
                  <gml:upperCorner>35.0003 139.0020 0</gml:upperCorner>
                </gml:Envelope>
              </gml:boundedBy>
              <app:appearanceMember>
                <app:Appearance>
                  <app:surfaceDataMember>
                    <app:ParameterizedTexture>
                      <app:imageURI>appearance/segment-square.png</app:imageURI>
                      <app:target uri="#poly-square-segmented-fit">
                        <app:TexCoordList>
                          <app:textureCoordinates ring="#ring-square-segmented-fit">0 0 4 0 4 1 0 1 0 0</app:textureCoordinates>
                        </app:TexCoordList>
                      </app:target>
                    </app:ParameterizedTexture>
                  </app:surfaceDataMember>
                </app:Appearance>
              </app:appearanceMember>
              <core:cityObjectMember>
                <tran:Square gml:id="squr-segmented-fit">
                  <gml:name>Segmented Aligned Square</gml:name>
                  <tran:lod1MultiSurface>
                    <gml:MultiSurface>
                      <gml:surfaceMember>
                        <gml:Polygon gml:id="poly-square-segmented-fit">
                          <gml:exterior>
                            <gml:LinearRing gml:id="ring-square-segmented-fit">
                              <gml:posList>35.0000 139.0000 0 35.0000 139.0020 0 35.0003 139.0020 0 35.0003 139.0000 0 35.0000 139.0000 0</gml:posList>
                            </gml:LinearRing>
                          </gml:exterior>
                        </gml:Polygon>
                      </gml:surfaceMember>
                    </gml:MultiSurface>
                  </tran:lod1MultiSurface>
                </tran:Square>
              </core:cityObjectMember>
            </core:CityModel>
            """);
    }

    private static void CreateRuntimeSegmentDensityComparisonFixture(string datasetRoot)
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
                  <gml:upperCorner>35.0005 139.0020 30</gml:upperCorner>
                </gml:Envelope>
              </gml:boundedBy>
              <core:cityObjectMember>
                <dem:ReliefFeature gml:id="dem-segment-density">
                  <gml:name>Segment Density Relief</gml:name>
                  <dem:reliefComponent>
                    <dem:TINRelief gml:id="dem-segment-density-component">
                      <dem:tin>
                        <gml:TriangulatedSurface srsName="http://www.opengis.net/def/crs/EPSG/0/6697" srsDimension="3">
                          <gml:trianglePatches>
                            <gml:Triangle gml:id="tri-segment-density-0">
                              <gml:exterior>
                                <gml:LinearRing gml:id="ring-segment-density-0">
                                  <gml:posList>35.0000 139.0000 10 35.0000 139.0020 30 35.0005 139.0000 10 35.0000 139.0000 10</gml:posList>
                                </gml:LinearRing>
                              </gml:exterior>
                            </gml:Triangle>
                            <gml:Triangle gml:id="tri-segment-density-1">
                              <gml:exterior>
                                <gml:LinearRing gml:id="ring-segment-density-1">
                                  <gml:posList>35.0005 139.0000 10 35.0000 139.0020 30 35.0005 139.0020 30 35.0005 139.0000 10</gml:posList>
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
                  <gml:upperCorner>35.0005 139.0020 0</gml:upperCorner>
                </gml:Envelope>
              </gml:boundedBy>
              <core:cityObjectMember>
                <tran:Road gml:id="tran-segment-density-narrow">
                  <gml:name>Narrow Segmented Road</gml:name>
                  <tran:lod1MultiSurface>
                    <gml:MultiSurface>
                      <gml:surfaceMember>
                        <gml:Polygon gml:id="poly-road-segment-density-narrow">
                          <gml:exterior>
                            <gml:LinearRing gml:id="ring-road-segment-density-narrow">
                              <gml:posList>35.000000 139.0000 0 35.000000 139.0020 0 35.000018 139.0020 0 35.000018 139.0000 0 35.000000 139.0000 0</gml:posList>
                            </gml:LinearRing>
                          </gml:exterior>
                        </gml:Polygon>
                      </gml:surfaceMember>
                    </gml:MultiSurface>
                  </tran:lod1MultiSurface>
                </tran:Road>
              </core:cityObjectMember>
              <core:cityObjectMember>
                <tran:Road gml:id="tran-segment-density-wide">
                  <gml:name>Wide Segmented Road</gml:name>
                  <tran:lod1MultiSurface>
                    <gml:MultiSurface>
                      <gml:surfaceMember>
                        <gml:Polygon gml:id="poly-road-segment-density-wide">
                          <gml:exterior>
                            <gml:LinearRing gml:id="ring-road-segment-density-wide">
                              <gml:posList>35.000200 139.0000 0 35.000200 139.0020 0 35.000300 139.0020 0 35.000300 139.0000 0 35.000200 139.0000 0</gml:posList>
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
    }

    private static void CreateRuntimeSkewedTransportationFixture(string datasetRoot)
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
                  <gml:lowerCorner>35.0000 139.0000 0</gml:lowerCorner>
                  <gml:upperCorner>35.0006 139.0020 20</gml:upperCorner>
                </gml:Envelope>
              </gml:boundedBy>
              <core:cityObjectMember>
                <dem:ReliefFeature gml:id="dem-skewed-road">
                  <gml:name>Skewed Road Relief</gml:name>
                  <dem:reliefComponent>
                    <dem:TINRelief gml:id="dem-skewed-road-component">
                      <dem:tin>
                        <gml:TriangulatedSurface srsName="http://www.opengis.net/def/crs/EPSG/0/6697" srsDimension="3">
                          <gml:trianglePatches>
                            <gml:Triangle gml:id="tri-skewed-road-0">
                              <gml:exterior>
                                <gml:LinearRing gml:id="ring-skewed-road-0">
                                  <gml:posList>35.0000 139.0000 0 35.0000 139.0020 20 35.0006 139.0000 0 35.0000 139.0000 0</gml:posList>
                                </gml:LinearRing>
                              </gml:exterior>
                            </gml:Triangle>
                            <gml:Triangle gml:id="tri-skewed-road-1">
                              <gml:exterior>
                                <gml:LinearRing gml:id="ring-skewed-road-1">
                                  <gml:posList>35.0006 139.0000 0 35.0000 139.0020 20 35.0006 139.0020 20 35.0006 139.0000 0</gml:posList>
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
                  <gml:lowerCorner>35.0003 139.0000 0</gml:lowerCorner>
                  <gml:upperCorner>35.0005 139.0020 0</gml:upperCorner>
                </gml:Envelope>
              </gml:boundedBy>
              <core:cityObjectMember>
                <tran:Road gml:id="tran-skewed-road">
                  <gml:name>Skewed Segmented Road</gml:name>
                  <tran:lod1MultiSurface>
                    <gml:MultiSurface>
                      <gml:surfaceMember>
                        <gml:Polygon gml:id="poly-skewed-road">
                          <gml:exterior>
                            <gml:LinearRing gml:id="ring-skewed-road">
                              <gml:posList>35.000350 139.0000 0 35.000350 139.0020 0 35.000420 139.0014 0 35.000420 139.0008 0 35.000350 139.0000 0</gml:posList>
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
    }

    private static void CreateRuntimePartialDemCoverageRoadFixture(string datasetRoot)
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
                  <gml:lowerCorner>35.0000 139.0000 0</gml:lowerCorner>
                  <gml:upperCorner>35.0001 139.0010 20</gml:upperCorner>
                </gml:Envelope>
              </gml:boundedBy>
              <core:cityObjectMember>
                <dem:ReliefFeature gml:id="dem-partial-coverage">
                  <gml:name>Partial Coverage Relief</gml:name>
                  <dem:reliefComponent>
                    <dem:TINRelief gml:id="dem-partial-coverage-component">
                      <dem:tin>
                        <gml:TriangulatedSurface srsName="http://www.opengis.net/def/crs/EPSG/0/6697" srsDimension="3">
                          <gml:trianglePatches>
                            <gml:Triangle gml:id="tri-partial-coverage">
                              <gml:exterior>
                                <gml:LinearRing gml:id="ring-partial-coverage">
                                  <gml:posList>35.0000 139.0000 0 35.0000 139.0010 20 35.0001 139.0000 0 35.0000 139.0000 0</gml:posList>
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
                  <gml:lowerCorner>35.0000 139.0000 50</gml:lowerCorner>
                  <gml:upperCorner>35.0001 139.0020 50</gml:upperCorner>
                </gml:Envelope>
              </gml:boundedBy>
              <core:cityObjectMember>
                <tran:Road gml:id="tran-partial-coverage-road">
                  <gml:name>Partial Coverage Road</gml:name>
                  <tran:lod1MultiSurface>
                    <gml:MultiSurface>
                      <gml:surfaceMember>
                        <gml:Polygon gml:id="poly-partial-coverage-road">
                          <gml:exterior>
                            <gml:LinearRing gml:id="ring-partial-coverage-road">
                              <gml:posList>35.000010 139.0002 50 35.000010 139.0020 50 35.000025 139.0020 50 35.000025 139.0002 50 35.000010 139.0002 50</gml:posList>
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

    private static void CreateRuntimeSlopedTransportationFixture(string datasetRoot)
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
                  <gml:upperCorner>20 2 4</gml:upperCorner>
                </gml:Envelope>
              </gml:boundedBy>
              <core:cityObjectMember>
                <tran:Road gml:id="tran-generated-sloped">
                  <gml:name>Segmented Slope Road</gml:name>
                  <tran:lod2MultiSurface>
                    <gml:MultiSurface>
                      <gml:surfaceMember>
                        <gml:Polygon gml:id="poly-generated-sloped-road">
                          <gml:exterior>
                            <gml:LinearRing gml:id="ring-generated-sloped-road">
                              <gml:posList>0 0 0 20 0 1 20 2 4 0 2 0 0 0 0</gml:posList>
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

    private static void CreateRuntimeTexturelessWaterwayFixture(string datasetRoot)
    {
        string waterwayDirectory = Path.Combine(datasetRoot, "udx", "wwy", "53394525");
        Directory.CreateDirectory(waterwayDirectory);
        File.WriteAllText(
            Path.Combine(waterwayDirectory, "plateau_tokyo23ku_wwy_53394525.gml"),
            """
            <?xml version="1.0" encoding="UTF-8"?>
            <core:CityModel xmlns:core="http://www.opengis.net/citygml/2.0" xmlns:gml="http://www.opengis.net/gml" xmlns:wwy="urn:plateau:test:wwy">
              <gml:boundedBy>
                <gml:Envelope srsDimension="3">
                  <gml:lowerCorner>0 0 0</gml:lowerCorner>
                  <gml:upperCorner>12 2 1</gml:upperCorner>
                </gml:Envelope>
              </gml:boundedBy>
              <core:cityObjectMember>
                <wwy:Waterway gml:id="wwy-generated-uv">
                  <gml:name>Generated UV Waterway</gml:name>
                  <wwy:lod2MultiSurface>
                    <gml:MultiSurface>
                      <gml:surfaceMember>
                        <gml:Polygon gml:id="poly-generated-uv-waterway">
                          <gml:exterior>
                            <gml:LinearRing gml:id="ring-generated-uv-waterway">
                              <gml:posList>0 0 0 12 0 1 12 2 1 0 2 0 0 0 0</gml:posList>
                            </gml:LinearRing>
                          </gml:exterior>
                        </gml:Polygon>
                      </gml:surfaceMember>
                    </gml:MultiSurface>
                  </wwy:lod2MultiSurface>
                </wwy:Waterway>
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

    private static IEnumerable<(ResoniteFloat3 Position, double UvX)> CollectWorldVertices(ResoniteConstructionCityObject cityObject)
    {
        foreach (ResoniteMeshVertex vertex in cityObject.Mesh.Vertices)
        {
            yield return (
                new ResoniteFloat3(
                    cityObject.Transform.Position.X + vertex.Position.X,
                    cityObject.Transform.Position.Y + vertex.Position.Y,
                    cityObject.Transform.Position.Z + vertex.Position.Z),
                vertex.UV0.X);
        }
    }

    private static string CreateGeneratedDemTexturePathForMeshCode(string meshCode)
    {
        Assert.True(PlateauMeshCode.TryGetBounds(meshCode, out (double SouthLatitude, double NorthLatitude, double WestLongitude, double EastLongitude) bounds));
        double leftPixel = WebMercatorTileMath.LongitudeToPixelX(bounds.WestLongitude, LocalCityGmlResonitePlanBuilder.DefaultDemTerrainTextureZoomLevel);
        double rightPixel = WebMercatorTileMath.LongitudeToPixelX(bounds.EastLongitude, LocalCityGmlResonitePlanBuilder.DefaultDemTerrainTextureZoomLevel);
        double topPixel = WebMercatorTileMath.LatitudeToPixelY(bounds.NorthLatitude, LocalCityGmlResonitePlanBuilder.DefaultDemTerrainTextureZoomLevel);
        double bottomPixel = WebMercatorTileMath.LatitudeToPixelY(bounds.SouthLatitude, LocalCityGmlResonitePlanBuilder.DefaultDemTerrainTextureZoomLevel);

        MethodInfo method = typeof(LocalCityGmlResonitePlanBuilder).GetMethod(
            "CreateDemTerrainTexturePath",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Expected private DEM terrain texture path helper.");
        return (string)method.Invoke(null, [leftPixel, rightPixel, topPixel, bottomPixel])!;
    }

    internal sealed record CapturedResoniteScene(
        ResoniteConstructionMetadata Metadata,
        IReadOnlyList<ResoniteConstructionCityObject> CityObjects);

    private static bool Approximately(double actual, double expected)
    {
        return Math.Abs(actual - expected) < 1e-4;
    }

    private static double Round(double value)
    {
        return Math.Round(value, 6);
    }

    private static void CreateZipArchiveFromDirectory(string sourceDirectory, string archivePath)
    {
        using FileStream stream = File.Create(archivePath);
        using ZipArchive archive = new(stream, ZipArchiveMode.Create, leaveOpen: false);
        foreach (string filePath in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            string relativePath = Path.GetRelativePath(sourceDirectory, filePath).Replace('\\', '/');
            ZipArchiveEntry entry = archive.CreateEntry(relativePath);
            using Stream entryStream = entry.Open();
            using FileStream fileStream = File.OpenRead(filePath);
            fileStream.CopyTo(entryStream);
        }
    }

    private static void CreateSevenZipArchiveFromDirectory(string sourceDirectory, string archivePath)
    {
        using FileStream stream = File.Create(archivePath);
        using SharpCompress.Writers.IWriter writer = SevenZipWriter.OpenWriter(
            stream,
            new SevenZipWriterOptions(CompressionType.LZMA)
            {
                LeaveStreamOpen = false,
            });

        foreach (string filePath in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            string relativePath = Path.GetRelativePath(sourceDirectory, filePath).Replace('\\', '/');
            using FileStream fileStream = File.OpenRead(filePath);
            writer.Write(relativePath, fileStream, modificationTime: null);
        }
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
