using System;
using System.Collections.Generic;
using System.Linq;

namespace PlateauResoniteLink.Resonite.Targets.Resonite;

internal static class ObservedSourceRootPlacementResolver
{
    private const double StrictPlacementEquivalenceTolerance = 0.001;
    private const double SiblingProjectionDriftTolerance = 0.25;

    public static ObservedSourceRootPlacement? TryResolve(
        string sourceFileSlotName,
        string rootMeshCode,
        IReadOnlyList<ObservedDatasetSourceRoot> directSourceRoots)
    {
        ObservedSourceRootPlacement[] exactSourceRoots = directSourceRoots
            .Where(root => string.Equals(root.SlotName, sourceFileSlotName, StringComparison.Ordinal))
            .Select(root => new ObservedSourceRootPlacement(
                root.Position,
                ReferenceMeshCode: rootMeshCode,
                SlotId: root.SlotId))
            .ToArray();
        if (exactSourceRoots.Length > 0)
        {
            return SelectDeterministicObservedPlacement(
                exactSourceRoots,
                $"Dataset root contains multiple observed source roots named '{sourceFileSlotName}' with different placements. Append placement is ambiguous.",
                StrictPlacementEquivalenceTolerance);
        }

        ObservedSourceRootPlacement[] ancestorCandidates = directSourceRoots
            .Select(static root => root.ConcreteMeshCode is { } meshCode
                ? (Root: root, MeshCode: meshCode)
                : default)
            .Where(candidate => candidate.Root is not null
                && rootMeshCode.StartsWith(candidate.MeshCode, StringComparison.Ordinal))
            .Select(candidate => new ObservedSourceRootPlacement(
                ResonitePlacementPolicy.Add(
                    candidate.Root.Position,
                    ResonitePlacementPolicy.ComputeMeshCodeOffset(candidate.MeshCode, rootMeshCode)),
                ReferenceMeshCode: candidate.MeshCode,
                SlotId: candidate.Root.SlotId))
            .OrderByDescending(static candidate => candidate.ReferenceMeshCode.Length)
            .ToArray();
        if (ancestorCandidates.Length == 0)
        {
            ObservedSourceRootPlacement[] observedMeshCodeRoots = directSourceRoots
                .Select(static root => root.ConcreteMeshCode is { } meshCode
                    ? (Root: root, MeshCode: meshCode)
                    : default)
                .Where(static candidate => candidate.Root is not null)
                .Select(candidate => new ObservedSourceRootPlacement(
                    ResolvePositionFromObservedRootOrigin(candidate.MeshCode, rootMeshCode, candidate.Root.Position),
                    ReferenceMeshCode: candidate.MeshCode,
                    SlotId: candidate.Root.SlotId))
                .ToArray();
            return observedMeshCodeRoots.Length == 0
                ? null
                : SelectDeterministicObservedPlacement(
                    observedMeshCodeRoots,
                    $"Dataset root contains multiple observed source roots that resolve different placements for mesh-code '{rootMeshCode}'. Append placement is ambiguous.",
                    SiblingProjectionDriftTolerance);
        }

        int bestLength = ancestorCandidates[0].ReferenceMeshCode.Length;
        ObservedSourceRootPlacement[] bestCandidates = ancestorCandidates
            .Where(candidate => candidate.ReferenceMeshCode.Length == bestLength)
            .ToArray();
        return SelectDeterministicObservedPlacement(
            bestCandidates,
            $"Dataset root contains multiple observed source roots for mesh-code '{bestCandidates[0].ReferenceMeshCode}' with different placements. Append placement is ambiguous.",
            StrictPlacementEquivalenceTolerance);
    }

    private static ObservedSourceRootPlacement SelectDeterministicObservedPlacement(
        IReadOnlyCollection<ObservedSourceRootPlacement> candidates,
        string ambiguousMessage,
        double tolerance)
    {
        ObservedSourceRootPlacement selected = candidates
            .OrderBy(static candidate => candidate.ReferenceMeshCode, StringComparer.Ordinal)
            .ThenBy(static candidate => candidate.SlotId, StringComparer.Ordinal)
            .First();
        if (candidates.Any(candidate => !AreEquivalentPositions(candidate.Position, selected.Position, tolerance)))
        {
            throw new InvalidOperationException(ambiguousMessage);
        }

        return selected;
    }

    private static bool AreEquivalentPositions(ResoniteFloat3 left, ResoniteFloat3 right, double tolerance)
    {
        return Math.Abs(left.X - right.X) <= tolerance
            && Math.Abs(left.Y - right.Y) <= tolerance
            && Math.Abs(left.Z - right.Z) <= tolerance;
    }

    private static ResoniteFloat3 ResolvePositionFromObservedRootOrigin(
        string observedMeshCode,
        string rootMeshCode,
        ResoniteFloat3 observedRootPosition)
    {
        ResoniteLocalOrigin observedParentOrigin = ResonitePlacementPolicy.ResolveParentOriginFromMeshRootPosition(
            observedMeshCode,
            observedRootPosition);
        ResoniteFloat3 rootPosition = ResonitePlacementPolicy.ResolveMeshRootPosition(
            observedParentOrigin,
            rootMeshCode,
            observedRootHeight: observedRootPosition.Y);
        return rootPosition;
    }
}

internal readonly record struct ObservedSourceRootPlacement(
    ResoniteFloat3 Position,
    string ReferenceMeshCode,
    string SlotId);
