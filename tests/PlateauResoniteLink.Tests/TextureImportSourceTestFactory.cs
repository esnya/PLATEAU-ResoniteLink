using PlateauResoniteLink.Core.Application.Importing.Contracts;

using System;
using System.Collections.Generic;
using System.Linq;

using PlateauResoniteLink.Resonite.Targets.Resonite.Diagnostics;

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
        return TextureImportSourceFactory.CreateInMemoryRaw(
            width,
            height,
            colorProfile,
            rawRgba32Bytes,
            identity ?? $"test-rgba32:{width}:{height}:{Guid.NewGuid():N}");
    }

    public static IReadOnlyList<Rgba32RawTexturePayload> ImportedRgba32Textures(SceneSinkRecordingClient client)
    {
        return client.ImportedTexturePayloads
            .OfType<Rgba32RawTexturePayload>()
            .ToArray();
    }

    public static IReadOnlyList<RgbaFloat32RawTexturePayload> ImportedHdrTextures(SceneSinkRecordingClient client)
    {
        return client.ImportedTexturePayloads
            .OfType<RgbaFloat32RawTexturePayload>()
            .ToArray();
    }

    public static bool IsSolidColorTexture(Rgba32RawTexturePayload texture, byte r, byte g, byte b)
    {
        byte[] expectedPixel = [r, g, b, 255];
        return texture.Width == 2
            && texture.Height == 2
            && texture.Bytes.Chunk(4).All(pixel => pixel.SequenceEqual(expectedPixel));
    }
}
