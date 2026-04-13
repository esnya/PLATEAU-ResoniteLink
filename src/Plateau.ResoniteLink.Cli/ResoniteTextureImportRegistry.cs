using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Cli;

internal sealed class ResoniteTextureImportRegistry
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<TextureReferenceKey, ResoniteTextureImport> importsByKey = new();

    public void Register(
        string texturePath,
        ResoniteTextureSourceKind sourceKind,
        ResoniteTextureImport textureImport)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(texturePath);
        ArgumentNullException.ThrowIfNull(textureImport);

        importsByKey[ResoniteMaterialAssetManager.CreateTextureReferenceKey(texturePath, sourceKind)] = textureImport;
    }

    public bool TryGet(
        string texturePath,
        ResoniteTextureSourceKind sourceKind,
        out ResoniteTextureImport? textureImport)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(texturePath);

        return importsByKey.TryGetValue(
            ResoniteMaterialAssetManager.CreateTextureReferenceKey(texturePath, sourceKind),
            out textureImport);
    }
}
