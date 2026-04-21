using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;

using PlateauResoniteLink.Domain.Importing;
using PlateauResoniteLink.Transport.ResoniteLink;

using ResoniteLink;

namespace PlateauResoniteLink.Targets.Resonite.Execution;

internal static class ResoniteBatchOperations
{
    internal readonly record struct BatchTemporarySlotId(string Value);
    internal readonly record struct BatchTemporaryComponentId(string Value);
    internal readonly record struct BatchTemporaryMessageId(string Value);

    internal readonly record struct PendingBatchSlot(
        BatchTemporarySlotId LocalId,
        BatchTemporaryMessageId MessageId,
        string SlotName);

    internal readonly record struct PendingBatchComponent(
        BatchTemporaryComponentId LocalId,
        BatchTemporaryMessageId MessageId,
        string ComponentType);

    internal readonly record struct PendingBatchOperation(
        BatchTemporaryMessageId MessageId,
        string Description);

    internal sealed class BatchOperationAccumulator
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
            return AddSlot(
                AllocateSlotId("local_slot"),
                AllocateMessageId("batch_message"),
                parentId,
                slotName,
                position,
                rotation);
        }

        public PendingBatchSlot AddSlot(
            BatchTemporarySlotId localId,
            BatchTemporaryMessageId messageId,
            string parentId,
            string slotName,
            ResoniteFloat3? position,
            ResoniteFloatQ? rotation)
        {
            Operations.Add(CreateAddSlotOperation(parentId, slotName, position, rotation, localId, messageId));
            PendingOperations.Add(new PendingBatchOperation(messageId, $"slot '{slotName}'"));
            return new PendingBatchSlot(localId, messageId, slotName);
        }

        public PendingBatchComponent AddComponent(
            string containerSlotId,
            string componentType,
            IReadOnlyDictionary<string, Member> members)
        {
            return AddComponent(
                AllocateComponentId("local_component"),
                AllocateMessageId("batch_message"),
                containerSlotId,
                componentType,
                members);
        }

        public PendingBatchComponent AddComponent(
            BatchTemporaryComponentId localId,
            BatchTemporaryMessageId messageId,
            string containerSlotId,
            string componentType,
            IReadOnlyDictionary<string, Member> members)
        {
            Operations.Add(CreateAddComponentOperation(containerSlotId, componentType, members, localId, messageId));
            PendingOperations.Add(new PendingBatchOperation(messageId, $"component '{componentType}'"));
            return new PendingBatchComponent(localId, messageId, componentType);
        }

        private BatchTemporarySlotId AllocateSlotId(string prefix)
        {
            return new BatchTemporarySlotId(
                string.Create(
                    System.Globalization.CultureInfo.InvariantCulture,
                    $"{prefix}_{batchScopeToken}_{++nextEntityId}"));
        }

        private BatchTemporaryComponentId AllocateComponentId(string prefix)
        {
            return new BatchTemporaryComponentId(
                string.Create(
                    System.Globalization.CultureInfo.InvariantCulture,
                    $"{prefix}_{batchScopeToken}_{++nextEntityId}"));
        }

        private BatchTemporaryMessageId AllocateMessageId(string prefix)
        {
            return new BatchTemporaryMessageId(
                string.Create(
                    System.Globalization.CultureInfo.InvariantCulture,
                    $"{prefix}_{batchScopeToken}_{++nextMessageId}"));
        }
    }

    public static AddSlot CreateAddSlotOperation(
        string parentId,
        string slotName,
        ResoniteFloat3? position,
        ResoniteFloatQ? rotation,
        PendingBatchSlot pendingSlot)
    {
        return CreateAddSlotOperation(
            parentId,
            slotName,
            position,
            rotation,
            pendingSlot.LocalId,
            pendingSlot.MessageId);
    }

    public static AddSlot CreateAddSlotOperation(
        string parentId,
        string slotName,
        ResoniteFloat3? position,
        ResoniteFloatQ? rotation,
        BatchTemporarySlotId requestedSlotId,
        BatchTemporaryMessageId messageId)
    {
        return CreateAddSlotOperation(
            parentId,
            slotName,
            position,
            rotation,
            requestedSlotId.Value,
            messageId.Value);
    }

    public static AddSlot CreateAddSlotOperation(
        string parentId,
        string slotName,
        ResoniteFloat3? position,
        ResoniteFloatQ? rotation,
        string? requestedSlotId = null,
        string? messageId = null)
    {
        return new AddSlot
        {
            MessageID = messageId,
            Data = new Slot
            {
                ID = requestedSlotId,
                Parent = new Reference
                {
                    TargetID = parentId,
                },
                Name = new Field_string
                {
                    Value = slotName,
                },
                Position = position is null ? null : CreateFloat3(position),
                Rotation = rotation is null ? null : CreateFloatQ(rotation),
            },
        };
    }

    public static AddComponent CreateAddComponentOperation(
        string containerSlotId,
        string componentType,
        IReadOnlyDictionary<string, Member> members,
        PendingBatchComponent pendingComponent)
    {
        return CreateAddComponentOperation(
            containerSlotId,
            componentType,
            members,
            pendingComponent.LocalId,
            pendingComponent.MessageId);
    }

    public static AddComponent CreateAddComponentOperation(
        string containerSlotId,
        string componentType,
        IReadOnlyDictionary<string, Member> members,
        BatchTemporaryComponentId requestedComponentId,
        BatchTemporaryMessageId messageId)
    {
        return CreateAddComponentOperation(
            containerSlotId,
            componentType,
            members,
            requestedComponentId.Value,
            messageId.Value);
    }

    public static AddComponent CreateAddComponentOperation(
        string containerSlotId,
        string componentType,
        IReadOnlyDictionary<string, Member> members,
        string? requestedComponentId = null,
        string? messageId = null)
    {
        return new AddComponent
        {
            MessageID = messageId,
            ContainerSlotId = containerSlotId,
            Data = new Component
            {
                ID = requestedComponentId,
                ComponentType = componentType,
                Members = new Dictionary<string, Member>(members, StringComparer.Ordinal),
            },
        };
    }

    public static Field_float3 CreateFloat3(ResoniteFloat3 value)
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

    public static Field_floatQ CreateFloatQ(ResoniteFloatQ value)
    {
        return new Field_floatQ
        {
            Value = new floatQ
            {
                x = (float)value.X,
                y = (float)value.Y,
                z = (float)value.Z,
                w = (float)value.W,
            },
        };
    }
}

