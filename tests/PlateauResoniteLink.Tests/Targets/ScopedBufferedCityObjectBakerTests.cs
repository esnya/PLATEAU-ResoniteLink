using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using PlateauResoniteLink.Targets.Resonite;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace PlateauResoniteLink.Tests.Targets;

public sealed class ScopedBufferedCityObjectBakerTests
{
    [Fact]
    public async Task FlushAllAsyncKeepsDistinctObjectsAcrossDifferentSourceUnits()
    {
        ScopedBufferedCityObjectBaker baker = CreateScopedBaker();

        await AssertBufferedAsync(
            baker,
            CreateNonDemObject(
                "object-one",
                lodLevel: 1,
                "source-object-one",
                sourceUnitKey: "source-unit-one",
                sourceFileRelativePath: null,
                CreatePayload("textures/object-one.png", new Rgba32(255, 0, 0, 255))));
        await AssertBufferedAsync(
            baker,
            CreateNonDemObject(
                "object-two",
                lodLevel: 1,
                "source-object-two",
                sourceUnitKey: "source-unit-two",
                sourceFileRelativePath: null,
                CreatePayload("textures/object-two.png", new Rgba32(0, 255, 0, 255))));

        IReadOnlyList<ResoniteConstructionCityObject> baked = await baker.FlushAllAsync();

        Assert.Equal(2, baked.Count);
        Assert.Contains(baked, static cityObject => cityObject.SourceObjectKey == "source-object-one");
        Assert.Contains(baked, static cityObject => cityObject.SourceObjectKey == "source-object-two");
    }

    [Fact]
    public async Task TryBufferAsyncRequiresSourceFileOrSourceUnitScope()
    {
        ScopedBufferedCityObjectBaker baker = CreateScopedBaker();

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await baker.TryBufferAsync(
                CreateNonDemObject(
                    "object-no-scope",
                    lodLevel: 1,
                    "source-object",
                    sourceUnitKey: null,
                    sourceFileRelativePath: null,
                    CreatePayload("textures/object-no-scope.png", new Rgba32(0, 0, 255, 255)))));

