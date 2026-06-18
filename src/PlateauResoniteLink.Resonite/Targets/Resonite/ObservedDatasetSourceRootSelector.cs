using System;
using System.Collections.Generic;
using System.Linq;

using ResoniteLink;

namespace PlateauResoniteLink.Targets.Resonite;

internal sealed record ObservedDatasetSourceRoot(
    string SlotId,
    string SlotName,
    ResoniteFloat3 Position,
    string? ConcreteMeshCode);

internal static class ObservedDatasetSourceRootSelector
{
    internal static ObservedDatasetSourceRoot[] Select(
        string datasetRootSlotId,
        IEnumerable<Slot> observedSlots,
        IEnumerable<string> createdSlotIds,
        IEnumerable<string>? expectedSourceRootSlotNames = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(datasetRootSlotId);
        ArgumentNullException.ThrowIfNull(observedSlots);
        ArgumentNullException.ThrowIfNull(createdSlotIds);

        HashSet<string> createdSlotIdSet = new(createdSlotIds, StringComparer.Ordinal);
        HashSet<string> expectedSourceRootSlotNameSet = new(expectedSourceRootSlotNames ?? [], StringComparer.Ordinal);
        return observedSlots
            .Where(slot => string.Equals(slot.Parent?.TargetID, datasetRootSlotId, StringComparison.Ordinal))
            .Where(slot => IsReusableSourceRootCandidate(slot, createdSlotIdSet, expectedSourceRootSlotNameSet))
            .Select(CreateSourceRoot)
            .ToArray();
    }

    internal static ObservedDatasetSourceRoot[] SelectDirectChildren(
        IEnumerable<Slot> observedDirectChildren,
        IEnumerable<string> createdSlotIds,
        IEnumerable<string>? expectedSourceRootSlotNames = null)
    {
        ArgumentNullException.ThrowIfNull(observedDirectChildren);
        ArgumentNullException.ThrowIfNull(createdSlotIds);

        HashSet<string> createdSlotIdSet = new(createdSlotIds, StringComparer.Ordinal);
        HashSet<string> expectedSourceRootSlotNameSet = new(expectedSourceRootSlotNames ?? [], StringComparer.Ordinal);
        return observedDirectChildren
            .Where(slot => IsReusableSourceRootCandidate(slot, createdSlotIdSet, expectedSourceRootSlotNameSet))
            .Select(CreateSourceRoot)
            .ToArray();
    }

    private static bool IsReusableSourceRootCandidate(
        Slot slot,
        HashSet<string> createdSlotIds,
        HashSet<string> expectedSourceRootSlotNames)
    {
        string? slotName = slot.Name?.Value;
        return !string.IsNullOrWhiteSpace(slotName)
            && !string.Equals(slotName, "Assets", StringComparison.Ordinal)
            && (expectedSourceRootSlotNames.Contains(slotName)
                || ResoniteSourceMeshCodeAnchor.TryGetConcreteMeshCode(slotName, out _))
            && (string.IsNullOrWhiteSpace(slot.ID) || !createdSlotIds.Contains(slot.ID!));
    }

    private static ObservedDatasetSourceRoot CreateSourceRoot(Slot slot)
    {
        string slotName = slot.Name?.Value
            ?? throw new InvalidOperationException("Observed source-file root did not expose a Name.");
        string? concreteMeshCode = ResoniteSourceMeshCodeAnchor.TryGetConcreteMeshCode(slotName, out string meshCode)
            ? meshCode
            : null;
        return new ObservedDatasetSourceRoot(
            SlotId: slot.ID ?? string.Empty,
            SlotName: slotName,
            Position: GetRequiredSourceRootPosition(slot),
            ConcreteMeshCode: concreteMeshCode);
    }

    private static ResoniteFloat3 GetRequiredSourceRootPosition(Slot slot)
    {
        if (slot.Position is Field_float3 position)
        {
            return new ResoniteFloat3(position.Value.x, position.Value.y, position.Value.z);
        }

        throw new InvalidOperationException(
            $"Observed source-file root '{slot.Name?.Value ?? slot.ID ?? "<unnamed>"}' did not expose a Position. "
            + "Append placement and anchor recovery require positioned source-file roots.");
    }
}
