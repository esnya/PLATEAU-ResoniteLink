using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;

using PlateauResoniteLink.Application.Importing;
using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Tests.Targets;

public sealed class CanonicalSceneEquivalenceTests
{
    public static TheoryData<string, string, string, string[]> RealFixtureScenarios => new()
    {
        { "single-bldg", "LocalPlateauDataset", "53394525", ["bldg"] },
        { "mixed-bldg-tran-luse", "LocalPlateauDatasetMixedObjects", "53394525", ["bldg", "tran", "luse"] },
        { "parent-mesh-bldg-tran-luse", "LocalPlateauDatasetParentMeshPackages", "533945", ["bldg", "tran", "luse"] },
    };

    [Theory]
    [MemberData(nameof(RealFixtureScenarios))]
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The import service owns the scene sink lifetime for this integration path.")]
    public async Task RealFixtureLiveSendIntoFakeLinkMatchesCanonicalBaseline(
        string scenarioName,
        string fixtureName,
        string meshCode,
        string[] packageNames)
    {
        SceneSinkRecordingClient client = new();
        PlateauImportService service = CreateService(
            ResoniteLiveSceneImportTargetTestSupport.CreateImportTarget(client, enableMeshBake: true));
        PlateauImportRequest request = new(
            Dataset: scenarioName,
            MeshCode: meshCode,
            CityGmlSource: DatasetLocation.Local(TestData.GetFixturePath(fixtureName)),
            PackageNames: packageNames);

        ImportExecutionResult result = await service.ExecuteAsync(
            request,
            TestData.GetRepositoryPath(".tmp", "canonical-scene-equivalence-tests"));

        string[] actual = CanonicalSceneEquivalenceTestSupport.CreateSignature(scenarioName, result, client);

        Assert.Equal(GetExpectedSignature(scenarioName), actual);
    }

