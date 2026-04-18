using Plateau.ResoniteLink.Domain.Importing;

using ResoniteLink;

namespace Plateau.ResoniteLink.Targets.Resonite;

internal static class ResoniteBatchOperations
{
    internal readonly record struct PendingBatchSlot(
        string LocalId,
        string MessageId,
        string SlotName);

    internal readonly record struct PendingBatchComponent(
        string LocalId,
        string MessageId,
        string ComponentType);

    internal readonly record struct PendingBatchOperation(
        string MessageId,
        string Description);

    public static AddSlot CreateAddSlotOperation(
        string parentId,
        string slotName,
        ResoniteFloat3? position,
        ResoniteFloatQ? rotation,
        PendingBatchSlot pendingSlot)
    {
        return CreateAddSlotOperation(parentId, slotName, position, rotation, pendingSlot.LocalId, pendingSlot.MessageId);
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
                .ToDictionary(response => response.SourceMessageID, StringComparer.Ordinal),
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

    private Response ResolveResponse(string messageId)
    {
        return ResolveResponse(messageId, $"resolve batch message '{messageId}'");
    }

    private Response ResolveResponse(string messageId, string operationName)
    {
        if (!responsesByMessageId.TryGetValue(messageId, out Response? response))
        {
            if (responsesWithoutMessageId.Count == 0)
            {
                throw new InvalidOperationException($"Batch response did not include message '{messageId}'.");
            }

            response = responsesWithoutMessageId.Dequeue();
        }

        ResoniteLinkClient.EnsureSuccess(response, operationName);
        return response;
    }
}
