using System.Collections.Generic;

using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Targets.Resonite;

internal enum ResoniteCorrectionAxis
{
    X,
    Y,
    Z,
}

internal enum ResonitePlacementCorrectionReason
{
    RequestRelativeMeshCodeOffset,
    ObservedRootHeight,
}

internal readonly record struct ResonitePlacementCorrectionTerm(
    ResoniteCorrectionAxis Axis,
    double Value,
    ResonitePlacementCorrectionReason Reason);

internal sealed record ResonitePlacementCorrectionLayers(
    IReadOnlyList<ResonitePlacementCorrectionTerm> Source,
    IReadOnlyList<ResonitePlacementCorrectionTerm> Import,
    IReadOnlyList<ResonitePlacementCorrectionTerm> Placement,
    IReadOnlyList<ResonitePlacementCorrectionTerm> PostPlacement)
{
    public static ResonitePlacementCorrectionLayers Empty { get; } = new([], [], [], []);
}

internal sealed record ResonitePlacementCorrectionResult(
    ResoniteFloat3 CorrectedRootPosition,
    ResonitePlacementCorrectionLayers Layers)
{
    public ResoniteFloat3 ResolveCityObjectLocalPosition(ResoniteFloat3 cityObjectPosition)
    {
        return ResonitePlacementPolicy.Subtract(cityObjectPosition, CorrectedRootPosition);
    }
}
