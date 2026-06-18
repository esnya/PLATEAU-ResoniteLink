namespace PlateauResoniteLink.Core.Domain.Importing;

public sealed record BundledDefaultMaterialVariant(
    BundledDefaultTextureAsset<BundledDefaultAlbedoTextureRole> Albedo,
    BundledDefaultMaterialProfile TextureSet,
    BundledDefaultMaterialTextureSources? TextureSources = null);

public sealed record BundledDefaultMaterialTextureSources(
    BundledDefaultTextureAsset<BundledDefaultAlbedoTextureRole>? Albedo = null,
    BundledDefaultTextureAsset<BundledDefaultEmissionTextureRole>? Emission = null,
    BundledDefaultTextureAsset<BundledDefaultHeightTextureRole>? Height = null,
    BundledDefaultTextureAsset<BundledDefaultMetallicTextureRole>? Metallic = null,
    BundledDefaultTextureAsset<BundledDefaultNormalTextureRole>? Normal = null);
