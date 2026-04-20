using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;

using Plateau.ResoniteLink.Application.Importing;
using Plateau.ResoniteLink.Domain.Importing;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Plateau.ResoniteLink.Tests.Application;

public sealed class LocalCityGmlObjectProjectionTests
{
    private static readonly HttpClient SharedDatasetSourceResolverHttpClient = new();

    private static PlateauImportService CreateService(ISceneImportTarget sceneBuilder)
    {
        return new PlateauImportService(
            sceneBuilder,
            new CkanPlateauDatasetSourceResolver(SharedDatasetSourceResolverHttpClient),
            new LocalCityGmlDocumentReader(),
            constructionSourceFactory: new LocalCityGmlConstructionSourceFactory(
                new LocalCityGmlDocumentReader(),
                new LocalCityGmlConstructionComposer(
                    new LocalCityGmlGeometryProjector(new DefaultMaterialResolver()),
                    new LocalCityGmlCommonMaterialEnumerator(new DefaultMaterialResolver()))));
    }

    [Fact]
    [SuppressMessage(
        "Performance",
        "CA1849:Call async methods when in an async method",
        Justification = "This test intentionally compares the sync wrapper against the async entrypoint.")]
    public async Task ConstructionSourceFactoryCreateAsyncMatchesExplicitDocumentSetPathForCanonicalBootstrap()
    {
        string fixturePath = TestData.GetFixturePath("LocalPlateauDataset");
        PlateauImportRequest request = new(
            Dataset: "tokyo23ku",
            MeshCode: "53394525",
            SourceKind: DatasetSourceKind.Local,
            LocalSourcePath: fixturePath,
            ServerUri: null);

        LocalCityGmlDocumentReader documentReader = new();
        LocalCityGmlDocumentSet documentSet = await documentReader.ReadAsync(request);
        LocalCityGmlConstructionSourceFactory constructionSourceFactory = new(
            documentReader,
            new LocalCityGmlConstructionComposer(
                new LocalCityGmlGeometryProjector(new DefaultMaterialResolver()),
                new LocalCityGmlCommonMaterialEnumerator(new DefaultMaterialResolver())));
        IResoniteConstructionSource asyncSource = await constructionSourceFactory.CreateAsync(request);
        IResoniteConstructionSource explicitDocumentSetSource = await constructionSourceFactory.CreateAsync(request, documentSet);

        Assert.Equal(asyncSource.Metadata.SchemaVersion, explicitDocumentSetSource.Metadata.SchemaVersion);
        Assert.Equal(asyncSource.Metadata.WorldName, explicitDocumentSetSource.Metadata.WorldName);
        Assert.Same(request, asyncSource.Metadata.Request);
        Assert.Same(request, explicitDocumentSetSource.Metadata.Request);
        Assert.Equal(asyncSource.Metadata.SourceDataset.PackageNames, explicitDocumentSetSource.Metadata.SourceDataset.PackageNames);
        Assert.Equal(asyncSource.Metadata.SourceDataset.SourceFiles, explicitDocumentSetSource.Metadata.SourceDataset.SourceFiles);
        Assert.Equal(asyncSource.Metadata.SourceDataset.TerrainTextureOverlays, explicitDocumentSetSource.Metadata.SourceDataset.TerrainTextureOverlays);
        Assert.Equal(asyncSource.Metadata.SourceDataset.RequestedMeshCodes, explicitDocumentSetSource.Metadata.SourceDataset.RequestedMeshCodes);
        Assert.Equal(asyncSource.Metadata.LocalOrigin, explicitDocumentSetSource.Metadata.LocalOrigin);
    }

