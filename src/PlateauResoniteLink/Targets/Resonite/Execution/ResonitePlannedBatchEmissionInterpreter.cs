using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

using PlateauResoniteLink.Diagnostics;

using PlateauResoniteLink.Transport.ResoniteLink;

using ResoniteLink;

namespace PlateauResoniteLink.Targets.Resonite.Execution;

internal static class PlannedBatchEmissionInterpreter
{
    public static Task ExecuteAsync(
        IResoniteLinkClient client,
        ResoniteConstructionCityObject cityObject,
        PlannedBatchEmission batchEmission,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        return ExecuteCoreAsync(client, cityObject, batchEmission, logger, cancellationToken);
    }

    private static async Task ExecuteCoreAsync(
        IResoniteLinkClient client,
        ResoniteConstructionCityObject cityObject,
        PlannedBatchEmission batchEmission,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        ResoniteBatchOperations.BatchActionBuilder batchBuilder = new();
        Dictionary<PlannedBatchSlotEmission, ResoniteBatchOperations.PendingBatchSlot> pendingSlotsByPlanId =
            new(ReferenceEqualityComparer.Instance);
        Dictionary<PlannedBatchComponentEmission, ResoniteBatchOperations.PendingBatchComponent> pendingComponentsByPlanId =
            new(ReferenceEqualityComparer.Instance);
        Dictionary<PlannedFieldReference, ResoniteBatchOperations.BatchTemporaryFieldId> pendingFieldsByPlanId =
            new(ReferenceEqualityComparer.Instance);
        List<ResoniteBatchOperations.PendingBatchSlot> pendingSlots = [];
        List<ResoniteBatchOperations.PendingBatchComponent> pendingComponents = [];

        foreach (PlannedBatchSlotEmission slotEmission in batchEmission.SlotEmissions)
        {
            ResoniteBatchOperations.PendingBatchSlot pendingSlot = batchBuilder.AddSlot(
                ResolveSlotTargetId(slotEmission.ParentTarget, pendingSlotsByPlanId),
                slotEmission.SlotName,
                slotEmission.Position,
                slotEmission.Rotation,
                slotEmission.OrderOffset);
            pendingSlotsByPlanId[slotEmission] = pendingSlot;
            pendingSlots.Add(pendingSlot);
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
                logger.WriteDebug(
                    "Terrain grid displacement texture ready. Creating GridMesh ({Width}x{Height}, displacement={Displacement:F3}).",
                    points.Value.x,
                    points.Value.y,
                    displacement.Value);
            }

            ResoniteBatchOperations.PendingBatchComponent pendingComponent = batchBuilder.AddComponent(
                ResolveSlotTargetId(componentEmission.ContainerTarget, pendingSlotsByPlanId),
                componentEmission.ComponentType,
                translatedMembers);
            pendingComponentsByPlanId[componentEmission] = pendingComponent;
            pendingComponents.Add(pendingComponent);
        }

        int operationCount = batchBuilder.Actions.Count;
        Stopwatch batchStopwatch = Stopwatch.StartNew();
        BatchResponse batchResponse = await client.RunDataModelOperationBatchAsync(batchBuilder.Actions, cancellationToken);
        batchStopwatch.Stop();
        logger.WriteDebug(
            "City object '{DisplayName}' batch completed in {ElapsedSeconds:F3}s (operations={OperationCount}, est_payload_bytes={EstimatedPayloadBytes}).",
            cityObject.DisplayName,
            batchStopwatch.Elapsed.TotalSeconds,
            operationCount,
            EstimateBatchPayloadBytes(operationCount));

        CanonicalBatchEntityMap canonicalBatchEntityMap = CanonicalBatchEntityMap.Create(batchResponse);
        foreach (ResoniteBatchOperations.PendingBatchSlot pendingSlot in pendingSlots)
        {
            _ = canonicalBatchEntityMap.ResolveSlot(pendingSlot);
        }

