namespace PlateauResoniteLink.Domain.Importing;

public sealed record BundledDefaultMaterialProfile(
    ScalarPair TextureScale,
    ScalarPair? TextureOffset = null);
