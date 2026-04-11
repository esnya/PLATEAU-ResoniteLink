namespace Plateau.ResoniteLink.Domain.Importing;

public static class BundledDefaultMaterialProfiles
{
    public static readonly ResoniteFloat2 FacadeDefaultTilesPerMeter = CreateTilesPerMeter(13.0, 13.0);
    public static readonly ResoniteFloat2 Facade018ATilesPerMeter = CreateTilesPerMeter(13.0, 13.0);
    public static readonly ResoniteFloat2 Facade019ATilesPerMeter = CreateTilesPerMeter(13.0, 13.0);
    public static readonly ResoniteFloat2 Facade020ATilesPerMeter = CreateTilesPerMeter(13.0, 13.0);
    public static readonly ResoniteFloat2 ConcreteDefaultTilesPerMeter = BundledDefaultMaterialTiling.DefaultTilesPerMeter;
    public static readonly ResoniteFloat2 RoofingTiles012ATilesPerMeter = CreateTilesPerMeter(2.9, 2.9);
    public static readonly ResoniteFloat2 RoofingTiles014BTilesPerMeter = CreateTilesPerMeter(2.9, 2.9);
    public static readonly ResoniteFloat2 Asphalt020LTilesPerMeter = CreateTilesPerMeter(4.6, 4.6);
    public static readonly ResoniteFloat2 Asphalt023LTilesPerMeter = CreateTilesPerMeter(2.5, 2.5);
    public static readonly ResoniteFloat2 Ground054TilesPerMeter = CreateTilesPerMeter(3.5, 3.5);

    public static ResoniteFloat2 GetTilesPerMeter(string texturePath)
    {
        return texturePath.ToLowerInvariant() switch
        {
            "default-materials/facade/facade001_2k-jpg_color.jpg" => FacadeDefaultTilesPerMeter,
            "default-materials/facade/facade018a_2k-jpg_color.jpg" => Facade018ATilesPerMeter,
            "default-materials/facade/facade019a_2k-jpg_color.jpg" => Facade019ATilesPerMeter,
            "default-materials/facade/facade020a_2k-jpg_color.jpg" => Facade020ATilesPerMeter,
            "default-materials/roof/concrete012_2k-jpg_color.jpg" => ConcreteDefaultTilesPerMeter,
            "default-materials/roof/concrete033_2k-jpg_color.jpg" => ConcreteDefaultTilesPerMeter,
            "default-materials/roof/roofingtiles012a_2k-jpg_color.jpg" => RoofingTiles012ATilesPerMeter,
            "default-materials/roof/roofingtiles014b_2k-jpg_color.jpg" => RoofingTiles014BTilesPerMeter,
            "default-materials/road/asphalt020l_2k-jpg_color.jpg" => Asphalt020LTilesPerMeter,
            "default-materials/road/asphalt023l_2k-jpg_color.jpg" => Asphalt023LTilesPerMeter,
            "default-materials/other/concrete012_2k-jpg_color.jpg" => ConcreteDefaultTilesPerMeter,
            "default-materials/other/ground054_2k-jpg_color.jpg" => Ground054TilesPerMeter,
            _ => BundledDefaultMaterialTiling.DefaultTilesPerMeter,
        };
    }

    private static ResoniteFloat2 CreateTilesPerMeter(double widthMeters, double heightMeters)
    {
        return new ResoniteFloat2(1.0 / widthMeters, 1.0 / heightMeters);
    }
}
