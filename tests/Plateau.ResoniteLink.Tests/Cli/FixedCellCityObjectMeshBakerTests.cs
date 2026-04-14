using Plateau.ResoniteLink.Cli;
using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Tests.Cli;

public sealed class FixedCellCityObjectMeshBakerTests
{
    [Fact]
    public void TryBufferBakesLod1BuildingsInSameCellIntoSingleMesh()
    {
        FixedCellCityObjectMeshBaker baker = new(cellSizeMeters: 64.0, maxCityObjectsPerBatch: 2, maxVerticesPerBatch: 1000);
        ResoniteConstructionCityObject first = CreateTriangleBuilding("first", x: 10.0, z: 12.0, actualMeshCode: "533945", sourceUnitKey: "unit-a");
        ResoniteConstructionCityObject second = CreateTriangleBuilding("second", x: 18.0, z: 20.0, actualMeshCode: "533945", sourceUnitKey: "unit-a");

        bool firstBuffered = baker.TryBuffer(first, out ResoniteConstructionCityObject? firstBaked);
        bool secondBuffered = baker.TryBuffer(second, out ResoniteConstructionCityObject? baked);

        Assert.True(firstBuffered);
        Assert.Null(firstBaked);
        Assert.True(secondBuffered);
        Assert.Null(baked);
        ResoniteConstructionCityObject bakedOnFlush = Assert.Single(baker.FlushAll());
        baked = bakedOnFlush;
        Assert.Equal("bldg", baked.PackageName);
        Assert.Equal(1, baked.LodLevel);
        Assert.Equal(6, baked.Mesh.Vertices.Count);
        Assert.Single(baked.Mesh.Submeshes);
        Assert.Single(baked.Materials);
        Assert.Equal([0], baked.Materials[0].SubmeshIndices);
        Assert.Equal(new ResoniteFloat3(10.0, 0.0, 12.0), baked.Transform.Position);

        ResoniteFloat3[] positions = baked.Mesh.Vertices.Select(static vertex => vertex.Position).ToArray();
        Assert.Contains(new ResoniteFloat3(0.0, 0.0, 0.0), positions);
        Assert.Contains(new ResoniteFloat3(1.0, 0.0, 0.0), positions);
        Assert.Contains(new ResoniteFloat3(0.0, 0.0, 1.0), positions);
        Assert.Contains(new ResoniteFloat3(8.0, 0.0, 8.0), positions);
        Assert.Contains(new ResoniteFloat3(9.0, 0.0, 8.0), positions);
        Assert.Contains(new ResoniteFloat3(8.0, 0.0, 9.0), positions);
    }

    [Fact]
    public void FlushAllReturnsSeparateBatchesPerCell()
    {
        FixedCellCityObjectMeshBaker baker = new(cellSizeMeters: 64.0, maxCityObjectsPerBatch: 10, maxVerticesPerBatch: 1000);
        Assert.True(baker.TryBuffer(CreateTriangleBuilding("left", x: 10.0, z: 10.0, actualMeshCode: "533945", sourceUnitKey: "unit-a"), out _));
        Assert.True(baker.TryBuffer(CreateTriangleBuilding("right", x: 80.0, z: 10.0, actualMeshCode: "533945", sourceUnitKey: "unit-a"), out _));

        IReadOnlyList<ResoniteConstructionCityObject> baked = baker.FlushAll();

        ResoniteConstructionCityObject cityObject = Assert.Single(baked);
        Assert.Equal(new ResoniteFloat3(10.0, 0.0, 10.0), cityObject.Transform.Position);
        Assert.Equal(6, cityObject.Mesh.Vertices.Count);
    }

