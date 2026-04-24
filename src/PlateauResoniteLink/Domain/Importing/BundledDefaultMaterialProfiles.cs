namespace PlateauResoniteLink.Domain.Importing;

public static class BundledDefaultMaterialProfiles
{
    public static readonly ScalarPair FacadeDefaultTilesPerMeterValue = FacadeMaterialUvScaling.Facade018AProfile.TextureScale;
    public static readonly ScalarPair Facade018ATilesPerMeterValue = FacadeMaterialUvScaling.Facade018AProfile.TextureScale;
    public static readonly ScalarPair Facade019ATilesPerMeterValue = FacadeMaterialUvScaling.Facade019AProfile.TextureScale;
    public static readonly ScalarPair Facade020ATilesPerMeterValue = FacadeMaterialUvScaling.Facade020AProfile.TextureScale;
    public static readonly ScalarPair ConcreteDefaultTilesPerMeterValue = BundledDefaultMaterialTiling.DefaultTilesPerMeterValue;
    public static readonly ScalarPair RoofingTiles012ATilesPerMeterValue = CreateTilesPerMeterValue(2.9, 2.9);
    public static readonly ScalarPair RoofingTiles014BTilesPerMeterValue = CreateTilesPerMeterValue(2.9, 2.9);
    public static readonly ScalarPair Plaster002TilesPerMeterValue = CreateTilesPerMeterValue(2.5, 2.5);
    public static readonly ScalarPair Ground054TilesPerMeterValue = CreateTilesPerMeterValue(3.5, 3.5);
    public static readonly ScalarPair RoadDefaultTilesPerMeterValue = BundledDefaultMaterialTiling.DefaultTilesPerMeterValue;
    public static readonly ScalarPair TextureCanFacadeTilesPerMeterValue = CreateTilesPerMeterValue(6.0, 6.0);

    public static BundledDefaultMaterialProfile GetProfile(string texturePath)
    {
        return texturePath.ToLowerInvariant() switch
        {
            "default-materials/ambientcg/facade/facade001_2k-jpg_color.jpg" => FacadeMaterialUvScaling.Facade001Profile,
            "default-materials/ambientcg/facade/facade018a_2k-jpg_color.jpg" => FacadeMaterialUvScaling.Facade018AProfile,
            "default-materials/ambientcg/facade/facade019a_2k-jpg_color.jpg" => FacadeMaterialUvScaling.Facade019AProfile,
            "default-materials/ambientcg/facade/facade020a_2k-jpg_color.jpg" => FacadeMaterialUvScaling.Facade020AProfile,
            _ => new BundledDefaultMaterialProfile(GetTilesPerMeterValue(texturePath)),
        };
    }

    public static ScalarPair GetTilesPerMeterValue(string texturePath)
    {
        return texturePath.ToLowerInvariant() switch
        {
            "default-materials/ambientcg/facade/facade001_2k-jpg_color.jpg" => FacadeDefaultTilesPerMeterValue,
            "default-materials/ambientcg/facade/facade018a_2k-jpg_color.jpg" => Facade018ATilesPerMeterValue,
            "default-materials/ambientcg/facade/facade019a_2k-jpg_color.jpg" => Facade019ATilesPerMeterValue,
            "default-materials/ambientcg/facade/facade020a_2k-jpg_color.jpg" => Facade020ATilesPerMeterValue,
            "default-materials/ambientcg/roof/concrete012_2k-jpg_color.jpg" => ConcreteDefaultTilesPerMeterValue,
            "default-materials/ambientcg/roof/concrete033_2k-jpg_color.jpg" => ConcreteDefaultTilesPerMeterValue,
            "default-materials/ambientcg/roof/roofingtiles012a_2k-jpg_color.jpg" => RoofingTiles012ATilesPerMeterValue,
            "default-materials/ambientcg/roof/roofingtiles014b_2k-jpg_color.jpg" => RoofingTiles014BTilesPerMeterValue,
            "default-materials/ambientcg/road/road012a_2k-jpg_color.jpg" => RoadDefaultTilesPerMeterValue,
            "default-materials/ambientcg/road/road013a_2k-jpg_color.jpg" => RoadDefaultTilesPerMeterValue,
            "default-materials/ambientcg/road/road014a_2k-jpg_color.jpg" => RoadDefaultTilesPerMeterValue,
            "default-materials/ambientcg/road/road015a_2k-jpg_color.jpg" => RoadDefaultTilesPerMeterValue,
            "default-materials/ambientcg/wall/plaster001_2k-jpg_color.jpg" => Plaster002TilesPerMeterValue,
            "default-materials/ambientcg/wall/plaster002_2k-jpg_color.jpg" => Plaster002TilesPerMeterValue,
            "default-materials/ambientcg/wall/plaster003_2k-jpg_color.jpg" => Plaster002TilesPerMeterValue,
            "default-materials/ambientcg/wall/plaster004_2k-jpg_color.jpg" => Plaster002TilesPerMeterValue,
            "default-materials/ambientcg/wall/plaster005_2k-jpg_color.jpg" => Plaster002TilesPerMeterValue,
            "default-materials/ambientcg/wall/plaster006_2k-jpg_color.jpg" => Plaster002TilesPerMeterValue,
            "default-materials/ambientcg/other/concrete012_2k-jpg_color.jpg" => ConcreteDefaultTilesPerMeterValue,
            "default-materials/ambientcg/other/ground054_2k-jpg_color.jpg" => Ground054TilesPerMeterValue,
            "default-materials/texturecan/facade/others0021_2k_color.jpg" => TextureCanFacadeTilesPerMeterValue,
            "default-materials/texturecan/facade/others0022_2k_color.jpg" => TextureCanFacadeTilesPerMeterValue,
            "default-materials/texturecan/facade/others0025_2k_color.jpg" => TextureCanFacadeTilesPerMeterValue,
            "default-materials/texturecan/facade/others0026_2k_color.jpg" => TextureCanFacadeTilesPerMeterValue,
            "default-materials/texturecan/facade/others0029_2k_color.jpg" => TextureCanFacadeTilesPerMeterValue,
            _ => BundledDefaultMaterialTiling.DefaultTilesPerMeterValue,
        };
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

    private static ScalarPair CreateTilesPerMeterValue(double widthMeters, double heightMeters)
    {
        return new ScalarPair(1.0 / widthMeters, 1.0 / heightMeters);
    }
}
