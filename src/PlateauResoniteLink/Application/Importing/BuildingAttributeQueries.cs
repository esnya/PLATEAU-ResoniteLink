using System;
using System.Linq;

using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Application.Importing;

internal static class BuildingAttributeQueries
{
    internal static int? TryGetKnownPositiveInteger(BuildingAttributeContext attributes, BuildingMetricKind kind)
    {
        if (!attributes.Metrics.TryGet(kind, out BuildingMetricValue metric))
        {
            return null;
        }

        int value = (int)Math.Round(metric.Value, MidpointRounding.AwayFromZero);
        return Math.Abs(metric.Value - value) < 1e-9
            && (value == 0 || FacadeFloorMetrics.IsUsableFloorCount(value))
                ? value
                : null;
    }

    internal static double? TryGetKnownPositiveMetric(BuildingAttributeContext attributes, BuildingMetricKind kind)
    {
        return attributes.Metrics.TryGet(kind, out BuildingMetricValue metric) && metric.Value > 0.0
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
