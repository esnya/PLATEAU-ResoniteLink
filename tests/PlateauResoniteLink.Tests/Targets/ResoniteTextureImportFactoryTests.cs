using PlateauResoniteLink.Domain.Importing;
using PlateauResoniteLink.Targets.Resonite;

namespace PlateauResoniteLink.Tests.Targets;

public sealed class ResoniteTextureImportFactoryTests
{
    [Fact]
    public void CreateRawFromPayloadCopiesRawRgbaBytes()
    {
        byte[] sourcePayloadBytes = [1, 2, 3, 4];
        ResoniteTexturePayload payload = new(
            1,
            1,
            ResoniteTextureColorProfiles.Srgb,
            sourcePayloadBytes,
            "dataset:texture",
            ResoniteTexturePayloadFormat.RawRgba32);

        ResoniteRawTextureImport import = ResoniteTextureImportFactory.CreateRawFromPayload(payload);
        sourcePayloadBytes[0] = 99;

        Assert.Equal<byte>([1, 2, 3, 4], import.RawRgba32Bytes);
    }
}
