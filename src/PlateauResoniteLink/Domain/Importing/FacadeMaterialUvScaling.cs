namespace PlateauResoniteLink.Domain.Importing;

public static class FacadeMaterialUvScaling
{
    public const double FloorSquareMeters = 3.25;

    public static readonly ScalarPair CommonMaterialScaleValue = new(
        ToFloorSquareUnits(1.0),
        ToFloorSquareUnits(1.0));

    public static double ToFloorSquareUnits(double meters)
    {
        return meters / FloorSquareMeters;
    }
}
