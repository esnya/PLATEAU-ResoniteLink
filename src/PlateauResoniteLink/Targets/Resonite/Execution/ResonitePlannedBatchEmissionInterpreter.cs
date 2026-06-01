using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using PlateauResoniteLink.Application.Logging;
using PlateauResoniteLink.Transport.ResoniteLink;

using ResoniteLink;

namespace PlateauResoniteLink.Targets.Resonite.Execution;

internal interface IResoniteSceneBatchEmitter
{
    Task ExecuteAsync(
        IResoniteLinkClient client,
        ResoniteConstructionCityObject cityObject,
        PlannedBatchEmission batchEmission,
        Action<string>? reportProgress,
        CancellationToken cancellationToken);
}

internal sealed class PlannedBatchEmissionInterpreter : IResoniteSceneBatchEmitter
{
    public Task ExecuteAsync(
        IResoniteLinkClient client,
        ResoniteConstructionCityObject cityObject,
        PlannedBatchEmission batchEmission,
        Action<string>? reportProgress,
        CancellationToken cancellationToken)
    {
        return ExecuteCoreAsync(client, cityObject, batchEmission, reportProgress, cancellationToken);
    }

    private static async Task ExecuteCoreAsync(
        IResoniteLinkClient client,
        ResoniteConstructionCityObject cityObject,
        PlannedBatchEmission batchEmission,
        Action<string>? reportProgress,
        CancellationToken cancellationToken)
    {
        ResoniteBatchOperations.BatchActionBuilder batchBuilder = new();
        Dictionary<BatchPlanSlotLocator, ResoniteBatchOperations.PendingBatchSlot> pendingSlotsByPlanId = new();
        Dictionary<BatchPlanComponentLocator, ResoniteBatchOperations.PendingBatchComponent> pendingComponentsByPlanId = new();
        Dictionary<BatchPlanFieldLocator, ResoniteBatchOperations.BatchTemporaryFieldId> pendingFieldsByPlanId = new();

        foreach (PlannedBatchSlotEmission slotEmission in batchEmission.SlotEmissions)
        {
            ResoniteBatchOperations.PendingBatchSlot pendingSlot = batchBuilder.AddSlot(
                ResolveSlotTargetId(slotEmission.ParentTarget, pendingSlotsByPlanId),
                slotEmission.SlotName,
                slotEmission.Position,
                slotEmission.Rotation);
            pendingSlotsByPlanId[slotEmission.Identity] = pendingSlot;
        }

        foreach (PlannedBatchComponentEmission componentEmission in batchEmission.ComponentEmissions)
        {
            Dictionary<string, Member> translatedMembers = TranslateMembers(
                componentEmission.Members,
                pendingSlotsByPlanId,
                pendingComponentsByPlanId,
                pendingFieldsByPlanId,
                batchBuilder);
            if (string.Equals(componentEmission.ComponentType, "[FrooxEngine]FrooxEngine.GridMesh", StringComparison.Ordinal)
                && translatedMembers.TryGetValue("Points", out Member? pointsMember)
                && translatedMembers.TryGetValue("DisplacementMagnitude", out Member? displacementMember)
                && pointsMember is Field_int2 points
                && displacementMember is Field_float displacement)
            {
                reportProgress?.Invoke(
                    PlateauLog.Debug(
                        "live",
                        "Terrain grid displacement texture ready. Creating GridMesh "
                        + $"({points.Value.x}x{points.Value.y}, displacement={displacement.Value:F3})."));
            }

            ResoniteBatchOperations.PendingBatchComponent pendingComponent = batchBuilder.AddComponent(
                ResolveSlotTargetId(componentEmission.ContainerTarget, pendingSlotsByPlanId),
                componentEmission.ComponentType,
                translatedMembers);
            pendingComponentsByPlanId[componentEmission.Identity] = pendingComponent;
        }

        int operationCount = batchBuilder.Actions.Count;
        Stopwatch batchStopwatch = Stopwatch.StartNew();
        BatchResponse batchResponse = await client.RunDataModelOperationBatchAsync(batchBuilder.Actions, cancellationToken);
        batchStopwatch.Stop();
        reportProgress?.Invoke(
            PlateauLog.Debug(
                "live",
                $"City object '{cityObject.DisplayName}' batch completed in {batchStopwatch.Elapsed.TotalSeconds:F3}s "
                + $"(operations={operationCount}, est_payload_bytes={EstimateBatchPayloadBytes(operationCount)})."));

        CanonicalBatchEntityMap canonicalBatchEntityMap = CanonicalBatchEntityMap.Create(batchResponse);
        canonicalBatchEntityMap.ValidateAll(batchBuilder.PendingActions);
        foreach (BatchPlanSlotLocator slotResolutionTarget in batchEmission.SlotResolutionTargets)
        {
            _ = canonicalBatchEntityMap.ResolveSlot(pendingSlotsByPlanId[slotResolutionTarget]);
        }

        foreach (BatchPlanComponentLocator componentResolutionTarget in batchEmission.ComponentResolutionTargets)
        {
            _ = canonicalBatchEntityMap.ResolveComponent(pendingComponentsByPlanId[componentResolutionTarget]);
        }
    }