    [Fact]
    public void TryBufferNoEvictionEvenWithBufferedCellLimit()
    {
        FixedCellCityObjectMeshBaker baker = new(
            cellSizeMeters: 64.0,
            maxCityObjectsPerBatch: 10,
            maxVerticesPerBatch: 1000,
            maxBufferedCells: 2);
        Assert.True(baker.TryBuffer(CreateTriangleBuilding("left", x: 10.0, z: 10.0, actualMeshCode: "53394525", sourceUnitKey: "unit-left"), out ResoniteConstructionCityObject? firstBaked));
        Assert.True(baker.TryBuffer(CreateTriangleBuilding("center", x: 80.0, z: 10.0, actualMeshCode: "53394526", sourceUnitKey: "unit-center"), out ResoniteConstructionCityObject? secondBaked));

        bool thirdBuffered = baker.TryBuffer(CreateTriangleBuilding("right", x: 138.0, z: 10.0, actualMeshCode: "53394527", sourceUnitKey: "unit-right"), out ResoniteConstructionCityObject? flushed);

        Assert.True(thirdBuffered);
        Assert.Null(firstBaked);
        Assert.Null(secondBaked);
        Assert.Null(flushed);

        IReadOnlyList<ResoniteConstructionCityObject> remainingBatches = baker.FlushAll();
        Assert.Equal(3, remainingBatches.Count);
        Assert.Contains(remainingBatches, static cityObject => cityObject.ActualMeshCode == "53394525");
        Assert.Contains(remainingBatches, static cityObject => cityObject.ActualMeshCode == "53394526");
        Assert.Contains(remainingBatches, static cityObject => cityObject.ActualMeshCode == "53394527");
    }

    [Fact]
    public async Task TryBufferAsyncEmitsReadyCityObjectWhenCellBatchLimitIsExceeded()
    {
        FixedCellCityObjectMeshBaker baker = new(
            cellSizeMeters: 64.0,
            maxCityObjectsPerBatch: 1,
            maxVerticesPerBatch: 1000);

        BufferedCityObjectBufferResult first = await baker.TryBufferAsync(
            CreateTriangleBuilding("first", x: 10.0, z: 10.0));
        BufferedCityObjectBufferResult second = await baker.TryBufferAsync(
            CreateTriangleBuilding("second", x: 11.0, z: 10.0));

        Assert.True(first.Buffered);
        Assert.Empty(first.ReadyCityObjects);
        Assert.True(second.Buffered);
        Assert.NotEmpty(second.ReadyCityObjects);

        IReadOnlyList<ResoniteConstructionCityObject> remainingBatches = await baker.FlushAllAsync();
        Assert.Empty(remainingBatches);
    }

    [Fact]
    public async Task TryBufferAsyncDoesNotFlushWhenSourceUnitChangesWithoutCapacityPressure()
    {
        FixedCellCityObjectMeshBaker baker = new(
            cellSizeMeters: 64.0,
            maxCityObjectsPerBatch: 10,
            maxVerticesPerBatch: 1000);

        BufferedCityObjectBufferResult first = await baker.TryBufferAsync(
            CreateTriangleBuilding("first", x: 10.0, z: 10.0, sourceUnitKey: "unit-a"));
        BufferedCityObjectBufferResult second = await baker.TryBufferAsync(
            CreateTriangleBuilding("second", x: 80.0, z: 10.0, sourceUnitKey: "unit-b"));

        Assert.True(first.Buffered);
        Assert.Empty(first.ReadyCityObjects);
        Assert.True(second.Buffered);
        Assert.Empty(second.ReadyCityObjects);

        IReadOnlyList<ResoniteConstructionCityObject> baked = await baker.FlushAllAsync();
        Assert.Equal(2, baked.Count);
        Assert.Contains(baked, static cityObject => cityObject.SourceUnitKey == "unit-a");
        Assert.Contains(baked, static cityObject => cityObject.SourceUnitKey == "unit-b");
    }

    [Fact]
    public void TryBufferSkipsNonTargetCityObjects()
    {
        FixedCellCityObjectMeshBaker baker = new(cellSizeMeters: 64.0, maxCityObjectsPerBatch: 2, maxVerticesPerBatch: 1000);
        ResoniteConstructionCityObject lod2Building = CreateTriangleBuilding("lod2", x: 10.0, z: 10.0) with { LodLevel = 2 };

        bool buffered = baker.TryBuffer(lod2Building, out ResoniteConstructionCityObject? baked);

        Assert.False(buffered);
        Assert.Null(baked);
        Assert.Empty(baker.FlushAll());
    }

    [Fact]
    public void FlushAllReturnsSingleBatchPerEightDigitMeshCodeAcrossCells()
    {
        FixedCellCityObjectMeshBaker baker = new(cellSizeMeters: 64.0, maxCityObjectsPerBatch: 10, maxVerticesPerBatch: 1000);
        Assert.True(baker.TryBuffer(CreateTriangleBuilding("left", x: 10.0, z: 10.0, sourceUnitKey: "shared-unit"), out _));
        Assert.True(baker.TryBuffer(CreateTriangleBuilding("right", x: 80.0, z: 10.0, sourceUnitKey: "shared-unit"), out _));

        IReadOnlyList<ResoniteConstructionCityObject> baked = baker.FlushAll();

        ResoniteConstructionCityObject cityObject = Assert.Single(baked);
        Assert.Equal("53394525", cityObject.ActualMeshCode);
        Assert.Equal(new ResoniteFloat3(10.0, 0.0, 10.0), cityObject.Transform.Position);
        Assert.Equal(6, cityObject.Mesh.Vertices.Count);
    }

