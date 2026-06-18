namespace PlateauResoniteLink.Core.Domain.Importing;

public static class BundledDefaultMaterialProfiles
{
    public static readonly ScalarPair FacadeDefaultTilesPerMeterValue = GetProfile(
        BundledDefaultMaterialFamilies.GetVariant(BundledDefaultMaterialFamilies.Facade, 0)).TextureScale;
    public static readonly ScalarPair ConcreteDefaultTilesPerMeterValue = GetProfile(
        BundledDefaultMaterialFamilies.GetVariant(BundledDefaultMaterialFamilies.Roof, 0)).TextureScale;

    public static BundledDefaultMaterialProfile GetProfile(string texturePath)
    {
        return BundledDefaultMaterialFamilies.TryGetVariantDefinition(texturePath, out BundledDefaultMaterialVariant variant)
            ? variant.TextureSet
            : new BundledDefaultMaterialProfile(BundledDefaultMaterialTiling.DefaultTilesPerMeterValue);
    }

    public static ScalarPair GetTilesPerMeterValue(string texturePath)
    {
        return GetProfile(texturePath).TextureScale;
    }

    public static ScalarPair? GetTextureOffsetValue(string texturePath)
    {
        return GetProfile(texturePath).TextureOffset;
    }

    public static ScalarPair GetImplicitTilesPerMeterValue(string texturePath)
    {
        return GetProfile(texturePath).GetImplicitTextureScale();
    }

    public static ScalarPair? GetImplicitTextureOffsetValue(string texturePath)
    {
        return GetProfile(texturePath).GetImplicitTextureOffset();
    }

}
