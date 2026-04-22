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
        Dictionary<string, ResoniteBatchOperations.PendingBatchSlot> pendingSlotsByPlanId = new(StringComparer.Ordinal);
        Dictionary<string, ResoniteBatchOperations.PendingBatchComponent> pendingComponentsByPlanId = new(StringComparer.Ordinal);

        foreach (PlannedBatchSlotEmission slotEmission in batchEmission.SlotEmissions)
        {
            ResoniteBatchOperations.PendingBatchSlot pendingSlot = batchBuilder.AddSlot(
                ResolveTargetId(slotEmission.ParentId, pendingSlotsByPlanId, pendingComponentsByPlanId),
                slotEmission.SlotName,
                slotEmission.Position,
                slotEmission.Rotation);
            pendingSlotsByPlanId[slotEmission.Identity.Value] = pendingSlot;
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
                ResolveTargetId(componentEmission.ContainerId, pendingSlotsByPlanId, pendingComponentsByPlanId),
                componentEmission.ComponentType,
                translatedMembers);
            pendingComponentsByPlanId[componentEmission.Identity.Value] = pendingComponent;
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
        foreach (BatchPlanEntityId slotResolutionTarget in batchEmission.SlotResolutionTargets)
        {
            _ = canonicalBatchEntityMap.ResolveSlot(pendingSlotsByPlanId[slotResolutionTarget.Value]);
        }

        foreach (BatchPlanEntityId componentResolutionTarget in batchEmission.ComponentResolutionTargets)
        {
            _ = canonicalBatchEntityMap.ResolveComponent(pendingComponentsByPlanId[componentResolutionTarget.Value]);
        }
    }

    private static long EstimateBatchPayloadBytes(int operationCount)
    {
        return Math.Max(1L, operationCount) * 1024L;
    }

    private static string ResolveTargetId(
        string targetId,
        IReadOnlyDictionary<string, ResoniteBatchOperations.PendingBatchSlot> pendingSlotsByPlanId,
        IReadOnlyDictionary<string, ResoniteBatchOperations.PendingBatchComponent> pendingComponentsByPlanId)
    {
        if (string.IsNullOrWhiteSpace(targetId))
        {
            return targetId;
        }

        if (pendingSlotsByPlanId.TryGetValue(targetId, out ResoniteBatchOperations.PendingBatchSlot pendingSlot))
        {
            return pendingSlot.LocalId.Value;
        }

        if (pendingComponentsByPlanId.TryGetValue(targetId, out ResoniteBatchOperations.PendingBatchComponent pendingComponent))
        {
            return pendingComponent.LocalId.Value;
        }

        return targetId;
    }

    private static Dictionary<string, Member> TranslateMembers(
        IReadOnlyDictionary<string, Member> members,
        IReadOnlyDictionary<string, ResoniteBatchOperations.PendingBatchSlot> pendingSlotsByPlanId,
        IReadOnlyDictionary<string, ResoniteBatchOperations.PendingBatchComponent> pendingComponentsByPlanId)
    {
        return members.ToDictionary(
            static pair => pair.Key,
            pair => TranslateMember(pair.Value, pendingSlotsByPlanId, pendingComponentsByPlanId),
            StringComparer.Ordinal);
    }

    private static Member TranslateMember(
        Member member,
        IReadOnlyDictionary<string, ResoniteBatchOperations.PendingBatchSlot> pendingSlotsByPlanId,
        IReadOnlyDictionary<string, ResoniteBatchOperations.PendingBatchComponent> pendingComponentsByPlanId)
    {
        return member switch
        {
            Reference reference => new Reference
            {
                TargetID = ResolveTargetId(reference.TargetID, pendingSlotsByPlanId, pendingComponentsByPlanId),
            },
            SyncList syncList => new SyncList
            {
                Elements = syncList.Elements
                    .Select(element => TranslateMember(element, pendingSlotsByPlanId, pendingComponentsByPlanId))
                    .ToList(),
            },
            _ => member,
        };
    }

}
