using System.Collections.Generic;

using PlateauResoniteLink.Targets.Resonite.Execution;

using ResoniteLink;

namespace PlateauResoniteLink.Targets.Resonite;

internal static class SourceRootPlacementResolver
{
    public static SourceRootPlacement Resolve(
        string sourceFileSlotName,
        string rootMeshCode,
        ResoniteLocalOrigin requestLocalOrigin,
        SceneAnchor? sceneAnchor,
        IReadOnlyList<Slot> observedDatasetSourceRoots)
    {
        ObservedSourceRootPlacement? observedRootPlacement = ObservedSourceRootPlacementResolver.TryResolve(
            sourceFileSlotName,
            rootMeshCode,
            observedDatasetSourceRoots);
        ResoniteFloat3 rootPosition = observedRootPlacement?.Position
            ?? ResolveDatasetRootAnchoredSourceRootPosition(sceneAnchor, rootMeshCode)
            ?? ResonitePlacementPolicy.ResolveMeshRootPosition(requestLocalOrigin, rootMeshCode);

        return new SourceRootPlacement(rootPosition, rootPosition);
    }

    private static ResoniteFloat3? ResolveDatasetRootAnchoredSourceRootPosition(
        SceneAnchor? sceneAnchor,
        string rootMeshCode)
    {
        if (sceneAnchor is not { ReferenceSourceFileRoot: null } anchor)
        {
            return null;
        }

        return ResonitePlacementPolicy.ComputeMeshCodeOffset(anchor.MeshCode, rootMeshCode);
    }
}

internal readonly record struct SourceRootPlacement(
    ResoniteFloat3 RootPosition,
    ResoniteFloat3 LocalPositionReferenceRoot);
