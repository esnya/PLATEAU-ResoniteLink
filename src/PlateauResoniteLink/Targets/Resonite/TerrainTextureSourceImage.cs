using System;

using PlateauResoniteLink.Domain.Importing;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace PlateauResoniteLink.Targets.Resonite;

internal sealed record TerrainTextureSourceImage(
    Image<Rgba32> Image,
    TextureUvRect? OccupiedUvRect) : IDisposable
{
    public void Dispose()
    {
        Image.Dispose();
    }
}
