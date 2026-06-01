using System.Threading;
using System.Threading.Tasks;

using PlateauResoniteLink.Application.Importing;

namespace PlateauResoniteLink.Tests.Application;

public sealed class TexturePayloadTests
{
    [Fact]
    public void ConstructorCopiesBinaryPayloadBytes()
    {
        byte[] source = [1, 2, 3, 4];

        TexturePayload payload = new(1, 1, "sRGB", source, "dataset:texture");
        source[0] = 9;

        Assert.Equal<byte>([1, 2, 3, 4], payload.BinaryPayload);
    }

    [Fact]
    public async Task ConstructorCreatesDimensionedRawTextureSource()
    {
        TexturePayload payload = new(1, 1, "sRGB", [1, 2, 3, 4], "dataset:texture");

        RawTexturePayload rawPayload = await TextureImportSourceMaterializer.MaterializeRawAsync(
            payload.Source,
            CancellationToken.None);

        Assert.Equal(1, rawPayload.Width);
        Assert.Equal(1, rawPayload.Height);
        Assert.Equal<byte>([1, 2, 3, 4], rawPayload.Bytes);
    }
}