        Assert.Contains("SourceFileRelativePath or SourceUnitKey", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FlushAllAsyncSeparatesDifferentLodWithinSameSourceFileScope()
    {
        ScopedBufferedCityObjectBaker baker = CreateScopedBaker();

        await AssertBufferedAsync(
            baker,
            CreateNonDemObject(
                "object-lod1",
                lodLevel: 1,
                "source-object",
                sourceUnitKey: "common-unit",
                sourceFileRelativePath: "udx/tran/53394525/common.gml",
                CreatePayload("textures/object-lod1.png", new Rgba32(255, 0, 0, 255))));
        await AssertBufferedAsync(
            baker,
            CreateNonDemObject(
                "object-lod2",
                lodLevel: 2,
                "source-object",
                sourceUnitKey: "common-unit",
                sourceFileRelativePath: "udx/tran/53394525/common.gml",
                CreatePayload("textures/object-lod2.png", new Rgba32(0, 255, 0, 255))));

        IReadOnlyList<ResoniteConstructionCityObject> baked = await baker.FlushAllAsync();

        Assert.Equal(2, baked.Count);
        Assert.Contains(baked, static cityObject => cityObject.LodLevel == 1);
        Assert.Contains(baked, static cityObject => cityObject.LodLevel == 2);
    }

    [Fact]
    public async Task TryBufferAsyncFlushesLeastRecentlyUsedScopeWhenMaxBufferedScopesIsExceeded()
    {
        ScopedBufferedCityObjectBaker baker = new(
            "NonDemBake",
            () => new RecordingBufferedCityObjectBaker(),
            maxBufferedScopes: 2);

        await AssertBufferedAsync(
            baker,
            CreateNonDemObject(
                "object-one",
                lodLevel: 1,
                "source-object-one",
                sourceUnitKey: "source-unit-one",
                sourceFileRelativePath: null,
                CreatePayload("textures/object-one.png", new Rgba32(255, 0, 0, 255))));
        await AssertBufferedAsync(
            baker,
            CreateNonDemObject(
                "object-two",
                lodLevel: 1,
                "source-object-two",
                sourceUnitKey: "source-unit-two",
                sourceFileRelativePath: null,
                CreatePayload("textures/object-two.png", new Rgba32(0, 255, 0, 255))));

        _ = await baker.TryBufferAsync(
            CreateNonDemObject(
                "object-one-refresh",
                lodLevel: 1,
                "source-object-one-refresh",
                sourceUnitKey: "source-unit-one",
                sourceFileRelativePath: null,
                CreatePayload("textures/object-one-refresh.png", new Rgba32(255, 255, 0, 255))));
        BufferedCityObjectBufferResult overflowResult = await baker.TryBufferAsync(
            CreateNonDemObject(
                "object-three",
                lodLevel: 1,
                "source-object-three",
                sourceUnitKey: "source-unit-three",
                sourceFileRelativePath: null,
                CreatePayload("textures/object-three.png", new Rgba32(0, 0, 255, 255))));

        Assert.True(overflowResult.Buffered);
        Assert.Equal("source-unit-two", Assert.Single(overflowResult.ReadyCityObjects).SourceUnitKey);
    }

    private static ScopedBufferedCityObjectBaker CreateScopedBaker()
    {
        return new ScopedBufferedCityObjectBaker(
            "NonDemBake",
            () => new NonDemCityObjectBaker(new ResoniteTextureImageLoader(), maxAtlasSize: 32, tilePaddingPixels: 0));
    }

    private static async Task AssertBufferedAsync(
        IResoniteBufferedCityObjectBaker baker,
        ResoniteConstructionCityObject cityObject)
    {
        BufferedCityObjectBufferResult result = await baker.TryBufferAsync(cityObject);
        Assert.True(result.Buffered);
        Assert.Empty(result.ReadyCityObjects);
    }

    private static ResoniteConstructionCityObject CreateNonDemObject(
        string slotKey,
        int lodLevel,
        string sourceObjectKey,
        string? sourceUnitKey,
        string? sourceFileRelativePath,
        ResoniteTexturePayload payload)
    {
        return new ResoniteConstructionCityObject(
            SlotKey: slotKey,
            DisplayName: slotKey,
            PackageName: "tran",
            ActualMeshCode: "53394525",
            LodLevel: lodLevel,
            Transform: new ResoniteTransform(new ResoniteFloat3(0.0, 0.0, 0.0)),
            Mesh: new ResoniteImportedMesh(
                [
                    new ResoniteMeshVertex(new ResoniteFloat3(0.0, 0.0, 0.0), new ResoniteFloat3(0.0, 1.0, 0.0), new ResoniteFloat2(0.0, 0.0)),
                    new ResoniteMeshVertex(new ResoniteFloat3(1.0, 0.0, 0.0), new ResoniteFloat3(0.0, 1.0, 0.0), new ResoniteFloat2(1.0, 0.0)),
                    new ResoniteMeshVertex(new ResoniteFloat3(0.0, 1.0, 0.0), new ResoniteFloat3(0.0, 1.0, 0.0), new ResoniteFloat2(0.0, 1.0)),
                ],
                [
                    new ResoniteMeshSubmesh(0, $"{slotKey}-material", [0, 1, 2]),
                ]),
            Materials:
            [
                new ResoniteMaterialBinding(
                    MaterialKey: $"{slotKey}-material",
                    BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
                    MaterialType: ResoniteMaterialType.Standard,
                    TexturePayload: payload,
                    TextureSourceKind: ResoniteTextureSourceKind.Dataset,
                    Projection: ResoniteMaterialProjection.Uv,
                    DepthOffset: null,
                    SubmeshIndices: [0]),
            ],
            SourceObjectKey: sourceObjectKey,
            SourceUnitKey: sourceUnitKey,
            SourceFileRelativePath: sourceFileRelativePath);
    }

    private static ResoniteTexturePayload CreatePayload(string identity, Rgba32 color)
    {
        using Image<Rgba32> image = new(2, 2);
        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                Span<Rgba32> row = accessor.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                {
                    row[x] = color;
                }
            }
        });

        return ResoniteTextureImportFactory.CreatePayloadFromImage(image, identity: identity);
    }

    private sealed class RecordingBufferedCityObjectBaker : IResoniteBufferedCityObjectBaker
    {
        private readonly List<ResoniteConstructionCityObject> bufferedCityObjects = [];

        public string Name => "Recording";

        public int BakedInputCityObjectCount { get; private set; }

        public int BakedOutputCityObjectCount { get; private set; }

        public ValueTask<BufferedCityObjectBufferResult> TryBufferAsync(
            ResoniteConstructionCityObject cityObject,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            bufferedCityObjects.Add(cityObject);
            BakedInputCityObjectCount++;
            return ValueTask.FromResult(new BufferedCityObjectBufferResult(Buffered: true, []));
        }

        public Task<IReadOnlyList<ResoniteConstructionCityObject>> FlushAllAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<ResoniteConstructionCityObject> readyCityObjects = bufferedCityObjects.ToArray();
            BakedOutputCityObjectCount += readyCityObjects.Count;
            bufferedCityObjects.Clear();
            return Task.FromResult(readyCityObjects);
        }

        public async Task FlushAllAsync(
            Func<ResoniteConstructionCityObject, CancellationToken, Task> onBakedCityObject,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(onBakedCityObject);
            foreach (ResoniteConstructionCityObject cityObject in await FlushAllAsync(cancellationToken))
            {
                await onBakedCityObject(cityObject, cancellationToken);
            }
        }
    }
}
