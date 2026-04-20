using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Tests.Targets;

public sealed class FixedCellCityObjectMeshBakerTests
{
    [Fact]
    public void TryBufferBakesLod1BuildingsIntoSingleMeshWithinSameScope()
    {
        FixedCellCityObjectMeshBaker baker = new(cellSizeMeters: 64.0, maxCityObjectsPerBatch: 10, maxVerticesPerBatch: 1000);
        ResoniteConstructionCityObject first = CreateTriangleBuilding("first", x: 10.0, z: 12.0, sourceUnitKey: "unit-a", sourceFileRelativePath: null);
        ResoniteConstructionCityObject second = CreateTriangleBuilding("second", x: 18.0, z: 20.0, sourceUnitKey: "unit-a", sourceFileRelativePath: null);

        Assert.True(baker.TryBuffer(first, out ResoniteConstructionCityObject? firstBaked));
        Assert.True(baker.TryBuffer(second, out ResoniteConstructionCityObject? secondBaked));

        Assert.Null(firstBaked);
        Assert.Null(secondBaked);
        ResoniteConstructionCityObject baked = Assert.Single(baker.FlushAll());
        Assert.Equal("bldg", baked.PackageName);
        Assert.Equal(1, baked.LodLevel);
        Assert.Equal(6, baked.Mesh.Vertices.Count);
        Assert.Single(baked.Mesh.Submeshes);
        Assert.Single(baked.Materials);
        Assert.Equal([0], baked.Materials[0].SubmeshIndices);
        Assert.Equal(new ResoniteFloat3(10.0, 0.0, 12.0), baked.Transform.Position);
    }

    [Fact]
    public void FlushAllSeparatesSameSourceUnitAcrossSourceFiles()
    {
        FixedCellCityObjectMeshBaker baker = new(cellSizeMeters: 64.0, maxCityObjectsPerBatch: 10, maxVerticesPerBatch: 1000);
        Assert.True(baker.TryBuffer(CreateTriangleBuilding("left", x: 10.0, z: 10.0, sourceUnitKey: "shared-unit", sourceFileRelativePath: "a.gml"), out _));
        Assert.True(baker.TryBuffer(CreateTriangleBuilding("right", x: 80.0, z: 10.0, sourceUnitKey: "shared-unit", sourceFileRelativePath: "b.gml"), out _));

        IReadOnlyList<ResoniteConstructionCityObject> baked = baker.FlushAll();

        Assert.Equal(2, baked.Count);
        Assert.Contains(baked, static cityObject => cityObject.SourceFileRelativePath == "a.gml");
        Assert.Contains(baked, static cityObject => cityObject.SourceFileRelativePath == "b.gml");
    }

    [Fact]
    public void FlushAllKeepsSameSourceFileMergedAcrossDifferentSourceUnits()
    {
        FixedCellCityObjectMeshBaker baker = new(cellSizeMeters: 64.0, maxCityObjectsPerBatch: 10, maxVerticesPerBatch: 1000);
        Assert.True(baker.TryBuffer(CreateTriangleBuilding("a-one", x: 10.0, z: 10.0, sourceUnitKey: "unit-a", sourceFileRelativePath: "common.gml"), out _));
        Assert.True(baker.TryBuffer(CreateTriangleBuilding("a-two", x: 18.0, z: 12.0, sourceUnitKey: "unit-b", sourceFileRelativePath: "common.gml"), out _));

        ResoniteConstructionCityObject baked = Assert.Single(baker.FlushAll());
        Assert.Equal("common.gml", baked.SourceFileRelativePath);
        Assert.Null(baked.SourceUnitKey);
        Assert.Equal(6, baked.Mesh.Vertices.Count);
    }

