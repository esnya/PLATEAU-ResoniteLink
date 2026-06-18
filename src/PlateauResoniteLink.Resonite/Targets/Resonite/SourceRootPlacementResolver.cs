using System.Collections.Generic;

namespace PlateauResoniteLink.Targets.Resonite;

internal static class SourceRootPlacementResolver
{
    public static SourceRootPlacement Resolve(
        string sourceFileSlotName,
        string rootMeshCode,
        ResoniteLocalOrigin requestLocalOrigin,
        IReadOnlyList<ObservedDatasetSourceRoot> observedDatasetSourceRoots)
    {
        ObservedSourceRootPlacement? observedRootPlacement = ObservedSourceRootPlacementResolver.TryResolve(
            sourceFileSlotName,
            rootMeshCode,
            observedDatasetSourceRoots);
        ResoniteFloat3 rootPosition = observedRootPlacement?.Position
            ?? ResonitePlacementPolicy.ResolveMeshRootPosition(requestLocalOrigin, rootMeshCode);

        return new SourceRootPlacement(rootPosition, rootPosition);
    }
}

internal readonly record struct SourceRootPlacement(
    ResoniteFloat3 RootPosition,
    ResoniteFloat3 LocalPositionReferenceRoot);
