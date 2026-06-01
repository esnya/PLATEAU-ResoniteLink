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
}
