namespace PlateauResoniteLink.Domain.Importing;

public static class FacadeMaterialUvScaling
{
    public const double FloorSquareMeters = 3.25;

    public static readonly ScalarPair CommonMaterialScaleValue = new(
        ToFloorSquareUnits(1.0),
        ToFloorSquareUnits(1.0));

    public static readonly ResoniteFloat2 CommonMaterialScale = new(
        CommonMaterialScaleValue.X,
        CommonMaterialScaleValue.Y);

    public static double ToFloorSquareUnits(double meters)
    {
        return meters / FloorSquareMeters;
    }
}
