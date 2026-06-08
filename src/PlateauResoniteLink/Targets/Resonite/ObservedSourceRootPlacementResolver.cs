using System;
using System.Collections.Generic;
using System.Linq;

using ResoniteLink;

namespace PlateauResoniteLink.Targets.Resonite;

internal static class ObservedSourceRootPlacementResolver
{
    private const double StrictPlacementEquivalenceTolerance = 0.001;
    private const double SiblingProjectionDriftTolerance = 0.25;

    public static ObservedSourceRootPlacement? TryResolve(
        string sourceFileSlotName,
        string rootMeshCode,
        IReadOnlyList<Slot> directSourceRoots)
    {
        ObservedSourceRootPlacement[] exactSourceRoots = directSourceRoots
            .Where(slot => string.Equals(slot.Name?.Value, sourceFileSlotName, StringComparison.Ordinal))
            .Select(slot => new ObservedSourceRootPlacement(
                GetRequiredSourceRootPosition(slot),
                ReferenceMeshCode: rootMeshCode,
                SlotId: slot.ID ?? string.Empty))
            .ToArray();
        if (exactSourceRoots.Length > 0)
        {
            return SelectDeterministicObservedPlacement(
                exactSourceRoots,
                $"Dataset root contains multiple observed source roots named '{sourceFileSlotName}' with different placements. Append placement is ambiguous.",
                StrictPlacementEquivalenceTolerance);
        }

        ObservedSourceRootPlacement[] ancestorCandidates = directSourceRoots
            .Select(static slot => ResoniteSourceMeshCodeAnchor.TryGetConcreteMeshCode(slot.Name?.Value ?? string.Empty, out string meshCode)
                ? (Slot: slot, MeshCode: meshCode)
                : default)
            .Where(candidate => candidate.Slot is not null
                && rootMeshCode.StartsWith(candidate.MeshCode, StringComparison.Ordinal))
            .Select(candidate => new ObservedSourceRootPlacement(
                ResonitePlacementPolicy.Add(
                    GetRequiredSourceRootPosition(candidate.Slot),
                    ResonitePlacementPolicy.ComputeMeshCodeOffset(candidate.MeshCode, rootMeshCode)),
                ReferenceMeshCode: candidate.MeshCode,
                SlotId: candidate.Slot.ID ?? string.Empty))
            .OrderByDescending(static candidate => candidate.ReferenceMeshCode.Length)
            .ToArray();
        if (ancestorCandidates.Length == 0)
        {
            ObservedSourceRootPlacement[] observedMeshCodeRoots = directSourceRoots
                .Select(static slot => ResoniteSourceMeshCodeAnchor.TryGetConcreteMeshCode(slot.Name?.Value ?? string.Empty, out string meshCode)
                    ? (Slot: slot, MeshCode: meshCode)
                    : default)
                .Where(static candidate => candidate.Slot is not null)
                .Select(candidate => new ObservedSourceRootPlacement(
                    ResolvePositionFromObservedRootOrigin(candidate.MeshCode, rootMeshCode, GetRequiredSourceRootPosition(candidate.Slot)),
                    ReferenceMeshCode: candidate.MeshCode,
                    SlotId: candidate.Slot.ID ?? string.Empty))
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

    private static ResoniteFloat3 GetRequiredSourceRootPosition(Slot slot)
    {
        if (slot.Position is not Field_float3 position)
        {
            throw new InvalidOperationException(
                $"Observed source-file root '{slot.Name?.Value ?? slot.ID ?? "<unnamed>"}' did not expose a Position. "
                + "Append placement requires positioned source-file roots.");
        }

        return new ResoniteFloat3(position.Value.x, position.Value.y, position.Value.z);
    }
}

internal readonly record struct ObservedSourceRootPlacement(
    ResoniteFloat3 Position,
    string ReferenceMeshCode,
    string SlotId);
