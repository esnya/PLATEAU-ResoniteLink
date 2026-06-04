using System;
using System.Linq;

using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Application.Importing;

internal static class BuildingAttributeQueries
{
    internal static int? TryGetKnownPositiveInteger(BuildingMetricValue? metric)
    {
        if (metric is null)
        {
            return null;
        }

        int value = (int)Math.Round(metric.Value, MidpointRounding.AwayFromZero);
        return Math.Abs(metric.Value - value) < 1e-9
            && (value == 0 || FacadeFloorMetrics.IsUsableFloorCount(value))
                ? value
                : null;
    }

    internal static double? TryGetKnownPositiveMetric(BuildingMetricValue? metric)
    {
        return metric is not null && metric.Value > 0.0
            ? metric.Value
            : null;
    }

    internal static bool HasUse(BuildingAttributeContext attributes, PlateauBuildingUse use)
    {
        return attributes.Uses.Any(value => value.Value == use)
            || attributes.DetailedUses.Any(value => value.Value == use)
            || attributes.CityGmlFunctionCodes.Any(code => BuildingAttributeCodeMapper.MapUse(code) == use);
    }
}
