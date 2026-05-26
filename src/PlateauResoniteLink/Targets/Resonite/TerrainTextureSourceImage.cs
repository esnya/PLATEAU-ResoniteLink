using System;

using PlateauResoniteLink.Domain.Importing;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace PlateauResoniteLink.Targets.Resonite;

internal sealed class TerrainTextureSourceImage(
    Image<Rgba32> image,
    TextureUvRect? occupiedUvRect) : IDisposable
{
    public Image<Rgba32> Image { get; } = image;

    public TextureUvRect? OccupiedUvRect { get; } = occupiedUvRect;

    public void Dispose()
    {
        Image.Dispose();
    }
}
