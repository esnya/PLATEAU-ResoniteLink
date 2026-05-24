using System;
using System.Collections.Generic;
using System.Linq;

using ResoniteLink;

namespace PlateauResoniteLink.Targets.Resonite;

internal sealed class ObservedSourceRootPlacementResolver(
    ResoniteSlotLocator datasetRootSlot,
    IReadOnlyDictionary<string, Slot> observedSlotSnapshotsById,
    IReadOnlyDictionary<string, byte> createdSlotIds)
{
    private const double StrictPlacementEquivalenceTolerance = 0.001;
    private const double SiblingProjectionDriftTolerance = 0.25;

    private Slot[]? observedDatasetSourceRoots;

    public void Reset()
    {
        observedDatasetSourceRoots = null;
    }

    public ResoniteFloat3? TryResolve(
        string sourceFileSlotName,
        string rootMeshCode)
    {
        Slot[] directSourceRoots = GetObservedDatasetSourceRoots();
        ObservedSourceRootPlacement[] exactSourceRoots = directSourceRoots
            .Where(slot => string.Equals(slot.Name?.Value, sourceFileSlotName, StringComparison.Ordinal))
            .Select(slot => new ObservedSourceRootPlacement(
                GetSlotPositionOrDefault(slot),
                ReferenceMeshCode: rootMeshCode,
                SlotId: slot.ID ?? string.Empty))
            .ToArray();
        if (exactSourceRoots.Length > 0)
        {
            return SelectDeterministicObservedPlacement(
                exactSourceRoots,
                $"Dataset root contains multiple observed source roots named '{sourceFileSlotName}' with different placements. Append placement is ambiguous.",
                StrictPlacementEquivalenceTolerance).Position;
        }

        ObservedSourceRootPlacement[] ancestorCandidates = directSourceRoots
            .Select(static slot => ResoniteSourceMeshCodeAnchor.TryGetConcreteMeshCode(slot.Name?.Value ?? string.Empty, out string meshCode)
                ? (Slot: slot, MeshCode: meshCode)
                : default)
            .Where(candidate => candidate.Slot is not null
                && rootMeshCode.StartsWith(candidate.MeshCode, StringComparison.Ordinal))
            .Select(candidate => new ObservedSourceRootPlacement(
                ResonitePlacementPolicy.Add(
                    GetSlotPositionOrDefault(candidate.Slot),
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
                    ResonitePlacementPolicy.Add(
                        GetSlotPositionOrDefault(candidate.Slot),
                        ResonitePlacementPolicy.ComputeMeshCodeOffset(candidate.MeshCode, rootMeshCode)),
                    ReferenceMeshCode: candidate.MeshCode,
                    SlotId: candidate.Slot.ID ?? string.Empty))
                .ToArray();
            return observedMeshCodeRoots.Length == 0
                ? null
                : SelectDeterministicObservedPlacement(
                    observedMeshCodeRoots,
                    $"Dataset root contains multiple observed source roots that resolve different placements for meshcode '{rootMeshCode}'. Append placement is ambiguous.",
                    SiblingProjectionDriftTolerance).Position;
        }

        int bestLength = ancestorCandidates[0].ReferenceMeshCode.Length;
        ObservedSourceRootPlacement[] bestCandidates = ancestorCandidates
            .Where(candidate => candidate.ReferenceMeshCode.Length == bestLength)
            .ToArray();
        return SelectDeterministicObservedPlacement(
            bestCandidates,
            $"Dataset root contains multiple observed source roots for meshcode '{bestCandidates[0].ReferenceMeshCode}' with different placements. Append placement is ambiguous.",
            StrictPlacementEquivalenceTolerance).Position;
    }

    private IEnumerable<Slot> EnumerateObservedDatasetSourceRoots()
    {
        return observedSlotSnapshotsById.Values
            .Where(slot => string.Equals(slot.Parent?.TargetID, datasetRootSlot.Value, StringComparison.Ordinal))
            .Where(slot => string.IsNullOrWhiteSpace(slot.ID) || !createdSlotIds.ContainsKey(slot.ID!))
            .Where(static slot => !string.Equals(slot.Name?.Value, "Assets", StringComparison.Ordinal));
    }

    private Slot[] GetObservedDatasetSourceRoots()
    {
        return observedDatasetSourceRoots ??= EnumerateObservedDatasetSourceRoots().ToArray();
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

    private static ResoniteFloat3 GetSlotPositionOrDefault(Slot slot)
    {
        if (slot.Position is not Field_float3 position)
        {
            return new ResoniteFloat3(0.0, 0.0, 0.0);
        }

        return new ResoniteFloat3(position.Value.x, position.Value.y, position.Value.z);
    }

    private readonly record struct ObservedSourceRootPlacement(
        ResoniteFloat3 Position,
        string ReferenceMeshCode,
        string SlotId);
}
