using System;
using System.Collections.Generic;
using System.Linq;

using ResoniteLink;

namespace PlateauResoniteLink.Targets.Resonite;

internal static class ObservedDatasetSourceRootSelector
{
    internal static Slot[] Select(
        string datasetRootSlotId,
        IEnumerable<Slot> observedSlots,
        IEnumerable<string> createdSlotIds)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(datasetRootSlotId);
        ArgumentNullException.ThrowIfNull(observedSlots);
        ArgumentNullException.ThrowIfNull(createdSlotIds);

        HashSet<string> createdSlotIdSet = new(createdSlotIds, StringComparer.Ordinal);
        return observedSlots
            .Where(slot => string.Equals(slot.Parent?.TargetID, datasetRootSlotId, StringComparison.Ordinal))
            .Where(slot => string.IsNullOrWhiteSpace(slot.ID) || !createdSlotIdSet.Contains(slot.ID!))
            .Where(static slot => !string.Equals(slot.Name?.Value, "Assets", StringComparison.Ordinal))
            .ToArray();
    }
}
