using System.Diagnostics.CodeAnalysis;
using System.Globalization;

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

        IResoniteConstructionSource asyncSource = await PlateauCityGmlConstructionSources.CreateAsync(request);
        IResoniteConstructionSource syncSource = PlateauCityGmlConstructionSources.Create(request);

        Assert.Equal(asyncSource.Metadata.SchemaVersion, syncSource.Metadata.SchemaVersion);
        Assert.Equal(asyncSource.Metadata.WorldName, syncSource.Metadata.WorldName);
        Assert.Same(request, asyncSource.Metadata.Request);
        Assert.Same(request, syncSource.Metadata.Request);
        Assert.Equal(asyncSource.Metadata.SourceDataset.PackageNames, syncSource.Metadata.SourceDataset.PackageNames);
        Assert.Equal(asyncSource.Metadata.SourceDataset.SourceFiles, syncSource.Metadata.SourceDataset.SourceFiles);
        Assert.Equal(asyncSource.Metadata.SourceDataset.TerrainTextureOverlays, syncSource.Metadata.SourceDataset.TerrainTextureOverlays);
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
        Assert.True(maxU - minU > 0.9);
        Assert.True(maxV - minV > 0.9);
        Assert.InRange(minU, 0.0, 0.1);
        Assert.InRange(minV, 0.0, 0.1);
        Assert.InRange(maxU, 0.9, 1.0);
        Assert.InRange(maxV, 0.9, 1.0);
        Assert.Single(demCityObject.Mesh.Submeshes);
        Assert.InRange(demCityObject.Mesh.Vertices.Count, 4, 6);
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
            static cityObject => cityObject.PackageName == "dem");
        Assert.Equal("53394525", demCityObject.DisplayName);
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
                  <gml:lowerCorner>35.6667 139.7000 0</gml:lowerCorner>
                  <gml:upperCorner>35.6699 139.7100 20</gml:upperCorner>
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
                                  <gml:posList>35.6669 139.7008 5 35.6698 139.7008 10 35.6698 139.7098 12 35.6669 139.7008 5</gml:posList>
                                </gml:LinearRing>
                              </gml:exterior>
                            </gml:Triangle>
                            <gml:Triangle gml:id="tri-dem-b">
                              <gml:exterior>
                                <gml:LinearRing gml:id="ring-dem-b">
                                  <gml:posList>35.6669 139.7008 5 35.6698 139.7098 12 35.6669 139.7098 7 35.6669 139.7008 5</gml:posList>
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
