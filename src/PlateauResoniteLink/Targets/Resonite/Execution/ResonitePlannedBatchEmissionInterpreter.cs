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
                pendingComponentsByPlanId);
            if (string.Equals(componentEmission.ComponentType, "[FrooxEngine]FrooxEngine.GridMesh", StringComparison.Ordinal)
                && translatedMembers.TryGetValue("Points", out Member? pointsMember)
                && translatedMembers.TryGetValue("DisplacementMagnitude", out Member? displacementMember)
                && pointsMember is Field_int2 points
                && displacementMember is Field_float displacement)
            {
                reportProgress?.Invoke(
                    $"[live] HeightMap texture ready. Creating GridMesh "
                    + $"({points.Value.x}x{points.Value.y}, displacement={displacement.Value:F3}).");
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
        if (target.Canonical is ResoniteSlotLocator canonical)
        {
            return canonical.Value;
        }

        if (target.Planned is BatchPlanSlotLocator planned
            && pendingSlotsByPlanId.TryGetValue(planned, out ResoniteBatchOperations.PendingBatchSlot pendingSlot))
        {
            return pendingSlot.LocalId.Value;
        }

        throw new InvalidOperationException("Batch slot target did not resolve to a planned or canonical slot.");
    }

    private static string ResolveWorldElementId(
        PlannedWorldElementReference target,
        Dictionary<BatchPlanSlotLocator, ResoniteBatchOperations.PendingBatchSlot> pendingSlotsByPlanId,
        Dictionary<BatchPlanComponentLocator, ResoniteBatchOperations.PendingBatchComponent> pendingComponentsByPlanId)
    {
        if (target.CanonicalSlot is ResoniteSlotLocator canonicalSlot)
        {
            return canonicalSlot.Value;
        }

        if (target.CanonicalComponent is ResoniteComponentLocator canonicalComponent)
        {
            return canonicalComponent.Value;
        }

        if (target.PlannedSlot is BatchPlanSlotLocator plannedSlot
            && pendingSlotsByPlanId.TryGetValue(plannedSlot, out ResoniteBatchOperations.PendingBatchSlot pendingSlot))
        {
            return pendingSlot.LocalId.Value;
        }

        if (target.PlannedComponent is BatchPlanComponentLocator plannedComponent
            && pendingComponentsByPlanId.TryGetValue(plannedComponent, out ResoniteBatchOperations.PendingBatchComponent pendingComponent))
        {
            return pendingComponent.LocalId.Value;
        }

        throw new InvalidOperationException("Batch world element reference did not resolve to a planned or canonical entity.");
    }

    private static Dictionary<string, Member> TranslateMembers(
        IReadOnlyDictionary<string, PlannedMember> members,
        Dictionary<BatchPlanSlotLocator, ResoniteBatchOperations.PendingBatchSlot> pendingSlotsByPlanId,
        Dictionary<BatchPlanComponentLocator, ResoniteBatchOperations.PendingBatchComponent> pendingComponentsByPlanId)
    {
        return members.ToDictionary(
            static pair => pair.Key,
            pair => TranslateMember(pair.Value, pendingSlotsByPlanId, pendingComponentsByPlanId),
            StringComparer.Ordinal);
    }

    private static Member TranslateMember(
        PlannedMember member,
        Dictionary<BatchPlanSlotLocator, ResoniteBatchOperations.PendingBatchSlot> pendingSlotsByPlanId,
        Dictionary<BatchPlanComponentLocator, ResoniteBatchOperations.PendingBatchComponent> pendingComponentsByPlanId)
    {
        return member switch
        {
            PlannedLiteralMember literal => literal.Value,
            PlannedElementReferenceMember reference => new Reference
            {
                TargetID = ResolveWorldElementId(reference.Target, pendingSlotsByPlanId, pendingComponentsByPlanId),
            },
            PlannedSyncListMember syncList => new SyncList
            {
                Elements = syncList.Elements
                    .Select(element => TranslateMember(element, pendingSlotsByPlanId, pendingComponentsByPlanId))
                    .ToList(),
            },
            _ => throw new InvalidOperationException($"Unsupported planned member type '{member.GetType().Name}'."),
        };
    }
}
