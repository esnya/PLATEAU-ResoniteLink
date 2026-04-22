namespace PlateauResoniteLink.Domain.Importing;

public static class BundledDefaultMaterialProfiles
{
    public static readonly ScalarPair FacadeDefaultTilesPerMeterValue = CreateTilesPerMeterValue(13.0, 13.0);
    public static readonly ScalarPair Facade018ATilesPerMeterValue = CreateTilesPerMeterValue(13.0, 13.0);
    public static readonly ScalarPair Facade019ATilesPerMeterValue = CreateTilesPerMeterValue(13.0, 13.0);
    public static readonly ScalarPair Facade020ATilesPerMeterValue = CreateTilesPerMeterValue(13.0, 13.0);
    public static readonly ScalarPair ConcreteDefaultTilesPerMeterValue = BundledDefaultMaterialTiling.DefaultTilesPerMeterValue;
    public static readonly ScalarPair RoofingTiles012ATilesPerMeterValue = CreateTilesPerMeterValue(2.9, 2.9);
    public static readonly ScalarPair RoofingTiles014BTilesPerMeterValue = CreateTilesPerMeterValue(2.9, 2.9);
    public static readonly ScalarPair Asphalt020LTilesPerMeterValue = CreateTilesPerMeterValue(4.6, 4.6);
    public static readonly ScalarPair Asphalt023LTilesPerMeterValue = CreateTilesPerMeterValue(2.5, 2.5);
    public static readonly ScalarPair Plaster002TilesPerMeterValue = CreateTilesPerMeterValue(2.5, 2.5);
    public static readonly ScalarPair Ground054TilesPerMeterValue = CreateTilesPerMeterValue(3.5, 3.5);

    public static readonly ResoniteFloat2 FacadeDefaultTilesPerMeter = ToResoniteFloat2(FacadeDefaultTilesPerMeterValue);
    public static readonly ResoniteFloat2 Facade018ATilesPerMeter = ToResoniteFloat2(Facade018ATilesPerMeterValue);
    public static readonly ResoniteFloat2 Facade019ATilesPerMeter = ToResoniteFloat2(Facade019ATilesPerMeterValue);
    public static readonly ResoniteFloat2 Facade020ATilesPerMeter = ToResoniteFloat2(Facade020ATilesPerMeterValue);
    public static readonly ResoniteFloat2 ConcreteDefaultTilesPerMeter = ToResoniteFloat2(ConcreteDefaultTilesPerMeterValue);
    public static readonly ResoniteFloat2 RoofingTiles012ATilesPerMeter = ToResoniteFloat2(RoofingTiles012ATilesPerMeterValue);
    public static readonly ResoniteFloat2 RoofingTiles014BTilesPerMeter = ToResoniteFloat2(RoofingTiles014BTilesPerMeterValue);
    public static readonly ResoniteFloat2 Asphalt020LTilesPerMeter = ToResoniteFloat2(Asphalt020LTilesPerMeterValue);
    public static readonly ResoniteFloat2 Asphalt023LTilesPerMeter = ToResoniteFloat2(Asphalt023LTilesPerMeterValue);
    public static readonly ResoniteFloat2 Plaster002TilesPerMeter = ToResoniteFloat2(Plaster002TilesPerMeterValue);
    public static readonly ResoniteFloat2 Ground054TilesPerMeter = ToResoniteFloat2(Ground054TilesPerMeterValue);

    public static ScalarPair GetTilesPerMeterValue(string texturePath)
    {
        return texturePath.ToLowerInvariant() switch
        {
            "default-materials/facade/facade001_2k-jpg_color.jpg" => FacadeDefaultTilesPerMeterValue,
            "default-materials/facade/facade018a_2k-jpg_color.jpg" => Facade018ATilesPerMeterValue,
            "default-materials/facade/facade019a_2k-jpg_color.jpg" => Facade019ATilesPerMeterValue,
            "default-materials/facade/facade020a_2k-jpg_color.jpg" => Facade020ATilesPerMeterValue,
            "default-materials/roof/concrete012_2k-jpg_color.jpg" => ConcreteDefaultTilesPerMeterValue,
            "default-materials/roof/concrete033_2k-jpg_color.jpg" => ConcreteDefaultTilesPerMeterValue,
            "default-materials/roof/roofingtiles012a_2k-jpg_color.jpg" => RoofingTiles012ATilesPerMeterValue,
            "default-materials/roof/roofingtiles014b_2k-jpg_color.jpg" => RoofingTiles014BTilesPerMeterValue,
            "default-materials/road/asphalt020l_2k-jpg_color.jpg" => Asphalt020LTilesPerMeterValue,
            "default-materials/road/asphalt023l_2k-jpg_color.jpg" => Asphalt023LTilesPerMeterValue,
            "default-materials/city-furniture/plaster002_2k-jpg_color.jpg" => Plaster002TilesPerMeterValue,
            "default-materials/other/concrete012_2k-jpg_color.jpg" => ConcreteDefaultTilesPerMeterValue,
            "default-materials/other/ground054_2k-jpg_color.jpg" => Ground054TilesPerMeterValue,
            _ => BundledDefaultMaterialTiling.DefaultTilesPerMeterValue,
        };
    }

    public static ResoniteFloat2 GetTilesPerMeter(string texturePath)
    {
        return ToResoniteFloat2(GetTilesPerMeterValue(texturePath));
    }

    private static ScalarPair CreateTilesPerMeterValue(double widthMeters, double heightMeters)
    {
        return new ScalarPair(1.0 / widthMeters, 1.0 / heightMeters);
    }

    private static ResoniteFloat2 ToResoniteFloat2(ScalarPair value)
    {
        return new ResoniteFloat2(value.X, value.Y);
    }
}
