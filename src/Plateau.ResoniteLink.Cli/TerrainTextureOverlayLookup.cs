using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Cli;

internal sealed class TerrainTextureOverlayLookup
{
    private readonly Dictionary<string, TerrainTextureOverlay> overlaysByPath;

    public TerrainTextureOverlayLookup(IEnumerable<TerrainTextureOverlay> terrainTextureOverlays)
    {
        ArgumentNullException.ThrowIfNull(terrainTextureOverlays);
        overlaysByPath = terrainTextureOverlays.ToDictionary(
            static overlay => overlay.TexturePath,
            StringComparer.Ordinal);
    }

    public bool TryGetOverlay(string texturePath, out TerrainTextureOverlay? terrainTextureOverlay)
    {
        return overlaysByPath.TryGetValue(texturePath, out terrainTextureOverlay);
    }
}
