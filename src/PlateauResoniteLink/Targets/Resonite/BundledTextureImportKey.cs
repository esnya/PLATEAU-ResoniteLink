using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Targets.Resonite;

internal readonly record struct BundledTextureImportKey(
    BundledDefaultTextureAsset Asset,
    string ColorProfile);
