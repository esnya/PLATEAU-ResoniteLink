using Plateau.ResoniteLink.Cli;
using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Tests.Cli;

public sealed class CompositeCityObjectBakerTests
{
    [Fact]
    public async Task FlushAllAsyncEmitsBakersInRegistrationOrder()
    {
        CompositeCityObjectBaker baker = new(
            new StubBaker("first", CreateCityObject("first"), flushDelayMilliseconds: 50),
            new StubBaker("second", CreateCityObject("second"), flushDelayMilliseconds: 0));

        List<string> flushedSlotKeys = [];
        await baker.FlushAllAsync(
            (cityObject, _) =>
            {
                flushedSlotKeys.Add(cityObject.SlotKey);
                return Task.CompletedTask;
            });

        Assert.Equal(["first", "second"], flushedSlotKeys);
    }

    private static ResoniteConstructionCityObject CreateCityObject(string slotKey)
    {
        return new ResoniteConstructionCityObject(
            SlotKey: slotKey,
            DisplayName: slotKey,
            PackageName: "bldg",
            ActualMeshCode: "53394525",
            LodLevel: 1,
            Transform: new ResoniteTransform(new ResoniteFloat3(0.0, 0.0, 0.0)),
            Mesh: new ResoniteImportedMesh(
                [
                    new ResoniteMeshVertex(new ResoniteFloat3(0.0, 0.0, 0.0), new ResoniteFloat3(0.0, 1.0, 0.0), new ResoniteFloat2(0.0, 0.0)),
                    new ResoniteMeshVertex(new ResoniteFloat3(1.0, 0.0, 0.0), new ResoniteFloat3(0.0, 1.0, 0.0), new ResoniteFloat2(1.0, 0.0)),
                    new ResoniteMeshVertex(new ResoniteFloat3(0.0, 0.0, 1.0), new ResoniteFloat3(0.0, 1.0, 0.0), new ResoniteFloat2(0.0, 1.0)),
                ],
                [
                    new ResoniteMeshSubmesh(0, "material", [0, 1, 2]),
                ]),
            Materials:
            [
                new ResoniteMaterialBinding(
                    MaterialKey: "material",
                    BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
                    MaterialType: ResoniteMaterialType.Standard,
                    TexturePath: null,
                    TextureSourceKind: ResoniteTextureSourceKind.Bundled,
                    Projection: ResoniteMaterialProjection.Uv,
                    DepthOffset: null,
                    SubmeshIndices: [0]),
            ]);
    }

    private sealed class StubBaker(
        string name,
        ResoniteConstructionCityObject cityObject,
        int flushDelayMilliseconds) : IResoniteBufferedCityObjectBaker
    {
        private bool emitted;

        public string Name => name;

        public int BakedInputCityObjectCount => 0;

        public int BakedOutputCityObjectCount => emitted ? 1 : 0;

        public ValueTask<BufferedCityObjectBufferResult> TryBufferAsync(
            ResoniteConstructionCityObject cityObject,
            CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(new BufferedCityObjectBufferResult(false, []));
        }

        public async Task FlushAllAsync(
            Func<ResoniteConstructionCityObject, CancellationToken, Task> onBakedCityObject,
            CancellationToken cancellationToken = default)
        {
            if (emitted)
            {
                return;
            }

            await Task.Delay(flushDelayMilliseconds, cancellationToken);
            emitted = true;
            await onBakedCityObject(cityObject, cancellationToken);
        }

        public async Task<IReadOnlyList<ResoniteConstructionCityObject>> FlushAllAsync(
            CancellationToken cancellationToken = default)
        {
            List<ResoniteConstructionCityObject> flushed = [];
            await FlushAllAsync(
                (cityObject, _) =>
                {
                    flushed.Add(cityObject);
                    return Task.CompletedTask;
                },
                cancellationToken);
            return flushed;
        }
    }
}
