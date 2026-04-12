using System.Diagnostics.CodeAnalysis;

using Plateau.ResoniteLink.Application.Importing;
using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Tests.Application;

public sealed class LocalCityGmlResonitePlanBuilderTests
{
    private static PlateauImportService CreateService(IResoniteSceneBuilder sceneBuilder)
    {
        return new PlateauImportService(
            sceneBuilder,
            new CkanPlateauDatasetSourceResolver(),
            constructionSourceFactory: new LocalCityGmlConstructionSourceFactory(
                new LocalCityGmlDocumentReader(),
                new LocalCityGmlConstructionComposer(
                    new LocalCityGmlGeometryProjector(new DefaultMaterialResolver()))));
    }

    [Fact]
    [SuppressMessage(
        "Performance",
        "CA1849:Call async methods when in an async method",
        Justification = "This test intentionally compares the sync wrapper against the async entrypoint.")]
    public async Task CreateConstructionSourceAsyncMatchesCreateConstructionSourceForCanonicalBootstrap()
    {
        string fixturePath = TestData.GetFixturePath("LocalPlateauDataset");
        PlateauImportRequest request = new(
            Dataset: "tokyo23ku",
            MeshCode: "53394525",
            SourceKind: DatasetSourceKind.Local,
            LocalSourcePath: fixturePath,
            ServerUri: null);

        IResoniteConstructionSource asyncSource = await LocalCityGmlResonitePlanBuilder.CreateConstructionSourceAsync(request);
        IResoniteConstructionSource syncSource = LocalCityGmlResonitePlanBuilder.CreateConstructionSource(request);

        Assert.Equal(asyncSource.Metadata.SchemaVersion, syncSource.Metadata.SchemaVersion);
        Assert.Equal(asyncSource.Metadata.WorldName, syncSource.Metadata.WorldName);
        Assert.Same(request, asyncSource.Metadata.Request);
        Assert.Same(request, syncSource.Metadata.Request);
        Assert.Equal(asyncSource.Metadata.SourceDataset.PackageNames, syncSource.Metadata.SourceDataset.PackageNames);
        Assert.Equal(asyncSource.Metadata.SourceDataset.SourceFiles, syncSource.Metadata.SourceDataset.SourceFiles);
        Assert.Equal(
            asyncSource.Metadata.SourceDataset.TerrainTextureOverlays,
            syncSource.Metadata.SourceDataset.TerrainTextureOverlays);
        Assert.Equal(asyncSource.Metadata.SourceDataset.RequestedMeshCodes, syncSource.Metadata.SourceDataset.RequestedMeshCodes);
        Assert.Equal(asyncSource.Metadata.LocalOrigin, syncSource.Metadata.LocalOrigin);
    }

    [Fact]
    public async Task SplitParsedCityObjectPreservesNonGeneratedDemSurfacesWhenOverlaysSplit()
    {
        using TemporaryDirectory datasetRoot = new();
        CreateRuntimeMixedSurfaceDemFixture(datasetRoot.Path);

        await using StubSceneBuilder sceneBuilder = new();
        PlateauImportService service = CreateService(sceneBuilder);

        ImportExecutionResult result = await service.ExecuteAsync(
            new PlateauImportRequest(
                Dataset: "tokyo23ku",
                MeshCode: "53394525",
                SourceKind: DatasetSourceKind.Local,
                LocalSourcePath: datasetRoot.Path,
                PackageNames: ["dem"],
                ServerUri: null),
            workRoot: "runtime/resonite");

        PlateauImportServiceTests.CapturedResoniteScene scene = result.Metadata.ToScene(sceneBuilder.CityObjects);

        ResoniteConstructionCityObject[] demCityObjects = scene.CityObjects
            .Where(static cityObject => cityObject.PackageName == "dem")
            .ToArray();

        Assert.Equal(3, demCityObjects.Length);

        const string explicitTexturePath = "udx/dem/53394525/appearance/mixed_surface.png";

        int generatedChunkCount = demCityObjects
            .Count(static cityObject => cityObject.Materials.Any(
                static material =>
                    material.TextureSourceKind == ResoniteTextureSourceKind.Bundled
                    && material.TexturePath is not null
                    && material.TexturePath.StartsWith(
                        LocalCityGmlResonitePlanBuilder.DefaultDemTerrainTexturePath,
                        StringComparison.Ordinal)));
        Assert.Equal(2, generatedChunkCount);

        int explicitChunkCount = demCityObjects
            .Count(cityObject => cityObject.Materials.Any(material => string.Equals(
                material.TexturePath,
                explicitTexturePath,
                StringComparison.Ordinal)));
        Assert.Equal(1, explicitChunkCount);
    }

    private static void CreateRuntimeMixedSurfaceDemFixture(string datasetRoot)
    {
        string packageDirectory = Path.Combine(datasetRoot, "udx", "dem", "53394525");
        Directory.CreateDirectory(packageDirectory);

        string xml =
            """
            <?xml version="1.0" encoding="UTF-8"?>
            <core:CityModel xmlns:app="http://www.opengis.net/citygml/appearance/2.0" xmlns:core="http://www.opengis.net/citygml/2.0" xmlns:gml="http://www.opengis.net/gml" xmlns:dem="http://www.opengis.net/citygml/relief/2.0">
              <gml:boundedBy>
                <gml:Envelope srsName="http://www.opengis.net/def/crs/EPSG/0/6697" srsDimension="3">
                  <gml:lowerCorner>35.0000 139.0000 0</gml:lowerCorner>
                  <gml:upperCorner>35.0100 139.0400 20</gml:upperCorner>
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
                                  <gml:posList>35.0000 139.0000 5 35.0100 139.0000 10 35.0100 139.0180 12 35.0000 139.0000 5</gml:posList>
                                </gml:LinearRing>
                              </gml:exterior>
                            </gml:Triangle>
                            <gml:Triangle gml:id="tri-dem-east">
                              <gml:exterior>
                                <gml:LinearRing gml:id="ring-dem-east">
                                  <gml:posList>35.0000 139.0220 6 35.0100 139.0220 8 35.0100 139.0400 14 35.0000 139.0220 6</gml:posList>
                                </gml:LinearRing>
                              </gml:exterior>
                            </gml:Triangle>
                            <gml:Triangle gml:id="tri-dem-textured">
                              <gml:exterior>
                                <gml:LinearRing gml:id="ring-dem-textured">
                                  <gml:posList>35.0001 139.0040 4 35.0099 139.0040 7 35.0099 139.0120 8 35.0001 139.0040 4</gml:posList>
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
        File.WriteAllText(Path.Combine(packageDirectory, "appearance", "mixed_surface.png"), "");
    }

    private sealed class StubSceneBuilder : IResoniteSceneBuilder
    {
        public List<ResoniteConstructionCityObject> CityObjects { get; } = [];

        public Task EnsureConnectedAsync(
            PlateauImportRequest request,
            CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task BeginAsync(
            ResoniteConstructionMetadata metadata,
            string workRoot,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public Task PrepareCommonMaterialAsync(
            ResoniteMaterialBinding material,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public Task ProcessCityObjectAsync(
            ResoniteConstructionCityObject cityObject,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CityObjects.Add(cityObject);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<string>> CompleteAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<string>>(["stub://resonite"]);
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }
}
