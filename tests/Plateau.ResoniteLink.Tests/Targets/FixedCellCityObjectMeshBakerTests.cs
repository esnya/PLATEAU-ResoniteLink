using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Tests.Targets;

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

    private static ResoniteConstructionCityObject CreateTriangleBuilding(
        string slotKey,
        double x,
        double z,
        string sourceUnitKey,
        string? sourceFileRelativePath)
    {
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
            SourceObjectKey: $"{sourceUnitKey}:{slotKey}",
            SourceUnitKey: sourceUnitKey,
            SourceFileRelativePath: sourceFileRelativePath);
    }
}
