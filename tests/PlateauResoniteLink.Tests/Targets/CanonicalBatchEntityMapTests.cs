using System;

using PlateauResoniteLink.Targets.Resonite;
using PlateauResoniteLink.Targets.Resonite.Execution;

using ResoniteLink;

namespace PlateauResoniteLink.Tests.Targets;

public sealed class CanonicalBatchEntityMapTests
{
    [Fact]
    public void CreateRejectsMixedMessageIdAndOrderedOnlyResponses()
    {
        ResoniteBatchOperations.BatchActionBuilder builder = new();
        ResoniteBatchOperations.PendingBatchSlot first = builder.AddSlot("parent", "First", null, null);
        _ = builder.AddSlot("parent", "Second", null, null);
        BatchResponse response = new()
        {
            Responses =
            [
                NewEntity("slot-1", first.MessageId.Value),
                NewEntity("slot-2", sourceMessageId: null),
            ],
        };

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => CanonicalBatchEntityMap.Create(response, builder.PendingActions));

        Assert.Contains("mixed message-correlated and ordered-only responses", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateRejectsDuplicateMessageIds()
    {
        ResoniteBatchOperations.BatchActionBuilder builder = new();
        ResoniteBatchOperations.PendingBatchSlot pending = builder.AddSlot("parent", "First", null, null);
        BatchResponse response = new()
        {
            Responses =
            [
                NewEntity("slot-1", pending.MessageId.Value),
                NewEntity("slot-2", pending.MessageId.Value),
            ],
        };

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => CanonicalBatchEntityMap.Create(response, builder.PendingActions));

        Assert.Contains($"duplicate message '{pending.MessageId.Value}'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateRejectsMissingMessageIdResponse()
    {
        ResoniteBatchOperations.BatchActionBuilder builder = new();
        ResoniteBatchOperations.PendingBatchSlot pending = builder.AddSlot("parent", "First", null, null);
        BatchResponse response = new()
        {
            Responses = [],
        };

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => CanonicalBatchEntityMap.Create(response, builder.PendingActions));

        Assert.Contains($"0 ordered response(s) for {builder.PendingActions.Count} pending operation(s)", exception.Message, StringComparison.Ordinal);
        Assert.Equal("First", pending.SlotName);
    }

    [Fact]
    public void CreateRejectsExtraMessageIdResponse()
    {
        ResoniteBatchOperations.BatchActionBuilder builder = new();
        ResoniteBatchOperations.PendingBatchSlot pending = builder.AddSlot("parent", "First", null, null);
        BatchResponse response = new()
        {
            Responses =
            [
                NewEntity("slot-1", pending.MessageId.Value),
                NewEntity("slot-extra", "unexpected-message"),
            ],
        };

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => CanonicalBatchEntityMap.Create(response, builder.PendingActions));

        Assert.Contains("2 message-correlated response(s) for 1 pending operation(s)", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateCorrelatesOrderedOnlyResponsesWhenCountsMatch()
    {
        ResoniteBatchOperations.BatchActionBuilder builder = new();
        ResoniteBatchOperations.PendingBatchSlot first = builder.AddSlot("parent", "First", null, null);
        ResoniteBatchOperations.PendingBatchSlot second = builder.AddSlot("parent", "Second", null, null);
        BatchResponse response = new()
        {
            Responses =
            [
                NewEntity("slot-1", sourceMessageId: null),
                NewEntity("slot-2", sourceMessageId: null),
            ],
        };

        CanonicalBatchEntityMap entityMap = CanonicalBatchEntityMap.Create(response, builder.PendingActions);

        Assert.Equal(new ResoniteSlotLocator("slot-1"), entityMap.ResolveSlot(first).Locator);
        Assert.Equal(new ResoniteSlotLocator("slot-2"), entityMap.ResolveSlot(second).Locator);
    }

    private static NewEntityId NewEntity(string entityId, string? sourceMessageId)
    {
        return new NewEntityId
        {
            EntityId = entityId,
            Success = true,
            SourceMessageID = sourceMessageId,
        };
    }
}
