namespace Plateau.ResoniteLink.Domain.Importing;

public static class BundledDefaultMaterialProfiles
{
    public static readonly ResoniteFloat2 Facade018ATilesPerMeter = CreateTilesPerMeter(13.0, 13.0);
    public static readonly ResoniteFloat2 Facade019ATilesPerMeter = CreateTilesPerMeter(13.0, 13.0);
    public static readonly ResoniteFloat2 Facade020ATilesPerMeter = CreateTilesPerMeter(13.0, 13.0);
    public static readonly ResoniteFloat2 Asphalt020LTilesPerMeter = CreateTilesPerMeter(4.6, 4.6);
    public static readonly ResoniteFloat2 Asphalt023LTilesPerMeter = CreateTilesPerMeter(2.5, 2.5);
    public static readonly ResoniteFloat2 Metal032TilesPerMeter = CreateTilesPerMeter(2.5, 2.5);
    public static readonly ResoniteFloat2 Ground054TilesPerMeter = CreateTilesPerMeter(3.5, 3.5);

    public static ResoniteFloat2 GetTilesPerMeter(string texturePath)
    {
        return texturePath.ToLowerInvariant() switch
        {
            "default-materials/facade/facade018a_2k-jpg_color.jpg" => Facade018ATilesPerMeter,
            "default-materials/facade/facade019a_2k-jpg_color.jpg" => Facade019ATilesPerMeter,
            "default-materials/facade/facade020a_2k-jpg_color.jpg" => Facade020ATilesPerMeter,
            "default-materials/road/asphalt020l_2k-jpg_color.jpg" => Asphalt020LTilesPerMeter,
            "default-materials/road/asphalt023l_2k-jpg_color.jpg" => Asphalt023LTilesPerMeter,
            "default-materials/city-furniture/metal032_2k-jpg_color.jpg" => Metal032TilesPerMeter,
            "default-materials/other/ground054_2k-jpg_color.jpg" => Ground054TilesPerMeter,
            _ => BundledDefaultMaterialTiling.DefaultTilesPerMeter,
        };
    }

    private static ResoniteFloat2 CreateTilesPerMeter(double widthMeters, double heightMeters)
    {
        return new ResoniteFloat2(1.0 / widthMeters, 1.0 / heightMeters);
    }
}
