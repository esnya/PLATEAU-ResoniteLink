namespace PlateauResoniteLink.Domain.Importing;

public static class BundledDefaultMaterialTiling
{
    public static readonly ScalarPair DefaultTilesPerMeterValue = new(0.35, 0.35);

    public static readonly ResoniteFloat2 DefaultTilesPerMeter = new(
        DefaultTilesPerMeterValue.X,
        DefaultTilesPerMeterValue.Y);
}
