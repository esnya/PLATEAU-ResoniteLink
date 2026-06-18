using PlateauResoniteLink.Core.Domain.Importing;

namespace PlateauResoniteLink.Plateau.Application.Importing.Plateau;

internal readonly record struct BuildingFacadeScale(
    bool LowRise,
    bool MidOrHighRise,
    bool Midrise,
    bool Highrise,
    bool Landmark,
    bool LargeLowRise)
{
    public static BuildingFacadeScale Classify(
        int? floorCount,
        double? measuredHeightMeters,
        double? geometryHeightMeters,
        double? footprintAreaSquareMeters)
    {
        double? effectiveHeightMeters = GetEffectiveHeightMeters(measuredHeightMeters, geometryHeightMeters);
        bool lowRise = IsLowRise(floorCount, effectiveHeightMeters);

        return new BuildingFacadeScale(
            lowRise,
            IsMidOrHighRise(floorCount, effectiveHeightMeters),
            IsMidrise(floorCount, effectiveHeightMeters),
            IsHighrise(floorCount, effectiveHeightMeters),
            IsLandmarkScale(floorCount, effectiveHeightMeters),
            lowRise && footprintAreaSquareMeters is >= 1000.0);
    }

    private static bool IsLowRise(int? floorCount, double? heightMeters)
    {
        return (!FacadeFloorMetrics.IsUsableFloorCount(floorCount) || floorCount <= 3)
            && (!heightMeters.HasValue || heightMeters.Value < 12.0);
    }

    private static bool IsMidOrHighRise(int? floorCount, double? heightMeters)
    {
        return (FacadeFloorMetrics.IsUsableFloorCount(floorCount) && floorCount >= 4)
            || heightMeters is >= 12.0;
    }

    private static bool IsMidrise(int? floorCount, double? heightMeters)
    {
        return (heightMeters is >= 25.0 and < 80.0)
            || (FacadeFloorMetrics.IsUsableFloorCount(floorCount) && floorCount is >= 8 and < 20);
    }

    private static bool IsHighrise(int? floorCount, double? heightMeters)
    {
        return (heightMeters is >= 80.0 and < 150.0)
            || (FacadeFloorMetrics.IsUsableFloorCount(floorCount) && floorCount is >= 20 and < 35);
    }

    private static bool IsLandmarkScale(int? floorCount, double? heightMeters)
    {
        return heightMeters is >= 150.0
            || (FacadeFloorMetrics.IsUsableFloorCount(floorCount) && floorCount >= 35);
    }

    private static double? GetEffectiveHeightMeters(double? measuredHeightMeters, double? geometryHeightMeters)
    {
        return TryGetPositiveValue(measuredHeightMeters)
            ?? TryGetPositiveValue(geometryHeightMeters);
    }

    private static double? TryGetPositiveValue(double? value)
    {
        return value is > 0.0 && !double.IsNaN(value.Value) && !double.IsInfinity(value.Value)
            ? value.Value
            : null;
    }
}
