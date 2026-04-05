namespace Plateau.ResoniteLink.Domain.Importing;

public static class BundledDefaultMaterialProfiles
{
    public static readonly ResoniteFloat2 Facade018CTilesPerMeter = CreateTilesPerMeter(13.0, 13.0);
    public static readonly ResoniteFloat2 Facade019ATilesPerMeter = CreateTilesPerMeter(13.0, 13.0);
    public static readonly ResoniteFloat2 Facade020ATilesPerMeter = CreateTilesPerMeter(13.0, 13.0);
    public static readonly ResoniteFloat2 Road006TilesPerMeter = CreateTilesPerMeter(7.5, 7.5);
    public static readonly ResoniteFloat2 Ground054TilesPerMeter = CreateTilesPerMeter(3.5, 3.5);

    public static ResoniteFloat2 GetTilesPerMeter(string texturePath)
    {
        return texturePath.ToLowerInvariant() switch
        {
            "default-materials/facade/facade018c_2k-jpg_color.jpg" => Facade018CTilesPerMeter,
            "default-materials/facade/facade019a_2k-jpg_color.jpg" => Facade019ATilesPerMeter,
            "default-materials/facade/facade020a_2k-jpg_color.jpg" => Facade020ATilesPerMeter,
            "default-materials/road/road006_2k-jpg_color.jpg" => Road006TilesPerMeter,
            "default-materials/other/ground054_2k-jpg_color.jpg" => Ground054TilesPerMeter,
            _ => BundledDefaultMaterialTiling.DefaultTilesPerMeter,
        };
    }

    private static ResoniteFloat2 CreateTilesPerMeter(double widthMeters, double heightMeters)
    {
        return new ResoniteFloat2(1.0 / widthMeters, 1.0 / heightMeters);
    }
}
