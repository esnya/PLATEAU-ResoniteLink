using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

using PlateauResoniteLink.Core.Application.Importing.Contracts;


namespace PlateauResoniteLink.Resonite.Targets.Resonite;

internal sealed class ResoniteDistanceCullingRegistry
{
    private readonly ConcurrentDictionary<DistanceCullingRegistrationKey, byte> registrations = new();

    public void Register(ResoniteConstructionCityObject cityObject, ResoniteObjectSlotHierarchy objectSlots)
    {
        ArgumentNullException.ThrowIfNull(cityObject);
        ArgumentNullException.ThrowIfNull(objectSlots);

        if (cityObject.DistanceCullingClass is not { } distanceCullingClass)
        {
            return;
        }

        CreatedSlot sourceFileSlot = objectSlots.SourceFileSlot
            ?? throw new InvalidOperationException("Distance culling requires the source file root slot.");
        registrations.TryAdd(
            new DistanceCullingRegistrationKey(
                sourceFileSlot.Locator.Value,
                sourceFileSlot.SlotName,
                objectSlots.LodSlot.Locator.Value,
                objectSlots.LodSlot.SlotName,
                distanceCullingClass),
            0);
    }

    public IReadOnlyList<ResoniteDistanceCullingSourceFilePlan> CreatePlans()
    {
        return registrations.Keys
            .GroupBy(static key => new SourceFileKey(key.SourceFileSlotId, key.SourceFileSlotName))
            .OrderBy(static group => group.Key.SourceFileSlotName, StringComparer.Ordinal)
            .ThenBy(static group => group.Key.SourceFileSlotId, StringComparer.Ordinal)
            .Select(static group => new ResoniteDistanceCullingSourceFilePlan(
                new CreatedSlot(new ResoniteSlotLocator(group.Key.SourceFileSlotId), group.Key.SourceFileSlotName),
                group
                    .GroupBy(static key => new LodSlotKey(key.LodSlotId, key.LodSlotName))
                    .Select(static lodGroup => new ResoniteDistanceCullingLodTarget(
                        new CreatedSlot(new ResoniteSlotLocator(lodGroup.Key.LodSlotId), lodGroup.Key.LodSlotName),
                        ResolveLodClass(lodGroup.Select(static key => key.DistanceCullingClass))))
                    .OrderBy(static target => target.LodSlot.SlotName, StringComparer.Ordinal)
                    .ThenBy(static target => target.LodSlot.Locator.Value, StringComparer.Ordinal)
                    .ToArray()))
            .Where(static plan => plan.Targets.Count > 0)
            .ToArray();
    }

    private static DistanceCullingClass ResolveLodClass(IEnumerable<DistanceCullingClass> classes)
    {
        DistanceCullingClass[] distinctClasses = classes.Distinct().ToArray();
        if (distinctClasses.Contains(DistanceCullingClass.Landmark))
        {
            return DistanceCullingClass.Landmark;
        }

        return distinctClasses.Length == 1
            ? distinctClasses[0]
            : throw new InvalidOperationException(
                $"LOD parent has incompatible distance culling classes: {string.Join(", ", distinctClasses)}.");
    }

    private readonly record struct DistanceCullingRegistrationKey(
        string SourceFileSlotId,
        string SourceFileSlotName,
        string LodSlotId,
        string LodSlotName,
        DistanceCullingClass DistanceCullingClass);

    private readonly record struct SourceFileKey(
        string SourceFileSlotId,
        string SourceFileSlotName);

    private readonly record struct LodSlotKey(
        string LodSlotId,
        string LodSlotName);
}

internal sealed record ResoniteDistanceCullingSourceFilePlan(
    CreatedSlot SourceFileSlot,
    IReadOnlyList<ResoniteDistanceCullingLodTarget> Targets);

internal sealed record ResoniteDistanceCullingLodTarget(
    CreatedSlot LodSlot,
    DistanceCullingClass DistanceCullingClass);