    private static long EstimateBatchPayloadBytes(int operationCount)
    {
        return Math.Max(1L, operationCount) * 1024L;
    }

    private static string ResolveSlotTargetId(
        PlannedSlotTargetReference target,
        Dictionary<BatchPlanSlotLocator, ResoniteBatchOperations.PendingBatchSlot> pendingSlotsByPlanId)
    {
        return target.Match(
            static canonical => canonical.Value,
            planned => pendingSlotsByPlanId.TryGetValue(planned, out ResoniteBatchOperations.PendingBatchSlot pendingSlot)
                ? pendingSlot.LocalId.Value
                : throw new InvalidOperationException("Batch slot target did not resolve to a planned slot."));
    }

    private static string ResolveWorldElementId(
        PlannedWorldElementReference target,
        Dictionary<BatchPlanSlotLocator, ResoniteBatchOperations.PendingBatchSlot> pendingSlotsByPlanId,
        Dictionary<BatchPlanComponentLocator, ResoniteBatchOperations.PendingBatchComponent> pendingComponentsByPlanId,
        Dictionary<BatchPlanFieldLocator, ResoniteBatchOperations.BatchTemporaryFieldId> pendingFieldsByPlanId,
        ResoniteBatchOperations.BatchActionBuilder batchBuilder)
    {
        return target.Match(
            static canonicalSlot => canonicalSlot.Value,
            static canonicalComponent => canonicalComponent.Value,
            plannedSlot => pendingSlotsByPlanId.TryGetValue(plannedSlot, out ResoniteBatchOperations.PendingBatchSlot pendingSlot)
                ? pendingSlot.LocalId.Value
                : throw new InvalidOperationException("Batch world element reference did not resolve to a planned slot."),
            plannedComponent => pendingComponentsByPlanId.TryGetValue(plannedComponent, out ResoniteBatchOperations.PendingBatchComponent pendingComponent)
                ? pendingComponent.LocalId.Value
                : throw new InvalidOperationException("Batch world element reference did not resolve to a planned component."),
            plannedField => ResolveFieldId(plannedField, pendingFieldsByPlanId, batchBuilder).Value);
    }

    private static Dictionary<string, Member> TranslateMembers(
        IReadOnlyDictionary<string, PlannedMember> members,
        Dictionary<BatchPlanSlotLocator, ResoniteBatchOperations.PendingBatchSlot> pendingSlotsByPlanId,
        Dictionary<BatchPlanComponentLocator, ResoniteBatchOperations.PendingBatchComponent> pendingComponentsByPlanId,
        Dictionary<BatchPlanFieldLocator, ResoniteBatchOperations.BatchTemporaryFieldId> pendingFieldsByPlanId,
        ResoniteBatchOperations.BatchActionBuilder batchBuilder)
    {
        return members.ToDictionary(
            static pair => pair.Key,
            pair => TranslateMember(pair.Value, pendingSlotsByPlanId, pendingComponentsByPlanId, pendingFieldsByPlanId, batchBuilder),
            StringComparer.Ordinal);
    }

