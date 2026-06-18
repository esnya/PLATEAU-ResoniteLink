using PlateauResoniteLink.Core.Domain.Importing;

namespace PlateauResoniteLink.Resonite.Targets.Resonite;

internal readonly record struct BundledTextureImportKey(
    BundledDefaultTextureAsset Asset,
    string ColorProfile);
