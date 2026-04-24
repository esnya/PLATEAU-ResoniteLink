namespace PlateauResoniteLink.Domain.Importing;

public static class FacadeFloorMetrics
{
    public const double DefaultFloorUnitMeters = 3.5;
    public const int UnknownFloorCountSentinel = 9999;

    public static bool IsUsableFloorCount(int? floorCount)
    {
        return floorCount is > 0 and < UnknownFloorCountSentinel;
    }

    public static double ToFloorUnits(double meters)
    {
        return meters / DefaultFloorUnitMeters;
    }
}
