using System;
using System.Collections.Generic;
using System.Linq;

using PlateauResoniteLink.Application.Importing;
using PlateauResoniteLink.Targets.Resonite.Diagnostics;

namespace PlateauResoniteLink.Tests;

internal static class TextureImportSourceTestFactory
{
    public static ITextureImportSource CreateRawTextureSource(
        int width,
        int height,
        string colorProfile,
        byte[] rawRgba32Bytes,
        string? identity = null)
    {
        return TextureImportSourceFactory.CreateInMemory(
            width,
            height,
            colorProfile,
            rawRgba32Bytes,
            identity ?? $"test-rgba32:{width}:{height}:{Guid.NewGuid():N}",
            TexturePayloadFormat.RawRgba32);
    }

    public static IReadOnlyList<RawTexturePayload> ImportedRgba32Textures(SceneSinkRecordingClient client)
    {
        return client.ImportedTexturePayloads
            .Where(static payload => payload.Format == RawTexturePayloadFormat.Rgba32)
            .ToArray();
    }

    public static IReadOnlyList<RawTexturePayload> ImportedHdrTextures(SceneSinkRecordingClient client)
    {
        return client.ImportedTexturePayloads
            .Where(static payload => payload.Format == RawTexturePayloadFormat.RgbaFloat32)
            .ToArray();
    }

    public static bool IsSolidColorTexture(RawTexturePayload texture, byte r, byte g, byte b)
    {
        byte[] expectedPixel = [r, g, b, 255];
        return texture.Format == RawTexturePayloadFormat.Rgba32
            && texture.Width == 2
            && texture.Height == 2
            && texture.Bytes.Chunk(4).All(pixel => pixel.SequenceEqual(expectedPixel));
    }
}
