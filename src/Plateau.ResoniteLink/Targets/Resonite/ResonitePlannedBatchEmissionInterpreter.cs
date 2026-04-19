using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;

using Plateau.ResoniteLink.Application.Logging;
using Plateau.ResoniteLink.Domain.Importing;

using static Plateau.ResoniteLink.Targets.Resonite.Execution.ResoniteBatchOperations;

using ResoniteLink;

namespace Plateau.ResoniteLink.Targets.Resonite.Execution;

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
        BatchOperationAccumulator batchBuilder = new();
        Dictionary<string, PendingBatchSlot> pendingSlotsByPlanId = new(StringComparer.Ordinal);
        Dictionary<string, PendingBatchComponent> pendingComponentsByPlanId = new(StringComparer.Ordinal);

        foreach (PlannedBatchSlotEmission slotEmission in batchEmission.SlotEmissions)
        {
            PendingBatchSlot pendingSlot = batchBuilder.AddSlot(
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

            PendingBatchComponent pendingComponent = batchBuilder.AddComponent(
                ResolveTargetId(componentEmission.ContainerId, pendingSlotsByPlanId, pendingComponentsByPlanId),
                componentEmission.ComponentType,
                translatedMembers);
            pendingComponentsByPlanId[componentEmission.Identity.Value] = pendingComponent;
        }

        int operationCount = batchBuilder.Operations.Count;
        Stopwatch batchStopwatch = Stopwatch.StartNew();
        BatchResponse batchResponse = await client.RunDataModelOperationBatchAsync(batchBuilder.Operations, cancellationToken);
        batchStopwatch.Stop();
        reportProgress?.Invoke(
            PlateauLog.Debug(
                "live",
                $"City object '{cityObject.DisplayName}' batch completed in {batchStopwatch.Elapsed.TotalSeconds:F3}s "
                + $"(operations={operationCount}, est_payload_bytes={EstimateBatchPayloadBytes(operationCount)})."));

        CanonicalBatchEntityMap canonicalBatchEntityMap = CanonicalBatchEntityMap.Create(batchResponse);
        canonicalBatchEntityMap.ValidateAll(batchBuilder.PendingOperations);
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
        IReadOnlyDictionary<string, PendingBatchSlot> pendingSlotsByPlanId,
        IReadOnlyDictionary<string, PendingBatchComponent> pendingComponentsByPlanId)
    {
        if (string.IsNullOrWhiteSpace(targetId))
        {
            return targetId;
        }

        if (pendingSlotsByPlanId.TryGetValue(targetId, out PendingBatchSlot pendingSlot))
        {
            return pendingSlot.LocalId;
        }

        if (pendingComponentsByPlanId.TryGetValue(targetId, out PendingBatchComponent pendingComponent))
        {
            return pendingComponent.LocalId;
        }

        return targetId;
    }

    private static Dictionary<string, Member> TranslateMembers(
        IReadOnlyDictionary<string, Member> members,
        IReadOnlyDictionary<string, PendingBatchSlot> pendingSlotsByPlanId,
        IReadOnlyDictionary<string, PendingBatchComponent> pendingComponentsByPlanId)
    {
        return members.ToDictionary(
            static pair => pair.Key,
            pair => TranslateMember(pair.Value, pendingSlotsByPlanId, pendingComponentsByPlanId),
            StringComparer.Ordinal);
    }

    private static Member TranslateMember(
        Member member,
        IReadOnlyDictionary<string, PendingBatchSlot> pendingSlotsByPlanId,
        IReadOnlyDictionary<string, PendingBatchComponent> pendingComponentsByPlanId)
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

    private sealed class BatchOperationAccumulator
    {
        private readonly string batchScopeToken = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8));
        private int nextEntityId;
        private int nextMessageId;

        public List<DataModelOperation> Operations { get; } = [];
        public List<PendingBatchOperation> PendingOperations { get; } = [];

        public PendingBatchSlot AddSlot(
            string parentId,
            string slotName,
            ResoniteFloat3? position,
            ResoniteFloatQ? rotation)
        {
            string localId = AllocateEntityId("local_slot");
            string messageId = AllocateMessageId();
            Operations.Add(CreateAddSlotOperation(parentId, slotName, position, rotation, localId, messageId));
            PendingOperations.Add(new PendingBatchOperation(messageId, $"slot '{slotName}'"));
            return new PendingBatchSlot(localId, messageId, slotName);
        }

        public PendingBatchComponent AddComponent(
            string containerSlotId,
            string componentType,
            IReadOnlyDictionary<string, Member> members)
        {
            string localId = AllocateEntityId("local_component");
            string messageId = AllocateMessageId();
            Operations.Add(CreateAddComponentOperation(containerSlotId, componentType, members, localId, messageId));
            PendingOperations.Add(new PendingBatchOperation(messageId, $"component '{componentType}'"));
            return new PendingBatchComponent(localId, messageId, componentType);
        }

        private string AllocateEntityId(string prefix)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{prefix}_{batchScopeToken}_{++nextEntityId}");
        }

        private string AllocateMessageId()
        {
            return string.Create(CultureInfo.InvariantCulture, $"batch_message_{batchScopeToken}_{++nextMessageId}");
        }
    }
}