        foreach (ResoniteBatchOperations.PendingBatchComponent pendingComponent in pendingComponents)
        {
            _ = canonicalBatchEntityMap.ResolveComponent(pendingComponent);
        }
    }

    private static long EstimateBatchPayloadBytes(int operationCount)
    {
        return Math.Max(1L, operationCount) * 1024L;
    }

    private static string ResolveSlotTargetId(
        PlannedSlotTargetReference target,
        Dictionary<PlannedBatchSlotEmission, ResoniteBatchOperations.PendingBatchSlot> pendingSlotsByPlanId)
    {
        return target switch
        {
            PlannedSlotTargetReference.CanonicalSlotTarget canonicalSlot => canonicalSlot.Locator.Value,
            PlannedSlotTargetReference.BatchSlotTarget plannedSlot => pendingSlotsByPlanId[plannedSlot.Slot].LocalId.Value,
            _ => throw new InvalidOperationException($"Unsupported planned slot target '{target.GetType().Name}'."),
        };
    }

    private static string ResolveWorldElementId(
        PlannedWorldElementReference target,
        Dictionary<PlannedBatchSlotEmission, ResoniteBatchOperations.PendingBatchSlot> pendingSlotsByPlanId,
        Dictionary<PlannedBatchComponentEmission, ResoniteBatchOperations.PendingBatchComponent> pendingComponentsByPlanId,
        Dictionary<PlannedFieldReference, ResoniteBatchOperations.BatchTemporaryFieldId> pendingFieldsByPlanId,
        ResoniteBatchOperations.BatchActionBuilder batchBuilder)
    {
        return target switch
        {
            PlannedWorldElementReference.CanonicalSlotElement canonicalSlot => canonicalSlot.Locator.Value,
            PlannedWorldElementReference.CanonicalComponentElement canonicalComponent => canonicalComponent.Locator.Value,
            PlannedWorldElementReference.BatchSlotElement plannedSlot => pendingSlotsByPlanId[plannedSlot.Slot].LocalId.Value,
            PlannedWorldElementReference.BatchComponentElement plannedComponent => pendingComponentsByPlanId[plannedComponent.Component].LocalId.Value,
            PlannedWorldElementReference.BatchFieldElement plannedField => ResolveFieldId(plannedField.Field, pendingFieldsByPlanId, batchBuilder).Value,
            _ => throw new InvalidOperationException($"Unsupported planned world element target '{target.GetType().Name}'."),
        };
    }

    private static Dictionary<string, Member> TranslateMembers(
        IReadOnlyDictionary<string, PlannedMember> members,
        Dictionary<PlannedBatchSlotEmission, ResoniteBatchOperations.PendingBatchSlot> pendingSlotsByPlanId,
        Dictionary<PlannedBatchComponentEmission, ResoniteBatchOperations.PendingBatchComponent> pendingComponentsByPlanId,
        Dictionary<PlannedFieldReference, ResoniteBatchOperations.BatchTemporaryFieldId> pendingFieldsByPlanId,
        ResoniteBatchOperations.BatchActionBuilder batchBuilder)
    {
        return members.ToDictionary(
            static pair => pair.Key,
            pair => TranslateMember(pair.Value, pendingSlotsByPlanId, pendingComponentsByPlanId, pendingFieldsByPlanId, batchBuilder),
            StringComparer.Ordinal);
    }

    private static Member TranslateMember(
        PlannedMember member,
        Dictionary<PlannedBatchSlotEmission, ResoniteBatchOperations.PendingBatchSlot> pendingSlotsByPlanId,
        Dictionary<PlannedBatchComponentEmission, ResoniteBatchOperations.PendingBatchComponent> pendingComponentsByPlanId,
        Dictionary<PlannedFieldReference, ResoniteBatchOperations.BatchTemporaryFieldId> pendingFieldsByPlanId,
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
                addressableReference.Field,
                addressableReference.Target,
                pendingSlotsByPlanId,
                pendingComponentsByPlanId,
                pendingFieldsByPlanId,
                batchBuilder),
            PlannedSyncListMember list => new SyncList
            {
                Elements = list.Elements
                    .Select(element => TranslateMember(
                        element,
                        pendingSlotsByPlanId,
                        pendingComponentsByPlanId,
                        pendingFieldsByPlanId,
                        batchBuilder))
                    .ToList(),
            },
            _ => throw new InvalidOperationException($"Unsupported planned member '{member.GetType().Name}'."),
        };
    }

    private static Reference TranslateAddressableReference(
        PlannedFieldReference field,
        PlannedWorldElementReference target,
        Dictionary<PlannedBatchSlotEmission, ResoniteBatchOperations.PendingBatchSlot> pendingSlotsByPlanId,
        Dictionary<PlannedBatchComponentEmission, ResoniteBatchOperations.PendingBatchComponent> pendingComponentsByPlanId,
        Dictionary<PlannedFieldReference, ResoniteBatchOperations.BatchTemporaryFieldId> pendingFieldsByPlanId,
        ResoniteBatchOperations.BatchActionBuilder batchBuilder)
    {
        string fieldId = ResolveFieldId(field, pendingFieldsByPlanId, batchBuilder).Value;
        return new Reference
        {
            ID = fieldId,
            TargetID = ResolveWorldElementId(
                target,
                pendingSlotsByPlanId,
                pendingComponentsByPlanId,
                pendingFieldsByPlanId,
                batchBuilder),
        };
    }

    private static Member TranslateAddressableField(
        PlannedAddressableFieldMember addressableField,
        Dictionary<PlannedFieldReference, ResoniteBatchOperations.BatchTemporaryFieldId> pendingFieldsByPlanId,
        ResoniteBatchOperations.BatchActionBuilder batchBuilder)
    {
        string fieldId = ResolveFieldId(addressableField.Field, pendingFieldsByPlanId, batchBuilder).Value;
        return addressableField.Bind(fieldId);
    }

    private static ResoniteBatchOperations.BatchTemporaryFieldId ResolveFieldId(
        PlannedFieldReference field,
        Dictionary<PlannedFieldReference, ResoniteBatchOperations.BatchTemporaryFieldId> pendingFieldsByPlanId,
        ResoniteBatchOperations.BatchActionBuilder batchBuilder)
    {
        if (pendingFieldsByPlanId.TryGetValue(field, out ResoniteBatchOperations.BatchTemporaryFieldId fieldId))
        {
            return fieldId;
        }

        ResoniteBatchOperations.BatchTemporaryFieldId allocatedFieldId = batchBuilder.AllocateFieldId();
        pendingFieldsByPlanId[field] = allocatedFieldId;
        return allocatedFieldId;
    }
}