    private static Member TranslateMember(
        PlannedMember member,
        Dictionary<BatchPlanSlotLocator, ResoniteBatchOperations.PendingBatchSlot> pendingSlotsByPlanId,
        Dictionary<BatchPlanComponentLocator, ResoniteBatchOperations.PendingBatchComponent> pendingComponentsByPlanId,
        Dictionary<BatchPlanFieldLocator, ResoniteBatchOperations.BatchTemporaryFieldId> pendingFieldsByPlanId,
        ResoniteBatchOperations.BatchActionBuilder batchBuilder)
    {
        return member switch
        {
            PlannedLiteralMember literal => literal.Value,
            PlannedElementReferenceMember reference => new Reference
            {
                TargetID = ResolveWorldElementId(
                    reference.Target,
                    pendingSlotsByPlanId,
                    pendingComponentsByPlanId,
                    pendingFieldsByPlanId,
                    batchBuilder),
            },
            PlannedAddressableFieldMember addressableField => TranslateAddressableField(
                addressableField,
                pendingFieldsByPlanId,
                batchBuilder),
            PlannedAddressableReferenceMember addressableReference => TranslateAddressableReference(
                addressableReference,
                pendingSlotsByPlanId,
                pendingComponentsByPlanId,
                pendingFieldsByPlanId,
                batchBuilder),
            PlannedSyncListMember syncList => new SyncList
            {
                Elements = syncList.Elements
                    .Select(element => TranslateMember(
                        element,
                        pendingSlotsByPlanId,
                        pendingComponentsByPlanId,
                        pendingFieldsByPlanId,
                        batchBuilder))
                    .ToList(),
            },
            _ => throw new InvalidOperationException($"Unsupported planned member type '{member.GetType().Name}'."),
        };
    }

    private static Reference TranslateAddressableReference(
        PlannedAddressableReferenceMember addressableReference,
        Dictionary<BatchPlanSlotLocator, ResoniteBatchOperations.PendingBatchSlot> pendingSlotsByPlanId,
        Dictionary<BatchPlanComponentLocator, ResoniteBatchOperations.PendingBatchComponent> pendingComponentsByPlanId,
        Dictionary<BatchPlanFieldLocator, ResoniteBatchOperations.BatchTemporaryFieldId> pendingFieldsByPlanId,
        ResoniteBatchOperations.BatchActionBuilder batchBuilder)
    {
        string fieldId = ResolveFieldId(addressableReference.Identity, pendingFieldsByPlanId, batchBuilder).Value;
        return new Reference
        {
            ID = fieldId,
            TargetID = ResolveWorldElementId(
                addressableReference.Target,
                pendingSlotsByPlanId,
                pendingComponentsByPlanId,
                pendingFieldsByPlanId,
                batchBuilder),
        };
    }

    private static Member TranslateAddressableField(
        PlannedAddressableFieldMember addressableField,
        Dictionary<BatchPlanFieldLocator, ResoniteBatchOperations.BatchTemporaryFieldId> pendingFieldsByPlanId,
        ResoniteBatchOperations.BatchActionBuilder batchBuilder)
    {
        string fieldId = ResolveFieldId(addressableField.Identity, pendingFieldsByPlanId, batchBuilder).Value;
        return addressableField.Value switch
        {
            Field_int2 value => new Field_int2
            {
                ID = fieldId,
                Value = value.Value,
            },
            Field_bool value => new Field_bool
            {
                ID = fieldId,
                Value = value.Value,
            },
            Field_float value => new Field_float
            {
                ID = fieldId,
                Value = value.Value,
            },
            Reference value => new Reference
            {
                ID = fieldId,
                TargetID = value.TargetID,
                TargetType = value.TargetType,
            },
            _ => throw new InvalidOperationException(
                $"Unsupported planned addressable field member type '{addressableField.Value.GetType().Name}'."),
        };
    }

    private static ResoniteBatchOperations.BatchTemporaryFieldId ResolveFieldId(
        BatchPlanFieldLocator locator,
        Dictionary<BatchPlanFieldLocator, ResoniteBatchOperations.BatchTemporaryFieldId> pendingFieldsByPlanId,
        ResoniteBatchOperations.BatchActionBuilder batchBuilder)
    {
        if (pendingFieldsByPlanId.TryGetValue(locator, out ResoniteBatchOperations.BatchTemporaryFieldId fieldId))
        {
            return fieldId;
        }

        ResoniteBatchOperations.BatchTemporaryFieldId allocatedFieldId = batchBuilder.AllocateFieldId();
        pendingFieldsByPlanId[locator] = allocatedFieldId;
        return allocatedFieldId;
    }
}
