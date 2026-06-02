using System;
using System.Threading;
using System.Threading.Tasks;

using PlateauResoniteLink.Targets.Resonite;

namespace PlateauResoniteLink.Tests.Targets;

public sealed class NonDemCityObjectBakeCandidateFactoryTests
{
    [Fact]
    public void BakeEntryVariantsRejectNullPayload()
    {
        Assert.Throws<ArgumentNullException>(() => new NonDemBakeEntry.Atlas(null!));
        Assert.Throws<ArgumentNullException>(() => new NonDemBakeEntry.Preserved(null!));
    }

    [Fact]
    public async Task CreateAsyncRejectsNullBakeEntryFromFactoryBoundary()
    {
        NonDemCityObjectBakeCandidateFactory candidateFactory = new(new NullBakeEntryFactory());

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => candidateFactory.CreateAsync(
                new NonDemBufferedCityObject(
                    CreateAtlasCandidateCityObject(),
                    NonDemCityObjectBakePolicies.Default),
                CancellationToken.None));

        Assert.Contains("returned no entry", exception.Message, StringComparison.Ordinal);
    }

    private static ResoniteConstructionCityObject CreateAtlasCandidateCityObject()
    {
        return new ResoniteConstructionCityObject(
            SlotKey: "object",
            DisplayName: "object",
            PackageName: "bldg",
            ActualMeshCode: "53394525",
            LodLevel: 2,
            Transform: new ResoniteTransform(new ResoniteFloat3(0.0, 0.0, 0.0)),
            Mesh: new ResoniteImportedMesh(
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
                [new ResoniteMeshSubmesh(0, [0, 1, 2])]),
            Materials:
            [
                new ResoniteMaterialBinding(
                    BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
                    MaterialType: ResoniteMaterialType.Standard,
                    TexturePayload: new ResoniteTexturePayload(
                        width: 1,
                        height: 1,
                        colorProfile: null,
                        binaryPayload: [255, 255, 255, 255],
                        identity: "dataset.png"),
                    TextureSourceKind: ResoniteTextureSourceKind.Dataset,
                    Projection: ResoniteMaterialProjection.Uv,
                    DepthOffset: null,
                    SubmeshIndices: [0]),
            ]);
    }

    private sealed class NullBakeEntryFactory : INonDemBakeEntryFactory
    {
        public Task<NonDemBakeEntry> CreateAsync(
            ResoniteConstructionCityObject cityObject,
            ResoniteMeshSubmesh submesh,
            ResoniteMaterialBinding material,
            CancellationToken cancellationToken)
        {
            _ = cityObject;
            _ = submesh;
            _ = material;
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<NonDemBakeEntry>(null!);
        }
    }
}