    [Fact]
    public async Task SplitParsedCityObjectPreservesNonGeneratedDemSurfacesWhenOverlaysSplit()
    {
        using TemporaryDirectory datasetRoot = new();
        CreateRuntimeMixedSurfaceDemFixture(datasetRoot.Path);

        await using StubSceneBuilder sceneBuilder = new();
        PlateauImportService service = CreateService(sceneBuilder);

        await service.ExecuteAsync(
            new PlateauImportRequest(
                Dataset: "tokyo23ku",
                MeshCode: "53394525",
                SourceKind: DatasetSourceKind.Local,
                LocalSourcePath: datasetRoot.Path,
                PackageNames: ["dem"],
                ServerUri: null),
            workRoot: "runtime/resonite");

        ImportedCityObject[] demCityObjects = sceneBuilder.CityObjects
            .Where(static cityObject => cityObject.PackageName == "dem")
            .ToArray();

        Assert.Equal(2, demCityObjects.Length);

        ImportedCityObject generatedChunk = Assert.Single(
            demCityObjects,
            static cityObject => cityObject.Materials.Any(static material => material.TerrainOverlay is not null));
        ImportedCityObject explicitChunk = Assert.Single(
            demCityObjects,
            static cityObject => cityObject.Materials.Any(static material => material.TexturePayload is not null));

        Assert.Equal("dem", generatedChunk.PackageName);
        Assert.Equal("dem", explicitChunk.PackageName);

        MaterialBinding generatedMaterial = Assert.Single(generatedChunk.Materials);
        Assert.Equal(TextureSourceKind.Dataset, generatedMaterial.TextureSourceKind);
        Assert.NotNull(generatedMaterial.TerrainOverlay);
        Assert.Null(generatedMaterial.TexturePayload);
        Assert.Single(generatedChunk.Mesh.Submeshes);
        Assert.InRange(generatedChunk.Mesh.Vertices.Count, 3, 9);

        MaterialBinding explicitMaterial = Assert.Single(explicitChunk.Materials);
        Assert.NotNull(explicitMaterial.TexturePayload);
        Assert.Contains(
            "udx/dem/53394525/appearance/mixed_surface.png",
            explicitMaterial.TexturePayload!.Identity,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task DemMeshModeNormalizesGeneratedUvPerChunkWithoutRelyingOnMaterialTextureTransform()
    {
        using TemporaryDirectory datasetRoot = new();
        CreateRuntimeDemChunkFixture(datasetRoot.Path);

        await using StubSceneBuilder sceneBuilder = new();
        PlateauImportService service = CreateService(sceneBuilder);

        await service.ExecuteAsync(
            new PlateauImportRequest(
                Dataset: "tokyo23ku",
                MeshCode: "53394525",
                SourceKind: DatasetSourceKind.Local,
                LocalSourcePath: datasetRoot.Path,
                PackageNames: ["dem"],
                ServerUri: null),
            workRoot: "runtime/resonite");

        ImportedCityObject demCityObject = Assert.Single(
            sceneBuilder.CityObjects,
            static cityObject => cityObject.PackageName == "dem");
        Assert.Equal("dem", demCityObject.PackageName);

        MaterialBinding material = Assert.Single(demCityObject.Materials);
        Assert.NotNull(material.TerrainOverlay);
        Assert.Null(material.TexturePayload);

        double minU = demCityObject.Mesh.Vertices.Min(static vertex => vertex.UV0.X);
        double maxU = demCityObject.Mesh.Vertices.Max(static vertex => vertex.UV0.X);
        double minV = demCityObject.Mesh.Vertices.Min(static vertex => vertex.UV0.Y);
        double maxV = demCityObject.Mesh.Vertices.Max(static vertex => vertex.UV0.Y);
        Assert.True(maxU - minU > 0.79);
        Assert.True(maxV - minV > 0.49);
        Assert.InRange(minU, 0.0, 0.1);
        Assert.InRange(minV, 0.0, 0.11);
        Assert.InRange(maxU, 0.89, 0.91);
        Assert.InRange(maxV, 0.89, 0.91);
        Assert.Single(demCityObject.Mesh.Submeshes);
        Assert.InRange(demCityObject.Mesh.Vertices.Count, 4, 6);
    }

    [Fact]
    public void MaterializeParsedCityObjectWithoutOverlayKeepsGeneratedDemSurfaceUntextured()
    {
        LocalCityGmlObjectProjection.GeodeticPoint[] vertices =
        [
            new(35.6669, 139.7202, 5.0),
            new(35.6698, 139.7202, 10.0),
            new(35.6698, 139.7208, 12.0),
        ];
        LocalCityGmlObjectProjection.ParsedSurface surface = new(
            PolygonId: "dem-uncovered",
            Semantic: LocalCityGmlObjectProjection.ParsedSurfaceSemantic.Ground,
            ExteriorRing: new LocalCityGmlObjectProjection.ParsedRing(
                "ring-dem-uncovered",
                vertices,
                UVs: null),
            InteriorRings: [],
            BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
            TexturePayload: null,
            UsesGeneratedDemTexture: true);
        LocalCityGmlObjectProjection.ParsedCityObject cityObject = new(
            SlotKey: "dem-object",
            DisplayName: "DEM Object",
            PackageName: "dem",
            ActualMeshCode: "53394525",
            LodLevel: 1,
            Surfaces: [surface],
            ReferenceSystem: LocalCityGmlObjectProjection.CoordinateReferenceSystem.Parse("EPSG:4326"),
            SourceFileRelativePath: "udx/dem/53394525/uncovered.gml",
            SourceUnitIdentity: "source-unit",
            SourceIdentity: "source",
            SharedAcrossMeshCodes: false);
        PlateauImportRequest request = new(
            Dataset: "tokyo23ku",
            MeshCode: "53394525",
            SourceKind: DatasetSourceKind.Local,
            LocalSourcePath: "fixture",
            ServerUri: null);

        ResoniteConstructionCityObject materialized = Assert.Single(
            LocalCityGmlObjectProjection.MaterializeParsedCityObject(
                cityObject,
                globalOriginPoint: vertices[0],
                globalCartesian: null,
                demTerrainTextureOverlays: [],
                requestedMeshAreas: [],
                terrainHeightSampler: null,
                request,
                new DefaultMaterialResolver()));

        ResoniteMaterialBinding material = Assert.Single(materialized.Materials);
        Assert.Null(material.TerrainOverlay);
        Assert.Null(material.TexturePayload);
        Assert.Equal(ResoniteTextureSourceKind.Dataset, material.TextureSourceKind);
    }

    [Fact]
    public void MaterializeParsedCityObjectClipsGeneratedDemToRequestedMeshCoverageBeforeOverlayAssignment()
    {
        LocalCityGmlObjectProjection.GeodeticPoint[] vertices =
        [
            new(35.0000, 139.0000, 5.0),
            new(35.0100, 139.0000, 10.0),
            new(35.0100, 139.0120, 12.0),
        ];
        LocalCityGmlObjectProjection.ParsedSurface surface = new(
            PolygonId: "dem-crossing",
            Semantic: LocalCityGmlObjectProjection.ParsedSurfaceSemantic.Ground,
            ExteriorRing: new LocalCityGmlObjectProjection.ParsedRing(
                "ring-dem-crossing",
                vertices,
                UVs: null),
            InteriorRings: [],
            BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
            TexturePayload: null,
            UsesGeneratedDemTexture: true);
        LocalCityGmlObjectProjection.ParsedCityObject cityObject = new(
            SlotKey: "dem-object",
            DisplayName: "DEM Object",
            PackageName: "dem",
            ActualMeshCode: "533945",
            LodLevel: 1,
            Surfaces: [surface],
            ReferenceSystem: LocalCityGmlObjectProjection.CoordinateReferenceSystem.Parse("EPSG:4326"),
            SourceFileRelativePath: "udx/dem/533945_dem_0000_op.gml",
            SourceUnitIdentity: "source-unit",
            SourceIdentity: "source",
            SharedAcrossMeshCodes: true);
        TerrainTextureOverlay overlay = new(
            PackageName: "dem",
            UrlTemplate: "https://tiles.example/{z}/{x}/{y}.png",
            ZoomLevel: 18,
            GeographicBounds: new GeographicRectangle(
                MinLatitude: 35.0000,
                MaxLatitude: 35.0200,
                MinLongitude: 139.0000,
                MaxLongitude: 139.0200),
            MaxTextureSize: LocalCityGmlObjectProjection.DefaultDemTerrainTextureMaxSize);
        MeshCodeBounds requestedMeshBounds = new(35.0000, 35.0200, 139.0000, 139.0100);
        PlateauImportRequest request = new(
            Dataset: "tokyo23ku",
            MeshCode: "53394525",
            SourceKind: DatasetSourceKind.Local,
            LocalSourcePath: "fixture",
            ServerUri: null);

        ResoniteConstructionCityObject materialized = Assert.Single(
            LocalCityGmlObjectProjection.MaterializeParsedCityObject(
                cityObject,
                globalOriginPoint: vertices[0],
                globalCartesian: null,
                demTerrainTextureOverlays: [overlay],
                requestedMeshAreas: [requestedMeshBounds],
                terrainHeightSampler: null,
                request,
                new DefaultMaterialResolver()));

        ResoniteMaterialBinding material = Assert.Single(materialized.Materials);
        Assert.NotNull(material.TerrainOverlay);
        Assert.Null(material.TexturePayload);
        Assert.Single(materialized.Mesh.Submeshes);
        Assert.NotEmpty(materialized.Mesh.Vertices);
        Assert.Null(material.TextureScale);
        Assert.Null(material.TextureOffset);
        double minU = materialized.Mesh.Vertices.Min(static vertex => vertex.UV0.X);
        double maxU = materialized.Mesh.Vertices.Max(static vertex => vertex.UV0.X);
        Assert.InRange(minU, 0.0, 0.01);
        Assert.InRange(maxU, 0.49, 0.51);
    }

    [Fact]
    public async Task DemHeightMapModeExtendsBoundaryConnectedMissingSamplesWithoutSeaLevelDrop()
    {
        using TemporaryDirectory datasetRoot = new();
        CreateRuntimeDemChunkFixture(datasetRoot.Path);

        await using StubSceneBuilder sceneBuilder = new();
        PlateauImportService service = CreateService(sceneBuilder);

        await service.ExecuteAsync(
            new PlateauImportRequest(
                Dataset: "tokyo23ku",
                MeshCode: "53394525",
                SourceKind: DatasetSourceKind.Local,
                LocalSourcePath: datasetRoot.Path,
                PackageNames: ["dem"],
                DemTerrainMode: DemTerrainMode.HeightMap,
                ServerUri: null),
            workRoot: "runtime/resonite");

        ImportedCityObject demCityObject = Assert.Single(
            sceneBuilder.CityObjects,
            static cityObject => cityObject.PackageName == "dem");
        HeightMapGridGeometry geometry = Assert.IsType<HeightMapGridGeometry>(demCityObject.Geometry);

        double[] topEdge = Enumerable.Range(0, geometry.Width)
            .Select(index => geometry.HeightSamples[index])
            .ToArray();
        double[] bottomEdge = Enumerable.Range(0, geometry.Width)
            .Select(index => geometry.HeightSamples[((geometry.Height - 1) * geometry.Width) + index])
            .ToArray();
        double[] leftEdge = Enumerable.Range(0, geometry.Height)
            .Select(index => geometry.HeightSamples[index * geometry.Width])
            .ToArray();
        double[] rightEdge = Enumerable.Range(0, geometry.Height)
            .Select(index => geometry.HeightSamples[(index * geometry.Width) + (geometry.Width - 1)])
            .ToArray();

        Assert.True(bottomEdge.Min() > -1.0, $"Bottom edge dropped to sea-level fallback: min={bottomEdge.Min():F6}");
        Assert.True(leftEdge.Min() > -1.0, $"Left edge dropped to sea-level fallback: min={leftEdge.Min():F6}");
        Assert.True(rightEdge.Min() > -1.0, $"Right edge dropped to sea-level fallback: min={rightEdge.Min():F6}");
        Assert.True(topEdge.Min() > -1.0, $"Top edge dropped to sea-level fallback: min={topEdge.Min():F6}");
    }

    [Fact]
    public async Task DemExactMeshRequestFiltersSplitParentMeshPiecesAfterOverlaySplit()
    {
        using TemporaryDirectory datasetRoot = new();
        CreateRuntimeParentMeshDemFixture(datasetRoot.Path, "53394525", "53394526");

        await using StubSceneBuilder sceneBuilder = new();
        PlateauImportService service = CreateService(sceneBuilder);

        await service.ExecuteAsync(
            new PlateauImportRequest(
                Dataset: "tokyo23ku",
                MeshCode: "53394525",
                SourceKind: DatasetSourceKind.Local,
                LocalSourcePath: datasetRoot.Path,
                PackageNames: ["dem"],
                DemTerrainMode: DemTerrainMode.HeightMap,
                ServerUri: null),
            workRoot: "runtime/resonite");

        ImportedCityObject demCityObject = Assert.Single(
            sceneBuilder.CityObjects,
            static cityObject => cityObject.PackageName == "dem");
        HeightMapGridGeometry geometry = Assert.IsType<HeightMapGridGeometry>(demCityObject.Geometry);
        Assert.True(geometry.Width > 0);
        Assert.True(geometry.Height > 0);
    }

    [Fact]
    public async Task DemExactMeshRequestPrefersConcreteMeshCodeNamedParentDemObjects()
    {
        using TemporaryDirectory datasetRoot = new();
        CreateRuntimeNamedParentMeshDemFixture(datasetRoot.Path, "53394525", "53394526");

        await using StubSceneBuilder sceneBuilder = new();
        PlateauImportService service = CreateService(sceneBuilder);

        await service.ExecuteAsync(
            new PlateauImportRequest(
                Dataset: "tokyo23ku",
                MeshCode: "53394525",
                SourceKind: DatasetSourceKind.Local,
                LocalSourcePath: datasetRoot.Path,
                PackageNames: ["dem"],
                DemTerrainMode: DemTerrainMode.HeightMap,
                ServerUri: null),
            workRoot: "runtime/resonite");

        ImportedCityObject demCityObject = Assert.Single(
            sceneBuilder.CityObjects,
            static cityObject => cityObject.PackageName == "dem"
                && !cityObject.DisplayName.EndsWith("Skirt", StringComparison.Ordinal));
        Assert.Equal("53394525", demCityObject.DisplayName);
    }

    [Fact]
    public void AlignAdjacentDemHeightMapChunkBoundariesAveragesSharedSamplesForPartialOverlapWithDifferentResolution()
    {
        ResoniteConstructionCityObject left = CreateHeightMapCityObject(
            "left-dem",
            new ResoniteFloat3(0.0, 14.0, 0.0),
            width: 2,
            height: 5,
            sizeX: 2.0,
            sizeZ: 4.0,
            [
                1.0, 10.0,
                1.0, 11.0,
                1.0, 12.0,
                1.0, 13.0,
                1.0, 14.0,
            ]);
        ResoniteConstructionCityObject right = CreateHeightMapCityObject(
            "right-dem",
            new ResoniteFloat3(2.0, 22.0, 0.0),
            width: 2,
            height: 3,
            sizeX: 2.0,
            sizeZ: 2.0,
            [
                20.0, 2.0,
                21.0, 2.0,
                22.0, 2.0,
            ]);

        ResoniteConstructionCityObject[] aligned = AlignAdjacentDemHeightMapChunkBoundariesForTest([left, right]);

        ResoniteHeightMapGridGeometry alignedLeft = Assert.IsType<ResoniteHeightMapGridGeometry>(aligned[0].Geometry);
        ResoniteHeightMapGridGeometry alignedRight = Assert.IsType<ResoniteHeightMapGridGeometry>(aligned[1].Geometry);

        Assert.Equal(10.0, alignedLeft.HeightSamples[1], 6);
        Assert.Equal(15.5, alignedLeft.HeightSamples[3], 6);
        Assert.Equal(16.5, alignedLeft.HeightSamples[5], 6);
        Assert.Equal(17.5, alignedLeft.HeightSamples[7], 6);
        Assert.Equal(14.0, alignedLeft.HeightSamples[9], 6);

        Assert.Equal(15.5, alignedRight.HeightSamples[0], 6);
        Assert.Equal(16.5, alignedRight.HeightSamples[2], 6);
        Assert.Equal(17.5, alignedRight.HeightSamples[4], 6);
    }

    [Fact]
    public async Task TerrainAlignedObjectDoesNotUseNearestTerrainPointOutsideDemTriangles()
    {
        using TemporaryDirectory datasetRoot = new();
        CreateRuntimeDemAndLandUseGapFixture(datasetRoot.Path);

        await using StubSceneBuilder sceneBuilder = new();
        PlateauImportService service = CreateService(sceneBuilder);

        await service.ExecuteAsync(
            new PlateauImportRequest(
                Dataset: "tokyo23ku",
                MeshCode: "53394525",
                SourceKind: DatasetSourceKind.Local,
                LocalSourcePath: datasetRoot.Path,
                PackageNames: ["dem", "luse"],
                ServerUri: null),
            workRoot: "runtime/resonite");

        ImportedCityObject landUse = Assert.Single(
            sceneBuilder.CityObjects,
            static cityObject => cityObject.PackageName == "luse");

        Assert.True(
            landUse.Transform.Position.Y > 40.0,
            $"Land-use object was incorrectly snapped toward nearby DEM fallback height: y={landUse.Transform.Position.Y:F6}");
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
                  <gml:lowerCorner>35.6840 139.6880 0</gml:lowerCorner>
                  <gml:upperCorner>35.6910 139.6995 20</gml:upperCorner>
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
                                  <gml:posList>35.6840 139.6880 5 35.6910 139.6880 10 35.6910 139.6935 12 35.6840 139.6880 5</gml:posList>
                                </gml:LinearRing>
                              </gml:exterior>
                            </gml:Triangle>
                            <gml:Triangle gml:id="tri-dem-east">
                              <gml:exterior>
                                <gml:LinearRing gml:id="ring-dem-east">
                                  <gml:posList>35.6842 139.6945 6 35.6910 139.6945 8 35.6910 139.6995 14 35.6842 139.6945 6</gml:posList>
                                </gml:LinearRing>
                              </gml:exterior>
                            </gml:Triangle>
                            <gml:Triangle gml:id="tri-dem-textured">
                              <gml:exterior>
                                <gml:LinearRing gml:id="ring-dem-textured">
                                  <gml:posList>35.6841 139.6895 4 35.6909 139.6895 7 35.6909 139.6925 8 35.6841 139.6895 4</gml:posList>
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

        string xml =
            """
            <?xml version="1.0" encoding="UTF-8"?>
            <core:CityModel xmlns:core="http://www.opengis.net/citygml/2.0" xmlns:gml="http://www.opengis.net/gml" xmlns:dem="http://www.opengis.net/citygml/relief/2.0">
              <gml:boundedBy>
                <gml:Envelope srsName="http://www.opengis.net/def/crs/EPSG/0/6697" srsDimension="3">
                  <gml:lowerCorner>35.6840 139.6880 0</gml:lowerCorner>
                  <gml:upperCorner>35.6910 139.6995 20</gml:upperCorner>
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
                                  <gml:posList>35.6842 139.6885 5 35.6908 139.6885 10 35.6908 139.6988 12 35.6842 139.6885 5</gml:posList>
                                </gml:LinearRing>
                              </gml:exterior>
                            </gml:Triangle>
                            <gml:Triangle gml:id="tri-dem-b">
                              <gml:exterior>
                                <gml:LinearRing gml:id="ring-dem-b">
                                  <gml:posList>35.6842 139.6885 5 35.6908 139.6988 12 35.6842 139.6988 7 35.6842 139.6885 5</gml:posList>
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

        string demXml =
            """
            <?xml version="1.0" encoding="UTF-8"?>
            <core:CityModel xmlns:core="http://www.opengis.net/citygml/2.0" xmlns:gml="http://www.opengis.net/gml" xmlns:dem="http://www.opengis.net/citygml/relief/2.0">
              <gml:boundedBy>
                <gml:Envelope srsName="http://www.opengis.net/def/crs/EPSG/0/6697" srsDimension="3">
                  <gml:lowerCorner>35.6667 139.7000 0</gml:lowerCorner>
                  <gml:upperCorner>35.6699 139.7100 20</gml:upperCorner>
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
                                  <gml:posList>35.6669 139.7008 5 35.6698 139.7008 6 35.6698 139.7040 7 35.6669 139.7008 5</gml:posList>
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
            """
            <?xml version="1.0" encoding="UTF-8"?>
            <core:CityModel xmlns:core="http://www.opengis.net/citygml/2.0" xmlns:gml="http://www.opengis.net/gml" xmlns:luse="http://www.opengis.net/citygml/landuse/2.0">
              <gml:boundedBy>
                <gml:Envelope srsName="http://www.opengis.net/def/crs/EPSG/0/6697" srsDimension="3">
                  <gml:lowerCorner>35.6667 139.7000 0</gml:lowerCorner>
                  <gml:upperCorner>35.6699 139.7100 60</gml:upperCorner>
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
                              <gml:posList>35.6672 139.7060 50 35.6692 139.7060 50 35.6692 139.7085 50 35.6672 139.7085 50 35.6672 139.7060 50</gml:posList>
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
        string packageDirectory = Path.Combine(datasetRoot, "udx", "dem", requestedMeshCode[..6]);
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

        File.WriteAllText(Path.Combine(packageDirectory, $"plateau_tokyo23ku_dem_{requestedMeshCode[..6]}_parent.gml"), xml);
    }

    private static void CreateRuntimeNamedParentMeshDemFixture(string datasetRoot, string requestedMeshCode, string adjacentMeshCode)
    {
        string packageDirectory = Path.Combine(datasetRoot, "udx", "dem", requestedMeshCode[..6]);
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

        File.WriteAllText(Path.Combine(packageDirectory, $"plateau_tokyo23ku_dem_{requestedMeshCode[..6]}_named-parent.gml"), xml);
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

    private static ResoniteConstructionCityObject[] AlignAdjacentDemHeightMapChunkBoundariesForTest(
        IReadOnlyList<ResoniteConstructionCityObject> cityObjects)
    {
        MethodInfo method = typeof(LocalCityGmlObjectProjection)
            .GetMethod(
                "AlignAdjacentDemHeightMapChunkBoundaries",
                BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Failed to resolve AlignAdjacentDemHeightMapChunkBoundaries.");

        return (ResoniteConstructionCityObject[])method.Invoke(null, [cityObjects])!;
    }

    private static ResoniteConstructionCityObject CreateHeightMapCityObject(
        string slotKey,
        ResoniteFloat3 position,
        int width,
        int height,
        double sizeX,
        double sizeZ,
        IReadOnlyList<double> heightSamples)
    {
        ResoniteMaterialBinding material = new(
            MaterialKey: $"{slotKey}-material",
            BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
            MaterialType: ResoniteMaterialType.Standard,
            TexturePayload: null,
            TextureSourceKind: ResoniteTextureSourceKind.Bundled,
            Projection: ResoniteMaterialProjection.Uv,
            DepthOffset: null,
            SubmeshIndices: [0]);
        return new ResoniteConstructionCityObject(
            SlotKey: slotKey,
            DisplayName: slotKey,
            PackageName: "dem",
            ActualMeshCode: "53394525",
            LodLevel: 1,
            Transform: new ResoniteTransform(position),
            Geometry: new ResoniteHeightMapGridGeometry(
                Width: width,
                Height: height,
                Size: new ResoniteFloat2(sizeX, sizeZ),
                MinHeight: heightSamples.Min(),
                MaxHeight: heightSamples.Max(),
                HeightSamples: heightSamples),
            Materials: [material],
            SourceObjectKey: slotKey,
            SourceUnitKey: slotKey,
            SourceFileRelativePath: $"udx/dem/53394525/{slotKey}.gml");
    }
    private sealed class StubSceneBuilder : ISceneImportTarget
    {
        public List<ImportedCityObject> CityObjects { get; } = [];

        public async Task<SceneImportExecutionResult> ExecuteAsync(
            SceneImportExecutionPlan plan,
            IAsyncEnumerable<ImportedCityObject> cityObjects,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _ = plan;
            await foreach (ImportedCityObject cityObject in cityObjects.WithCancellation(cancellationToken))
            {
                CityObjects.Add(cityObject);
            }

            return new SceneImportExecutionResult(["stub://resonite"], CityObjects.Count);
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }
}