    [Fact]
    public void TryBufferFlushesWhenEightDigitMeshBakeHitsBatchLimits()
    {
        FixedCellCityObjectMeshBaker baker = new(cellSizeMeters: 64.0, maxCityObjectsPerBatch: 1, maxVerticesPerBatch: 3);
        Assert.True(baker.TryBuffer(CreateTriangleBuilding("first", x: 10.0, z: 10.0, sourceUnitKey: "shared-unit"), out ResoniteConstructionCityObject? firstBaked));
        Assert.True(baker.TryBuffer(CreateTriangleBuilding("second", x: 80.0, z: 10.0, sourceUnitKey: "shared-unit"), out ResoniteConstructionCityObject? secondBaked));

        Assert.Null(firstBaked);
        Assert.NotNull(secondBaked);
        Assert.Empty(baker.FlushAll());
    }

    [Fact]
    public void FlushAllSeparatesEightDigitMeshBatchesPerSourceUnit()
    {
        FixedCellCityObjectMeshBaker baker = new(cellSizeMeters: 64.0, maxCityObjectsPerBatch: 10, maxVerticesPerBatch: 1000);
        Assert.True(baker.TryBuffer(CreateTriangleBuilding("left", x: 10.0, z: 10.0, sourceUnitKey: "unit-a"), out _));
        Assert.True(baker.TryBuffer(CreateTriangleBuilding("right", x: 80.0, z: 10.0, sourceUnitKey: "unit-b"), out _));

        IReadOnlyList<ResoniteConstructionCityObject> baked = baker.FlushAll();

        Assert.Equal(2, baked.Count);
        Assert.Contains(baked, static cityObject => cityObject.SourceUnitKey == "unit-a" && cityObject.Transform.Position == new ResoniteFloat3(10.0, 0.0, 10.0));
        Assert.Contains(baked, static cityObject => cityObject.SourceUnitKey == "unit-b" && cityObject.Transform.Position == new ResoniteFloat3(80.0, 0.0, 10.0));
    }

    [Fact]
    public async Task FlushAllAsyncKeepsSameScopeMergedAcrossInterleavedDifferentScopes()
    {
        FixedCellCityObjectMeshBaker baker = new(
            cellSizeMeters: 64.0,
            maxCityObjectsPerBatch: 10,
            maxVerticesPerBatch: 1000,
            maxBufferedCells: 8);

        await baker.TryBufferAsync(CreateTriangleBuilding("a-one", x: 10.0, z: 10.0, sourceUnitKey: "unit-a") with
        {
            SourceFileRelativePath = "udx/bldg/53394525_citygml/a.gml",
        });
        await baker.TryBufferAsync(CreateTriangleBuilding("b-one", x: 80.0, z: 10.0, sourceUnitKey: "unit-b") with
        {
            SourceFileRelativePath = "udx/bldg/53394525_citygml/b.gml",
        });
        await baker.TryBufferAsync(CreateTriangleBuilding("a-two", x: 12.0, z: 10.0, sourceUnitKey: "unit-a") with
        {
            SourceFileRelativePath = "udx/bldg/53394525_citygml/a.gml",
        });

        IReadOnlyList<ResoniteConstructionCityObject> baked = await baker.FlushAllAsync();

        Assert.Equal(2, baked.Count);
        ResoniteConstructionCityObject scopeA = Assert.Single(
            baked,
            static cityObject => cityObject.SourceFileRelativePath == "udx/bldg/53394525_citygml/a.gml");
        Assert.Equal(6, scopeA.Mesh.Vertices.Count);
        Assert.Equal("unit-a", scopeA.SourceUnitKey);
        Assert.Contains(
            baked,
            static cityObject => cityObject.SourceFileRelativePath == "udx/bldg/53394525_citygml/b.gml"
                && cityObject.SourceUnitKey == "unit-b");
    }