    private static string[] GetExpectedSignature(string scenarioName)
    {
        return scenarioName switch
        {
            "single-bldg" =>
            [
                "scenario=single-bldg",
                "scene=PLATEAU single-bldg 53394525",
                "processed=1",
                "source-files=udx/bldg/53394525/plateau_tokyo23ku_bldg_53394525.gml",
                "selected-meshes=53394525",
                "data-usage=CityGmlSourceFile:udx/bldg/53394525/plateau_tokyo23ku_bldg_53394525.gml:1",
                "connects=0",
                "slots=88:Assets:1|AtlasBake bldg LOD2 #1 [atlasbake-plateau_tokyo23ku_bldg_53394525-bldg-lod2-1]:2|Common Materials:1|LOD2:2|PLATEAU Shared Assets:1|PLATEAU single-bldg:1|city-furniture:1|facade-highrise-glass:1|facade-highrise-night-low:1|facade-midrise-grid:1|generic:1|other:1|plateau_tokyo23ku_bldg_53394525:2|road-triplanar:1|road-uv:1|roof:1|uv:2|uv-terrain-aligned:2|variant-0:18|variant-1:16|variant-2:7|variant-3:6|variant-4:2|variant-5:2|variant-6:1|variant-7:1|variant-8:1|vegetation:1|vertex-color:1|wall-apartment-tile-mid:1|wall-brick-retro:1|wall-commercial-panel:1|wall-factory-metal:1|wall-rc-painted-mid:1|wall-res-plaster-low:1|wall-res-tile-low:1|wall-school-public-band:1|wall-wood-rural:1",
                "components=270:[FrooxEngine]FrooxEngine.License:1|[FrooxEngine]FrooxEngine.MeshCollider:1|[FrooxEngine]FrooxEngine.MeshRenderer:1|[FrooxEngine]FrooxEngine.PBS_Metallic:32|[FrooxEngine]FrooxEngine.PBS_TriplanarMetallic:25|[FrooxEngine]FrooxEngine.PBS_VertexColorMetallic:2|[FrooxEngine]FrooxEngine.StaticMesh:1|[FrooxEngine]FrooxEngine.StaticTexture2D:207",
                "batches=7:1:4|11:1|33:1|310:1",
                "updates=0",
                "meshes=18:True:4/2:384:uv:18:16:8:4/3:1",
                "textures=raw:2048x2048:Linear:16777216:107|raw:2048x2048:sRGB:16777216:50",
                "slot-gets=PLATEAU Shared Assets/Common Materials/city-furniture:1:6|PLATEAU Shared Assets/Common Materials/facade-highrise-glass:1:3|PLATEAU Shared Assets/Common Materials/facade-highrise-night-low:1:2|PLATEAU Shared Assets/Common Materials/facade-midrise-grid:1:2|PLATEAU Shared Assets/Common Materials/other:1:9|PLATEAU Shared Assets/Common Materials/road-triplanar:1:4|PLATEAU Shared Assets/Common Materials/road-uv:1:4|PLATEAU Shared Assets/Common Materials/roof:1:4|PLATEAU Shared Assets/Common Materials/vegetation:1:2|PLATEAU Shared Assets/Common Materials/wall-apartment-tile-mid:1:2|PLATEAU Shared Assets/Common Materials/wall-brick-retro:1:2|PLATEAU Shared Assets/Common Materials/wall-commercial-panel:1:2|PLATEAU Shared Assets/Common Materials/wall-factory-metal:1:1|PLATEAU Shared Assets/Common Materials/wall-rc-painted-mid:1:2|PLATEAU Shared Assets/Common Materials/wall-res-plaster-low:1:2|PLATEAU Shared Assets/Common Materials/wall-res-tile-low:1:4|PLATEAU Shared Assets/Common Materials/wall-school-public-band:1:2|PLATEAU Shared Assets/Common Materials/wall-wood-rural:1:1|PLATEAU Shared Assets/Common Materials:2:54|Root:1:2",
            ],
            "mixed-bldg-tran-luse" =>
            [
                "scenario=mixed-bldg-tran-luse",
                "scene=PLATEAU mixed-bldg-tran-luse 53394525",
                "processed=1",
                "source-files=udx/bldg/53394525/plateau_tokyo23ku_bldg_53394525.gml|udx/luse/53394525/plateau_tokyo23ku_luse_53394525.gml|udx/tran/53394525/plateau_tokyo23ku_tran_53394525.gml",
                "selected-meshes=53394525",
                "data-usage=CityGmlSourceFile:udx/bldg/53394525/plateau_tokyo23ku_bldg_53394525.gml:1|CityGmlSourceFile:udx/luse/53394525/plateau_tokyo23ku_luse_53394525.gml:1|CityGmlSourceFile:udx/tran/53394525/plateau_tokyo23ku_tran_53394525.gml:1",
                "connects=0",
                "slots=100:Assets:1|AtlasBake bldg LOD2 #1 [atlasbake-plateau_tokyo23ku_bldg_53394525-bldg-lod2-1]:2|Common Materials:1|LOD1:2|LOD2:2|LOD3:2|Land Use One:2|PLATEAU Shared Assets:1|PLATEAU mixed-bldg-tran-luse:1|Road Segment One:2|city-furniture:1|facade-highrise-glass:1|facade-highrise-night-low:1|facade-midrise-grid:1|generic:1|other:1|plateau_tokyo23ku_bldg_53394525:2|plateau_tokyo23ku_luse_53394525:2|plateau_tokyo23ku_tran_53394525:2|road-triplanar:1|road-uv:1|roof:1|uv:2|uv-terrain-aligned:2|variant-0:18|variant-1:16|variant-2:7|variant-3:6|variant-4:2|variant-5:2|variant-6:1|variant-7:1|variant-8:1|vegetation:1|vertex-color:1|wall-apartment-tile-mid:1|wall-brick-retro:1|wall-commercial-panel:1|wall-factory-metal:1|wall-rc-painted-mid:1|wall-res-plaster-low:1|wall-res-tile-low:1|wall-school-public-band:1|wall-wood-rural:1",
                "components=281:[FrooxEngine]FrooxEngine.License:1|[FrooxEngine]FrooxEngine.MeshCollider:3|[FrooxEngine]FrooxEngine.MeshRenderer:3|[FrooxEngine]FrooxEngine.PBS_Metallic:32|[FrooxEngine]FrooxEngine.PBS_TriplanarMetallic:26|[FrooxEngine]FrooxEngine.PBS_VertexColorMetallic:2|[FrooxEngine]FrooxEngine.StaticMesh:3|[FrooxEngine]FrooxEngine.StaticTexture2D:210|[FrooxEngine]FrooxEngine.WireframeMaterial:1",
                "batches=17:1:12|6:1|9:1|11:1|33:1|310:1",
                "updates=0",
                "meshes=12:True:2/2:9724675.714:uv:12:5:4:2/2:1|18:True:4/2:36168555.922:uv:18:16:8:6/5:1|6:False:2:-0:uv:6:6:4:2/2:1",
                "textures=raw:2048x2048:Linear:16777216:109|raw:2048x2048:sRGB:16777216:51",
                "slot-gets=PLATEAU Shared Assets/Common Materials/city-furniture:1:6|PLATEAU Shared Assets/Common Materials/facade-highrise-glass:1:3|PLATEAU Shared Assets/Common Materials/facade-highrise-night-low:1:2|PLATEAU Shared Assets/Common Materials/facade-midrise-grid:1:2|PLATEAU Shared Assets/Common Materials/other:1:9|PLATEAU Shared Assets/Common Materials/road-triplanar:1:4|PLATEAU Shared Assets/Common Materials/road-uv:1:4|PLATEAU Shared Assets/Common Materials/roof:1:4|PLATEAU Shared Assets/Common Materials/vegetation:1:2|PLATEAU Shared Assets/Common Materials/wall-apartment-tile-mid:1:2|PLATEAU Shared Assets/Common Materials/wall-brick-retro:1:2|PLATEAU Shared Assets/Common Materials/wall-commercial-panel:1:2|PLATEAU Shared Assets/Common Materials/wall-factory-metal:1:1|PLATEAU Shared Assets/Common Materials/wall-rc-painted-mid:1:2|PLATEAU Shared Assets/Common Materials/wall-res-plaster-low:1:2|PLATEAU Shared Assets/Common Materials/wall-res-tile-low:1:4|PLATEAU Shared Assets/Common Materials/wall-school-public-band:1:2|PLATEAU Shared Assets/Common Materials/wall-wood-rural:1:1|PLATEAU Shared Assets/Common Materials:2:54|Root:1:2",
            ],
            "parent-mesh-bldg-tran-luse" =>
            [
                "scenario=parent-mesh-bldg-tran-luse",
                "scene=PLATEAU parent-mesh-bldg-tran-luse 533945",
                "processed=1",
                "source-files=udx/luse/533945/plateau_tokyo23ku_luse_533945.gml|udx/tran/533945/plateau_tokyo23ku_tran_533945.gml",
                "selected-meshes=533945",
                "data-usage=CityGmlSourceFile:udx/luse/533945/plateau_tokyo23ku_luse_533945.gml:1|CityGmlSourceFile:udx/tran/533945/plateau_tokyo23ku_tran_533945.gml:1",
                "connects=0",
                "slots=94:Assets:1|AtlasBake tran LOD1 #1 [atlasbake-plateau_tokyo23ku_tran_533945-tran-lod1-1]:2|Common Materials:1|LOD1:4|PLATEAU Shared Assets:1|PLATEAU parent-mesh-bldg-tran-luse:1|Parent Tile Land Use:2|city-furniture:1|facade-highrise-glass:1|facade-highrise-night-low:1|facade-midrise-grid:1|generic:1|other:1|plateau_tokyo23ku_luse_533945:2|plateau_tokyo23ku_tran_533945:2|road-triplanar:1|road-uv:1|roof:1|uv:2|uv-terrain-aligned:2|variant-0:18|variant-1:16|variant-2:7|variant-3:6|variant-4:2|variant-5:2|variant-6:1|variant-7:1|variant-8:1|vegetation:1|vertex-color:1|wall-apartment-tile-mid:1|wall-brick-retro:1|wall-commercial-panel:1|wall-factory-metal:1|wall-rc-painted-mid:1|wall-res-plaster-low:1|wall-res-tile-low:1|wall-school-public-band:1|wall-wood-rural:1",
                "components=268:[FrooxEngine]FrooxEngine.License:1|[FrooxEngine]FrooxEngine.MeshCollider:2|[FrooxEngine]FrooxEngine.MeshRenderer:2|[FrooxEngine]FrooxEngine.PBS_Metallic:31|[FrooxEngine]FrooxEngine.PBS_TriplanarMetallic:25|[FrooxEngine]FrooxEngine.PBS_VertexColorMetallic:2|[FrooxEngine]FrooxEngine.StaticMesh:2|[FrooxEngine]FrooxEngine.StaticTexture2D:202|[FrooxEngine]FrooxEngine.WireframeMaterial:1",
                "batches=12:1:8|5:1|6:1|33:1|310:1",
                "updates=0",
                "meshes=6:False:2:-0:uv:6:6:4:2/2:1|78:True:4/22:139613.996:uv:78:78:34:13/6:1",
                "textures=raw:2048x2048:Linear:16777216:104|raw:2048x2048:sRGB:16777216:48",
                "slot-gets=PLATEAU Shared Assets/Common Materials/city-furniture:1:6|PLATEAU Shared Assets/Common Materials/facade-highrise-glass:1:3|PLATEAU Shared Assets/Common Materials/facade-highrise-night-low:1:2|PLATEAU Shared Assets/Common Materials/facade-midrise-grid:1:2|PLATEAU Shared Assets/Common Materials/other:1:9|PLATEAU Shared Assets/Common Materials/road-triplanar:1:4|PLATEAU Shared Assets/Common Materials/road-uv:1:4|PLATEAU Shared Assets/Common Materials/roof:1:4|PLATEAU Shared Assets/Common Materials/vegetation:1:2|PLATEAU Shared Assets/Common Materials/wall-apartment-tile-mid:1:2|PLATEAU Shared Assets/Common Materials/wall-brick-retro:1:2|PLATEAU Shared Assets/Common Materials/wall-commercial-panel:1:2|PLATEAU Shared Assets/Common Materials/wall-factory-metal:1:1|PLATEAU Shared Assets/Common Materials/wall-rc-painted-mid:1:2|PLATEAU Shared Assets/Common Materials/wall-res-plaster-low:1:2|PLATEAU Shared Assets/Common Materials/wall-res-tile-low:1:4|PLATEAU Shared Assets/Common Materials/wall-school-public-band:1:2|PLATEAU Shared Assets/Common Materials/wall-wood-rural:1:1|PLATEAU Shared Assets/Common Materials:2:54|Root:1:2",
            ],
            _ => throw new ArgumentOutOfRangeException(nameof(scenarioName), scenarioName, null),
        };
    }