    [Fact]
    public async Task TryBufferAsyncEmitsReadyCityObjectWhenBatchLimitIsExceeded()
    {
        FixedCellCityObjectMeshBaker baker = new(cellSizeMeters: 64.0, maxCityObjectsPerBatch: 1, maxVerticesPerBatch: 1000);

        BufferedCityObjectBufferResult first = await baker.TryBufferAsync(CreateTriangleBuilding("first", x: 10.0, z: 10.0, sourceUnitKey: "unit-a", sourceFileRelativePath: null));
        BufferedCityObjectBufferResult second = await baker.TryBufferAsync(CreateTriangleBuilding("second", x: 11.0, z: 10.0, sourceUnitKey: "unit-a", sourceFileRelativePath: null));

        Assert.True(first.Buffered);
        Assert.Empty(first.ReadyCityObjects);
        Assert.True(second.Buffered);
        Assert.Single(second.ReadyCityObjects);
        Assert.Empty(await baker.FlushAllAsync());
    }

    [Fact]
    public async Task TryBufferAsyncFlushesOldestCellWhenBufferedCellLimitIsExceeded()
    {
        FixedCellCityObjectMeshBaker baker = new(
            cellSizeMeters: 64.0,
            maxCityObjectsPerBatch: 10,
            maxVerticesPerBatch: 1000,
            maxBufferedCells: 2);

        BufferedCityObjectBufferResult first = await baker.TryBufferAsync(
            CreateTriangleBuilding("first", x: 10.0, z: 10.0, sourceUnitKey: "unit-a", sourceFileRelativePath: "a.gml"));
        BufferedCityObjectBufferResult second = await baker.TryBufferAsync(
            CreateTriangleBuilding("second", x: 80.0, z: 10.0, sourceUnitKey: "unit-b", sourceFileRelativePath: "b.gml"));
        BufferedCityObjectBufferResult third = await baker.TryBufferAsync(
            CreateTriangleBuilding("third", x: 150.0, z: 10.0, sourceUnitKey: "unit-c", sourceFileRelativePath: "c.gml"));

        Assert.True(first.Buffered);
        Assert.True(second.Buffered);
        Assert.True(third.Buffered);
        ResoniteConstructionCityObject evicted = Assert.Single(third.ReadyCityObjects);
        Assert.Equal("a.gml", evicted.SourceFileRelativePath);

        IReadOnlyList<ResoniteConstructionCityObject> remaining = await baker.FlushAllAsync();
        Assert.Equal(2, remaining.Count);
        Assert.Contains(remaining, static cityObject => cityObject.SourceFileRelativePath == "b.gml");
        Assert.Contains(remaining, static cityObject => cityObject.SourceFileRelativePath == "c.gml");
    }

    [Fact]
    public void TryBufferSkipsNonTargetCityObjects()
    {
        FixedCellCityObjectMeshBaker baker = new(cellSizeMeters: 64.0, maxCityObjectsPerBatch: 2, maxVerticesPerBatch: 1000);
        ResoniteConstructionCityObject lod2Building = CreateTriangleBuilding("lod2", x: 10.0, z: 10.0, sourceUnitKey: "unit-a", sourceFileRelativePath: null) with { LodLevel = 2 };

        bool buffered = baker.TryBuffer(lod2Building, out ResoniteConstructionCityObject? baked);

        Assert.False(buffered);
        Assert.Null(baked);
        Assert.Empty(baker.FlushAll());
    }