    [Fact]
    public void FlushAllSeparatesSameSourceUnitAcrossSourceFiles()
    {
        FixedCellCityObjectMeshBaker baker = new(cellSizeMeters: 64.0, maxCityObjectsPerBatch: 10, maxVerticesPerBatch: 1000);
        Assert.True(baker.TryBuffer(CreateTriangleBuilding("left", x: 10.0, z: 10.0, sourceUnitKey: "shared-unit") with
        {
            SourceFileRelativePath = "udx/bldg/53394525_citygml/a.gml",
        }, out _));
        Assert.True(baker.TryBuffer(CreateTriangleBuilding("right", x: 80.0, z: 10.0, sourceUnitKey: "shared-unit") with
        {
            SourceFileRelativePath = "udx/bldg/53394525_citygml/b.gml",
        }, out _));

        IReadOnlyList<ResoniteConstructionCityObject> baked = baker.FlushAll();

        Assert.Equal(2, baked.Count);
        Assert.Contains(baked, static cityObject => cityObject.SourceFileRelativePath == "udx/bldg/53394525_citygml/a.gml");
        Assert.Contains(baked, static cityObject => cityObject.SourceFileRelativePath == "udx/bldg/53394525_citygml/b.gml");
    }

    [Fact]
    public void FlushAllKeepsSameSourceFileMergedAcrossDifferentSourceUnits()
    {
        FixedCellCityObjectMeshBaker baker = new(cellSizeMeters: 64.0, maxCityObjectsPerBatch: 10, maxVerticesPerBatch: 1000);
        Assert.True(
            baker.TryBuffer(CreateTriangleBuilding("a-one", x: 10.0, z: 10.0, sourceUnitKey: "unit-a") with
            {
                SourceFileRelativePath = "udx/bldg/53394525_citygml/common.gml",
            },
            out _));
        Assert.True(
            baker.TryBuffer(CreateTriangleBuilding("a-two", x: 18.0, z: 12.0, sourceUnitKey: "unit-b") with
            {
                SourceFileRelativePath = "udx/bldg/53394525_citygml/common.gml",
            },
            out _));

        ResoniteConstructionCityObject baked = Assert.Single(baker.FlushAll());
        Assert.Equal("udx/bldg/53394525_citygml/common.gml", baked.SourceFileRelativePath);
        Assert.Null(baked.SourceUnitKey);
        Assert.Equal(6, baked.Mesh.Vertices.Count);
    }

    private static ResoniteConstructionCityObject CreateTriangleBuilding(
        string slotKey,
        double x,
        double z,
        string actualMeshCode = "53394525",
        string sourceUnitKey = "source-unit")
    {
        return new ResoniteConstructionCityObject(
            SlotKey: slotKey,
            DisplayName: slotKey,
            PackageName: "bldg",
            ActualMeshCode: actualMeshCode,
            LodLevel: 1,
            Transform: new ResoniteTransform(new ResoniteFloat3(x, 0.0, z)),
            Mesh: new ResoniteImportedMesh(
                [
                    new ResoniteMeshVertex(new ResoniteFloat3(0.0, 0.0, 0.0), new ResoniteFloat3(0.0, 1.0, 0.0), new ResoniteFloat2(0.0, 0.0)),
                    new ResoniteMeshVertex(new ResoniteFloat3(1.0, 0.0, 0.0), new ResoniteFloat3(0.0, 1.0, 0.0), new ResoniteFloat2(1.0, 0.0)),
                    new ResoniteMeshVertex(new ResoniteFloat3(0.0, 0.0, 1.0), new ResoniteFloat3(0.0, 1.0, 0.0), new ResoniteFloat2(0.0, 1.0)),
                ],
                [
                    new ResoniteMeshSubmesh(
                        Index: 0,
                        MaterialKey: "shared-material",
                        TriangleVertexIndices: [0, 1, 2]),
                ]),
            Materials:
            [
                new ResoniteMaterialBinding(
                    MaterialKey: "shared-material",
                    BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
                    MaterialType: ResoniteMaterialType.Standard,
                    TexturePath: null,
                    TextureSourceKind: ResoniteTextureSourceKind.Bundled,
                    Projection: ResoniteMaterialProjection.Uv,
                    DepthOffset: null,
                    SubmeshIndices: [0]),
            ],
            SourceObjectKey: $"{sourceUnitKey}:{slotKey}",
            SourceUnitKey: sourceUnitKey,
            SourceFileRelativePath: $"{sourceUnitKey}.gml");
    }
}
