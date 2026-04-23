using System;

namespace PlateauResoniteLink.Domain.Importing;

public static class FacadeFloorMetrics
{
    public const double DefaultFloorUnitMeters = 3.5;
    public const int UnknownFloorCountSentinel = 9999;
    private const double MinimumReferenceHeightMeters = 1e-6;

    public static bool IsUsableFloorCount(int? floorCount)
    {
        return floorCount is > 0 and < UnknownFloorCountSentinel;
    }

    public static double ToFloorUnits(double meters)
    {
        return meters / DefaultFloorUnitMeters;
    }

    public static int ResolveFloorCount(
        int? floorsAboveGround,
        double? measuredHeightMeters,
        double? geometryHeightMeters = null)
    {
        if (floorsAboveGround.HasValue && IsUsableFloorCount(floorsAboveGround))
        {
            return floorsAboveGround.Value;
        }

        double? referenceHeightMeters = geometryHeightMeters is > MinimumReferenceHeightMeters
            ? geometryHeightMeters.Value
            : measuredHeightMeters is > MinimumReferenceHeightMeters
                ? measuredHeightMeters.Value
                : null;

        if (referenceHeightMeters is null)
        {
            return 1;
        }

        return Math.Max(
            1,
            (int)Math.Round(
                referenceHeightMeters.Value / DefaultFloorUnitMeters,
                MidpointRounding.AwayFromZero));
    }

    public static double EstimateFloorHeightMeters(
        int? floorsAboveGround,
        double? measuredHeightMeters,
        double? geometryHeightMeters = null)
    {
        int floorCount = ResolveFloorCount(floorsAboveGround, measuredHeightMeters, geometryHeightMeters);
        double referenceHeightMeters = geometryHeightMeters is > MinimumReferenceHeightMeters
            ? geometryHeightMeters.Value
            : measuredHeightMeters is > MinimumReferenceHeightMeters
                ? measuredHeightMeters.Value
                : DefaultFloorUnitMeters * floorCount;

        return referenceHeightMeters / floorCount;
    }
}