    private static PlateauImportService CreateService(ISceneSink sceneSink)
    {
        DefaultPlateauDatasetContentSourceFactory datasetContentSourceFactory = new(
            new RemoteArchiveDistributionPolicy(),
            new ArchiveFileLayoutPolicy());
        DefaultDemTextureSourcePolicy demTextureSourcePolicy = new(
            new DefaultDemTerrainGeoReferencedRasterCatalogFactory(datasetContentSourceFactory));
        LocalCityGmlDocumentReader documentReader = new(
            datasetContentSourceFactory,
            new CityGmlAppearanceStoreFactory(),
            new CityGmlLodSelector());
        CommonMaterialCatalog<DefaultCommonMaterialMember> commonMaterials = CommonMaterialCatalog.Create();

        return new PlateauImportService(
            sceneSink,
            new LocalDatasetSourceResolver(),
            new DefaultImportedSceneSourceFactory(
                documentReader,
                new DefaultImportedSceneSourceComposer(
                    new LocalCityGmlGeometryProjector(new DefaultMaterialResolver(commonMaterials)),
                    demTextureSourcePolicy),
                demTextureSourcePolicy,
                new CompositeImportedObjectUnitOptimizer([new ImportedDynamicMaterialUvUnitOptimizer()])),
            commonMaterials,
            new ArchiveFileLayoutPolicy());
    }

    private sealed class LocalDatasetSourceResolver : IPlateauDatasetSourceResolver
    {
        public Task<ValidatedPlateauImportRequest> ResolveAsync(
            ValidatedPlateauImportRequest request,
            string workRoot,
            CancellationToken cancellationToken = default)
        {
            _ = workRoot;
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(request);
        }
    }
}
