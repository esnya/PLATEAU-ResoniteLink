using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;

using PlateauResoniteLink.Transport.ResoniteLink;

using ResoniteLink;

namespace PlateauResoniteLink.Targets.Resonite.Execution;

internal static class ResoniteBatchOperations
{
    internal readonly record struct BatchTemporarySlotId(string Value);
    internal readonly record struct BatchTemporaryComponentId(string Value);
    internal readonly record struct BatchTemporaryFieldId(string Value);
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

    public static PendingBatchSlot CreatePendingSlot(
        string prefix,
        string slotName,
        string batchScopeToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);
        ArgumentException.ThrowIfNullOrWhiteSpace(slotName);
        ArgumentException.ThrowIfNullOrWhiteSpace(batchScopeToken);

        return new PendingBatchSlot(
            CreateTemporarySlotId(prefix, batchScopeToken),
            CreateTemporaryMessageId(prefix, batchScopeToken),
            slotName);
    }

    public static PendingBatchComponent CreatePendingComponent(
        string prefix,
        string componentType,
        string batchScopeToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);
        ArgumentException.ThrowIfNullOrWhiteSpace(componentType);
        ArgumentException.ThrowIfNullOrWhiteSpace(batchScopeToken);

        return new PendingBatchComponent(
            CreateTemporaryComponentId(prefix, batchScopeToken),
            CreateTemporaryMessageId(prefix, batchScopeToken),
            componentType);
    }

    public static string CreateBatchScopeToken(int byteCount = 4)
    {
        return Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(byteCount));
    }

    internal sealed class BatchActionBuilder
    {
        private readonly string batchScopeToken = CreateBatchScopeToken(byteCount: 8);
        private int nextEntityId;
        private int nextMessageId;

        public List<DataModelOperation> Actions { get; } = [];
        public List<PendingBatchOperation> PendingActions { get; } = [];

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
            Actions.Add(CreateAddSlotOperation(parentId, slotName, position, rotation, localId, messageId));
            PendingActions.Add(new PendingBatchOperation(messageId, $"slot '{slotName}'"));
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
            Actions.Add(CreateAddComponentOperation(containerSlotId, componentType, members, localId, messageId));
            PendingActions.Add(new PendingBatchOperation(messageId, $"component '{componentType}'"));
            return new PendingBatchComponent(localId, messageId, componentType);
        }

        public BatchTemporaryFieldId AllocateFieldId()
        {
            return CreateTemporaryFieldId("local_field", batchScopeToken, ++nextEntityId);
        }

        private BatchTemporarySlotId AllocateSlotId(string prefix)
        {
            return CreateTemporarySlotId(prefix, batchScopeToken, ++nextEntityId);
        }

        private BatchTemporaryComponentId AllocateComponentId(string prefix)
        {
            return CreateTemporaryComponentId(prefix, batchScopeToken, ++nextEntityId);
        }

        private BatchTemporaryMessageId AllocateMessageId(string prefix)
        {
            return CreateTemporaryMessageId(prefix, batchScopeToken, ++nextMessageId);
        }
    }

    private static BatchTemporarySlotId CreateTemporarySlotId(
        string prefix,
        string batchScopeToken,
        int? sequence = null)
    {
        return new BatchTemporarySlotId(
            FormatRequestLocalId(prefix, batchScopeToken, sequence));
    }

    private static BatchTemporaryComponentId CreateTemporaryComponentId(
        string prefix,
        string batchScopeToken,
        int? sequence = null)
    {
        return new BatchTemporaryComponentId(
            FormatRequestLocalId(prefix, batchScopeToken, sequence));
    }

    private static BatchTemporaryFieldId CreateTemporaryFieldId(
        string prefix,
        string batchScopeToken,
        int? sequence = null)
    {
        return new BatchTemporaryFieldId(
            FormatRequestLocalId(prefix, batchScopeToken, sequence));
    }

    private static BatchTemporaryMessageId CreateTemporaryMessageId(
        string prefix,
        string batchScopeToken,
        int? sequence = null)
    {
        return new BatchTemporaryMessageId(
            FormatRequestLocalId(string.Concat(prefix, "_message"), batchScopeToken, sequence));
    }

    private static string FormatRequestLocalId(string prefix, string batchScopeToken, int? sequence)
    {
        return sequence.HasValue
            ? string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"{prefix}_{batchScopeToken}_{sequence.Value}")
            : string.Concat(prefix, "_", batchScopeToken);
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

    private CanonicalBatchEntityMap(Dictionary<string, Response> responsesByMessageId)
    {
        this.responsesByMessageId = responsesByMessageId;
    }

    public static CanonicalBatchEntityMap Create(
        BatchResponse batchResponse,
        IReadOnlyList<ResoniteBatchOperations.PendingBatchOperation> pendingOperations)
    {
        ArgumentNullException.ThrowIfNull(batchResponse);
        ArgumentNullException.ThrowIfNull(pendingOperations);

        IReadOnlyList<Response> responses = batchResponse.Responses ?? [];
        return new CanonicalBatchEntityMap(CorrelateResponses(responses, pendingOperations));
    }

    public CreatedSlot ResolveSlot(ResoniteBatchOperations.PendingBatchSlot pendingSlot)
    {
        Response response = ResolveResponse(pendingSlot.MessageId);
        if (response is not NewEntityId newEntityId || string.IsNullOrWhiteSpace(newEntityId.EntityId))
        {
            throw new InvalidOperationException(
                $"Batch response for slot '{pendingSlot.SlotName}' did not include a canonical slot ID.");
        }

        return new CreatedSlot(new ResoniteSlotLocator(newEntityId.EntityId), pendingSlot.SlotName);
    }

    public CreatedComponent ResolveComponent(ResoniteBatchOperations.PendingBatchComponent pendingComponent)
    {
        Response response = ResolveResponse(pendingComponent.MessageId);
        if (response is not NewEntityId newEntityId || string.IsNullOrWhiteSpace(newEntityId.EntityId))
        {
            throw new InvalidOperationException(
                $"Batch response for component '{pendingComponent.ComponentType}' did not include a canonical component ID.");
        }

        return new CreatedComponent(new ResoniteComponentLocator(newEntityId.EntityId), pendingComponent.ComponentType);
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
            throw new InvalidOperationException($"Batch response did not include message '{messageId.Value}'.");
        }

        ResoniteLinkClient.EnsureSuccess(response, operationName);
        return response;
    }

    private static Dictionary<string, Response> CorrelateResponses(
        IReadOnlyList<Response> responses,
        IReadOnlyList<ResoniteBatchOperations.PendingBatchOperation> pendingOperations)
    {
        if (responses.Count == 0 && pendingOperations.Count == 0)
        {
            return new Dictionary<string, Response>(StringComparer.Ordinal);
        }

        bool anyMessageId = responses.Any(static response => !string.IsNullOrWhiteSpace(response.SourceMessageID));
        bool anyMissingMessageId = responses.Any(static response => string.IsNullOrWhiteSpace(response.SourceMessageID));
        if (anyMessageId && anyMissingMessageId)
        {
            throw new InvalidOperationException("Batch response mixed message-correlated and ordered-only responses.");
        }

        if (!anyMessageId)
        {
            return CorrelateOrderedResponses(responses, pendingOperations);
        }

        Dictionary<string, Response> correlated = new(StringComparer.Ordinal);
        foreach (Response response in responses)
        {
            string messageId = response.SourceMessageID!;
            if (!correlated.TryAdd(messageId, response))
            {
                throw new InvalidOperationException($"Batch response included duplicate message '{messageId}'.");
            }
        }

        foreach (ResoniteBatchOperations.PendingBatchOperation pendingOperation in pendingOperations)
        {
            if (!correlated.ContainsKey(pendingOperation.MessageId.Value))
            {
                throw new InvalidOperationException($"Batch response did not include message '{pendingOperation.MessageId.Value}'.");
            }
        }

        if (correlated.Count != pendingOperations.Count)
        {
            throw new InvalidOperationException(
                $"Batch response included {correlated.Count} message-correlated response(s) for {pendingOperations.Count} pending operation(s).");
        }

        return correlated;
    }

    private static Dictionary<string, Response> CorrelateOrderedResponses(
        IReadOnlyList<Response> responses,
        IReadOnlyList<ResoniteBatchOperations.PendingBatchOperation> pendingOperations)
    {
        if (responses.Count != pendingOperations.Count)
        {
            throw new InvalidOperationException(
                $"Batch response included {responses.Count} ordered response(s) for {pendingOperations.Count} pending operation(s).");
        }

        Dictionary<string, Response> correlated = new(StringComparer.Ordinal);
        for (int i = 0; i < responses.Count; i++)
        {
            ResoniteBatchOperations.PendingBatchOperation pendingOperation = pendingOperations[i];
            correlated[pendingOperation.MessageId.Value] = responses[i];
        }

        return correlated;
    }
}