internal sealed class CanonicalBatchEntityMap
{
    private readonly Dictionary<string, Response> responsesByMessageId;
    private readonly Queue<Response> responsesWithoutMessageId;

    private CanonicalBatchEntityMap(
        Dictionary<string, Response> responsesByMessageId,
        Queue<Response> responsesWithoutMessageId)
    {
        this.responsesByMessageId = responsesByMessageId;
        this.responsesWithoutMessageId = responsesWithoutMessageId;
    }

    public static CanonicalBatchEntityMap Create(BatchResponse batchResponse)
    {
        ArgumentNullException.ThrowIfNull(batchResponse);
        return new CanonicalBatchEntityMap(
            (batchResponse.Responses ?? [])
                .Where(static response => !string.IsNullOrWhiteSpace(response.SourceMessageID))
                .ToDictionary(response => response.SourceMessageID!, StringComparer.Ordinal),
            new Queue<Response>(
                (batchResponse.Responses ?? [])
                    .Where(static response => string.IsNullOrWhiteSpace(response.SourceMessageID))));
    }

    public CreatedSlot ResolveSlot(ResoniteBatchOperations.PendingBatchSlot pendingSlot)
    {
        Response response = ResolveResponse(pendingSlot.MessageId);
        if (response is not NewEntityId newEntityId || string.IsNullOrWhiteSpace(newEntityId.EntityId))
        {
            throw new InvalidOperationException(
                $"Batch response for slot '{pendingSlot.SlotName}' did not include a canonical slot ID.");
        }

        return new CreatedSlot(newEntityId.EntityId, pendingSlot.SlotName);
    }

    public CreatedComponent ResolveComponent(ResoniteBatchOperations.PendingBatchComponent pendingComponent)
    {
        Response response = ResolveResponse(pendingComponent.MessageId);
        if (response is not NewEntityId newEntityId || string.IsNullOrWhiteSpace(newEntityId.EntityId))
        {
            throw new InvalidOperationException(
                $"Batch response for component '{pendingComponent.ComponentType}' did not include a canonical component ID.");
        }

        return new CreatedComponent(newEntityId.EntityId, pendingComponent.ComponentType);
    }

    public void ValidateAll(IReadOnlyList<ResoniteBatchOperations.PendingBatchOperation> pendingOperations)
    {
        ArgumentNullException.ThrowIfNull(pendingOperations);
        foreach (ResoniteBatchOperations.PendingBatchOperation pendingOperation in pendingOperations)
        {
            _ = ResolveResponse(
                pendingOperation.MessageId,
                $"validate {pendingOperation.Description}");
        }
    }

    private Response ResolveResponse(ResoniteBatchOperations.BatchTemporaryMessageId messageId)
    {
        return ResolveResponse(messageId, $"resolve batch message '{messageId.Value}'");
    }

    private Response ResolveResponse(ResoniteBatchOperations.BatchTemporaryMessageId messageId, string operationName)
    {
        if (!responsesByMessageId.TryGetValue(messageId.Value, out Response? response))
        {
            if (responsesWithoutMessageId.Count == 0)
            {
                throw new InvalidOperationException($"Batch response did not include message '{messageId.Value}'.");
            }

            response = responsesWithoutMessageId.Dequeue();
        }

        ResoniteLinkClient.EnsureSuccess(response, operationName);
        return response;
    }
}
