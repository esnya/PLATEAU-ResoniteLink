using System;
using System.Threading;
using System.Threading.Tasks;

using PlateauResoniteLink.Application.Importing;
using PlateauResoniteLink.Targets.Resonite;

namespace PlateauResoniteLink.Tests.Targets;

public sealed class ResoniteTexturePayloadTests
{
    [Fact]
    public void ConstructorCopiesBinaryPayloadBytes()
    {
        byte[] source = [4, 3, 2, 1];

        ResoniteTexturePayload payload = new(1, 1, "sRGB", source, "dataset:texture");
        source[0] = 9;

        Assert.Equal<byte>([4, 3, 2, 1], payload.BinaryPayload);
    }

    [Fact]
    public async Task ConstructorCreatesDimensionedRawTextureSource()
    {
        ResoniteTexturePayload payload = new(1, 1, "sRGB", [4, 3, 2, 1], "dataset:texture");

        RawTexturePayload rawPayload = await TextureImportSourceMaterializer.MaterializeRawAsync(
            payload.Source,
            CancellationToken.None);

        Assert.Equal(1, rawPayload.Width);
        Assert.Equal(1, rawPayload.Height);
        Assert.Equal<byte>([4, 3, 2, 1], rawPayload.Bytes);
    }

    [Fact]
    public void RawConstructorKeepsGeneratedIdentityConsistentWithSource()
    {
        ResoniteTexturePayload payload = new(1, 1, "sRGB", [4, 3, 2, 1]);

        Assert.False(string.IsNullOrWhiteSpace(payload.Identity));
        Assert.Equal(payload.Identity, payload.Source.Identity);
    }

    [Theory]
    [InlineData(0, 1, 4)]
    [InlineData(1, 0, 4)]
    [InlineData(1, 1, 3)]
    [InlineData(1, 1, 5)]
    public void RawConstructorRejectsInvalidRawShape(int width, int height, int byteLength)
    {
        byte[] bytes = new byte[byteLength];

        Assert.ThrowsAny<ArgumentException>(() => new ResoniteTexturePayload(width, height, "sRGB", bytes, "dataset:texture"));
    }

    [Fact]
    public void EncodedConstructorRejectsPayloadIdentityThatDiffersFromSourceIdentity()
    {
        ITextureImportSource source = TextureImportSourceFactory.CreateEncodedImageInMemory(
            "sRGB",
            [1, 2, 3, 4],
            "source:texture");

        ArgumentException exception = Assert.Throws<ArgumentException>(() =>
            new ResoniteTexturePayload(
                1,
                1,
                "sRGB",
                source,
                "payload:texture"));

        Assert.Contains("identity must match", exception.Message, System.StringComparison.Ordinal);
    }

    [Fact]
    public void ContractRoundTripPreservesSourceBackedRawPayload()
    {
        ITextureImportSource source = TextureImportSourceFactory.CreateRawRgba32InMemory(
            1,
            1,
            "sRGB",
            new byte[] { 4, 3, 2, 1 },
            "source:texture");
        ResoniteTexturePayload payload = new(
            1,
            1,
            "sRGB",
            Assert.IsAssignableFrom<IRawTexturePayloadSource>(source));
        ResoniteMaterialBinding binding = new(
            BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
            MaterialType: ResoniteMaterialType.Standard,
            TexturePayload: payload,
            TextureSourceKind: ResoniteTextureSourceKind.Dataset,
            Projection: ResoniteMaterialProjection.Uv,
            DepthOffset: null,
            SubmeshIndices: [0]);

        MaterialBinding contractBinding = ResoniteLiveSceneImportTargetTestSupport.ToContractMaterial(binding);

        TexturePayload contractPayload = Assert.IsType<TexturePayload>(contractBinding.TexturePayload);
        Assert.Equal(TexturePayloadFormat.RawRgba32, contractPayload.Format);
        Assert.Same(source, contractPayload.Source);
        Assert.Equal(source.Identity, contractPayload.Identity);
        Assert.True(contractPayload.BinaryPayload.IsDefaultOrEmpty);
    }
}
