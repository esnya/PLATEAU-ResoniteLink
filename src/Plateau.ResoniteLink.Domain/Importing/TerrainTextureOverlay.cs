namespace Plateau.ResoniteLink.Domain.Importing;

public sealed record TerrainTextureOverlay(
    string TexturePath,
    string PackageName,
    string UrlTemplate,
    int ZoomLevel,
    GeographicRectangle GeographicBounds,
    int MaxTextureSize);