    [Fact]
    public void FlushAllRejectsBufferedMeshBakeWhenSubmeshMaterialAssignmentIsMissing()
    {
        FixedCellCityObjectMeshBaker baker = new(cellSizeMeters: 64.0, maxCityObjectsPerBatch: 10, maxVerticesPerBatch: 1000);
        ResoniteConstructionCityObject invalid = new(
            SlotKey: "invalid",
            DisplayName: "invalid",
            PackageName: "bldg",
            ActualMeshCode: "53394525",
            LodLevel: 1,
            Transform: new ResoniteTransform(new ResoniteFloat3(10.0, 0.0, 10.0)),
            Mesh: new ResoniteImportedMesh(
                [
                    new ResoniteMeshVertex(new ResoniteFloat3(0.0, 0.0, 0.0), new ResoniteFloat3(0.0, 1.0, 0.0), new ResoniteFloat2(0.0, 0.0)),
                    new ResoniteMeshVertex(new ResoniteFloat3(1.0, 0.0, 0.0), new ResoniteFloat3(0.0, 1.0, 0.0), new ResoniteFloat2(1.0, 0.0)),
                    new ResoniteMeshVertex(new ResoniteFloat3(0.0, 0.0, 1.0), new ResoniteFloat3(0.0, 1.0, 0.0), new ResoniteFloat2(0.0, 1.0)),
                    new ResoniteMeshVertex(new ResoniteFloat3(1.0, 0.0, 1.0), new ResoniteFloat3(0.0, 1.0, 0.0), new ResoniteFloat2(1.0, 1.0)),
                ],
                [
                    new ResoniteMeshSubmesh(0, "shared-material", [0, 1, 2]),
                    new ResoniteMeshSubmesh(1, "missing-material", [1, 3, 2]),
                ]),
            Materials:
            [
                new ResoniteMaterialBinding(
                    MaterialKey: "shared-material",
                    BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
                    MaterialType: ResoniteMaterialType.Standard,
                    TexturePayload: null,
                    TextureSourceKind: ResoniteTextureSourceKind.Bundled,
                    Projection: ResoniteMaterialProjection.Uv,
                    DepthOffset: null,
                    SubmeshIndices: [0]),
            ],
            SourceObjectKey: "unit-a:invalid",
            SourceUnitKey: "unit-a",
            SourceFileRelativePath: null);

        Assert.True(baker.TryBuffer(invalid, out _));

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(baker.FlushAll);
        Assert.Contains("left submesh index 1 without a material assignment", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FlushAllProducesDeterministicMeshForEquivalentBufferedOrderings()
    {
        FixedCellCityObjectMeshBaker forward = new(cellSizeMeters: 64.0, maxCityObjectsPerBatch: 10, maxVerticesPerBatch: 1000);
        FixedCellCityObjectMeshBaker reverse = new(cellSizeMeters: 64.0, maxCityObjectsPerBatch: 10, maxVerticesPerBatch: 1000);
        ResoniteConstructionCityObject first = CreateTriangleBuilding("first", x: 10.0, z: 12.0, sourceUnitKey: "unit-a", sourceFileRelativePath: "common.gml");
        ResoniteConstructionCityObject second = CreateTriangleBuilding("second", x: 18.0, z: 20.0, sourceUnitKey: "unit-b", sourceFileRelativePath: "common.gml");

        Assert.True(forward.TryBuffer(first, out _));
        Assert.True(forward.TryBuffer(second, out _));
        Assert.True(reverse.TryBuffer(second, out _));
        Assert.True(reverse.TryBuffer(first, out _));

        ResoniteConstructionCityObject bakedForward = Assert.Single(forward.FlushAll());
        ResoniteConstructionCityObject bakedReverse = Assert.Single(reverse.FlushAll());
        Assert.Equal(bakedForward.Mesh.Vertices, bakedReverse.Mesh.Vertices);
        Assert.Equal(bakedForward.Mesh.Submeshes.Count, bakedReverse.Mesh.Submeshes.Count);
        for (int submeshIndex = 0; submeshIndex < bakedForward.Mesh.Submeshes.Count; submeshIndex++)
        {
            Assert.Equal(
                bakedForward.Mesh.Submeshes[submeshIndex].TriangleVertexIndices,
                bakedReverse.Mesh.Submeshes[submeshIndex].TriangleVertexIndices);
        }
        Assert.Equal(bakedForward.Materials.Count, bakedReverse.Materials.Count);
        for (int materialIndex = 0; materialIndex < bakedForward.Materials.Count; materialIndex++)
        {
            Assert.Equal(bakedForward.Materials[materialIndex].MaterialKey, bakedReverse.Materials[materialIndex].MaterialKey);
            Assert.Equal(bakedForward.Materials[materialIndex].SubmeshIndices, bakedReverse.Materials[materialIndex].SubmeshIndices);
        }
    }

    [Fact]
    public void FlushAllPreservesConcatenatedVertexAndIndexTopology()
    {
        FixedCellCityObjectMeshBaker baker = new(cellSizeMeters: 64.0, maxCityObjectsPerBatch: 10, maxVerticesPerBatch: 1000);
        Assert.True(baker.TryBuffer(CreateTriangleBuilding("first", x: 10.0, z: 12.0, sourceUnitKey: "unit-a", sourceFileRelativePath: "common.gml"), out _));
        Assert.True(baker.TryBuffer(CreateTriangleBuilding("second", x: 18.0, z: 20.0, sourceUnitKey: "unit-b", sourceFileRelativePath: "common.gml"), out _));

        ResoniteConstructionCityObject baked = Assert.Single(baker.FlushAll());

        Assert.Equal(6, baked.Mesh.Vertices.Count);
        Assert.Single(baked.Mesh.Submeshes);
        Assert.Equal([0, 1, 2, 3, 4, 5], baked.Mesh.Submeshes[0].TriangleVertexIndices);
    }

    [Fact]
    public void FlushAllPreservesSourceWorldBounds()
    {
        FixedCellCityObjectMeshBaker baker = new(cellSizeMeters: 64.0, maxCityObjectsPerBatch: 10, maxVerticesPerBatch: 1000);
        Assert.True(baker.TryBuffer(CreateTriangleBuilding("first", x: 10.0, z: 12.0, sourceUnitKey: "unit-a", sourceFileRelativePath: "common.gml"), out _));
        Assert.True(baker.TryBuffer(CreateTriangleBuilding("second", x: 18.0, z: 20.0, sourceUnitKey: "unit-b", sourceFileRelativePath: "common.gml"), out _));

        ResoniteConstructionCityObject baked = Assert.Single(baker.FlushAll());
        double originX = baked.Transform.Position.X;
        double originZ = baked.Transform.Position.Z;
        double minX = baked.Mesh.Vertices.Min(vertex => vertex.Position.X + originX);
        double minZ = baked.Mesh.Vertices.Min(vertex => vertex.Position.Z + originZ);
        double maxX = baked.Mesh.Vertices.Max(vertex => vertex.Position.X + originX);
        double maxZ = baked.Mesh.Vertices.Max(vertex => vertex.Position.Z + originZ);

        Assert.Equal(10.0, minX, 6);
        Assert.Equal(12.0, minZ, 6);
        Assert.Equal(19.0, maxX, 6);
        Assert.Equal(21.0, maxZ, 6);
    }

    [Fact]
    public void FlushAllBakesDynamicUvTransformIntoMeshAndClearsMaterialTransform()
    {
        FixedCellCityObjectMeshBaker baker = new(cellSizeMeters: 64.0, maxCityObjectsPerBatch: 10, maxVerticesPerBatch: 1000);
        ResoniteMaterialBinding material = new(
            MaterialKey: "dynamic-material",
            BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
            MaterialType: ResoniteMaterialType.Standard,
            TexturePayload: new ResoniteTexturePayload(1, 1, "srgb", [255, 255, 255, 255], "textures/dynamic.png"),
            TextureSourceKind: ResoniteTextureSourceKind.Dataset,
            Projection: ResoniteMaterialProjection.Uv,
            DepthOffset: null,
            SubmeshIndices: [0],
            TextureScale: new ResoniteFloat2(2.0, 0.5),
            TextureOffset: new ResoniteFloat2(0.25, 0.75));
        Assert.True(baker.TryBuffer(CreateTriangleBuilding("dynamic", 10.0, 12.0, "unit-a", null, material), out _));

        ResoniteConstructionCityObject baked = Assert.Single(baker.FlushAll());

        ResoniteMaterialBinding bakedMaterial = Assert.Single(baked.Materials);
        Assert.Null(bakedMaterial.TextureScale);
        Assert.Null(bakedMaterial.TextureOffset);
        Assert.Equal(new ResoniteFloat2(0.25, 0.75), baked.Mesh.Vertices[0].UV0);
        Assert.Equal(new ResoniteFloat2(2.25, 0.75), baked.Mesh.Vertices[1].UV0);
        Assert.Equal(new ResoniteFloat2(0.25, 1.25), baked.Mesh.Vertices[2].UV0);
    }

    [Fact]
    public void FlushAllMergesEquivalentBundledFamilyMaterialsAcrossObjects()
    {
        FixedCellCityObjectMeshBaker baker = new(cellSizeMeters: 64.0, maxCityObjectsPerBatch: 10, maxVerticesPerBatch: 1000);
        Assert.True(baker.TryBuffer(CreateTriangleBuilding("roof-a", 10.0, 12.0, "unit-a", "common.gml", CreateBundledRoofMaterial("roof-a")), out _));
        Assert.True(baker.TryBuffer(CreateTriangleBuilding("roof-b", 18.0, 20.0, "unit-b", "common.gml", CreateBundledRoofMaterial("roof-b")), out _));

        ResoniteConstructionCityObject baked = Assert.Single(baker.FlushAll());

        Assert.Single(baked.Mesh.Submeshes);
        ResoniteMaterialBinding material = Assert.Single(baked.Materials);
        Assert.Equal("common|roof|variant:2|Triplanar|scale:0.344828x0.344828", material.MaterialKey);
        Assert.Equal(BundledDefaultMaterialFamilies.Roof, material.Family);
        Assert.Equal(ResoniteMaterialAssetScope.Common, material.AssetScope);
        Assert.Equal(2, material.BundledVariantIndex);
    }

    private static ResoniteConstructionCityObject CreateTriangleBuilding(
        string slotKey,
        double x,
        double z,
        string sourceUnitKey,
        string? sourceFileRelativePath,
        ResoniteMaterialBinding? material = null)
    {
        material ??= new ResoniteMaterialBinding(
            MaterialKey: "shared-material",
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
            PackageName: "bldg",
            ActualMeshCode: "53394525",
            LodLevel: 1,
            Transform: new ResoniteTransform(new ResoniteFloat3(x, 0.0, z)),
            Mesh: new ResoniteImportedMesh(
                [
                    new ResoniteMeshVertex(new ResoniteFloat3(0.0, 0.0, 0.0), new ResoniteFloat3(0.0, 1.0, 0.0), new ResoniteFloat2(0.0, 0.0)),
                    new ResoniteMeshVertex(new ResoniteFloat3(1.0, 0.0, 0.0), new ResoniteFloat3(0.0, 1.0, 0.0), new ResoniteFloat2(1.0, 0.0)),
                    new ResoniteMeshVertex(new ResoniteFloat3(0.0, 0.0, 1.0), new ResoniteFloat3(0.0, 1.0, 0.0), new ResoniteFloat2(0.0, 1.0)),
                ],
                [
                    new ResoniteMeshSubmesh(0, "shared-material", [0, 1, 2]),
                ]),
            Materials:
            [
                material,
            ],
            SourceObjectKey: $"{sourceUnitKey}:{slotKey}",
            SourceUnitKey: sourceUnitKey,
            SourceFileRelativePath: sourceFileRelativePath);
    }

    private static ResoniteMaterialBinding CreateBundledRoofMaterial(string materialKey)
    {
        return new ResoniteMaterialBinding(
            MaterialKey: materialKey,
            BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
            MaterialType: ResoniteMaterialType.Standard,
            TexturePayload: null,
            TextureSourceKind: ResoniteTextureSourceKind.Bundled,
            Projection: ResoniteMaterialProjection.Triplanar,
            DepthOffset: null,
            SubmeshIndices: [0],
            TextureScale: BundledDefaultMaterialProfiles.RoofingTiles012ATilesPerMeter,
            Family: BundledDefaultMaterialFamilies.Roof,
            AssetScope: ResoniteMaterialAssetScope.Common,
            BundledVariantIndex: 2);
    }
}
