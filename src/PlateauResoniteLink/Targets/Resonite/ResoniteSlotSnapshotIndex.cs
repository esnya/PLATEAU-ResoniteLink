using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

using ResoniteLink;

namespace PlateauResoniteLink.Targets.Resonite;

internal sealed class ResoniteSlotSnapshotIndex(CreatedSlot datasetRootSlot)
{
    private readonly ConcurrentDictionary<string, byte> createdSlotIds = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<SlotIndexKey, CreatedSlot> sharedSlotIndex = new();
    private readonly ConcurrentDictionary<string, Slot> observedSlotSnapshotsById = new(StringComparer.Ordinal);
    private ObservedSourceRootSlot[]? observedDatasetSourceRoots;

    public void IndexSetupHierarchy(ResoniteSceneSetupState setupState)
    {
        observedDatasetSourceRoots = null;
        if (setupState.DatasetRootSnapshot is not null)
        {
            observedSlotSnapshotsById.Clear();
            IndexObservedSlotSnapshot(setupState.DatasetRootSnapshot);
        }
        else
        {
            IndexCreatedSharedSlot(ResoniteSlotLocator.Root, setupState.DatasetRootSlot);
        }

        IndexCreatedSharedSlot(setupState.DatasetRootSlot.Locator, setupState.DatasetAssetsRootSlot);
    }

    public CreatedSlot? TryGetSharedChildSlot(ResoniteSlotLocator parent, string slotName)
    {
        return sharedSlotIndex.TryGetValue(new SlotIndexKey(parent.Value, slotName), out CreatedSlot createdSlot)
            ? createdSlot
            : null;
    }

    public CreatedSlot IndexCreatedSharedSlot(ResoniteSlotLocator parent, CreatedSlot createdSlot, ResoniteFloat3? position = null)
    {
        sharedSlotIndex[new SlotIndexKey(parent.Value, createdSlot.SlotName)] = createdSlot;
        observedSlotSnapshotsById[createdSlot.Locator.Value] = new Slot
        {
            ID = createdSlot.Locator.Value,
            Name = new Field_string { Value = createdSlot.SlotName },
            Parent = new Reference { TargetID = parent.Value },
            Position = position is null ? null : CreateFloat3(position),
        };
        return createdSlot;
    }

    public void MarkCreated(CreatedSlot createdSlot)
    {
        createdSlotIds[createdSlot.Locator.Value] = 0;
    }

    public IReadOnlyList<ObservedSourceRootSlot> GetObservedDatasetSourceRoots()
    {
        return observedDatasetSourceRoots ??= EnumerateObservedDatasetSourceRoots().ToArray();
    }

    private void IndexObservedSlotSnapshot(Slot slot)
    {
        if (string.IsNullOrWhiteSpace(slot.ID))
        {
            return;
        }

        observedSlotSnapshotsById[slot.ID] = slot;
        if (slot.Children is null || slot.Children.Count == 0)
        {
            return;
        }

        foreach (Slot child in slot.Children)
        {
            if (!string.IsNullOrWhiteSpace(child.ID) && !string.IsNullOrWhiteSpace(child.Name?.Value))
            {
                sharedSlotIndex[new SlotIndexKey(slot.ID!, child.Name!.Value)] = new CreatedSlot(new ResoniteSlotLocator(child.ID!), child.Name.Value);
            }

            IndexObservedSlotSnapshot(child);
        }
    }

    private IEnumerable<ObservedSourceRootSlot> EnumerateObservedDatasetSourceRoots()
    {
        return observedSlotSnapshotsById.Values
            .Where(slot => string.Equals(slot.Parent?.TargetID, datasetRootSlot.Locator.Value, StringComparison.Ordinal))
            .Where(slot => string.IsNullOrWhiteSpace(slot.ID) || !createdSlotIds.ContainsKey(slot.ID!))
            .Where(static slot => !string.Equals(slot.Name?.Value, "Assets", StringComparison.Ordinal))
            .Select(static slot => ObservedSourceRootSlot.TryCreate(slot, out ObservedSourceRootSlot sourceRoot)
                ? (SourceRoot: sourceRoot, HasPosition: true)
                : default)
            .Where(static candidate => candidate.HasPosition)
            .Select(static candidate => candidate.SourceRoot);
    }

    private static Field_float3 CreateFloat3(ResoniteFloat3 value)
    {
        return new Field_float3
        {
            Value = new float3
            {
                x = (float)value.X,
                y = (float)value.Y,
                z = (float)value.Z,
            },
        };
    }

    private readonly record struct SlotIndexKey(
        string ParentSlotId,
        string SlotName);
}
