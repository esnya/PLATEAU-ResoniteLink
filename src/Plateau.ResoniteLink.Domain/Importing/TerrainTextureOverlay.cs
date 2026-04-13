namespace Plateau.ResoniteLink.Domain.Importing;

public sealed record TerrainTextureOverlay(
    string TexturePath,
    string PackageName,
    string UrlTemplate,
    int ZoomLevel,
    GeographicRectangle GeographicBounds,
    int MaxTextureSize,
    string? FallbackUrlTemplate = null,
    TerrainTextureLicenseMode LicenseMode = TerrainTextureLicenseMode.Unknown);

public enum TerrainTextureLicenseMode
{
    Unknown = 0,
    PlateauOrthoOnly = 1,
    PlateauOrthoWithGsiFallback = 2,
}
