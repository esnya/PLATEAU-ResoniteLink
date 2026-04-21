using System;
using System.Threading;
using System.Threading.Tasks;

using PlateauResoniteLink.Domain.Importing;
using PlateauResoniteLink.Transport.ResoniteLink;

using ResoniteLink;

namespace PlateauResoniteLink.Targets.Resonite.Execution;

internal interface IResoniteSlotCreator
{
    Task<CreatedSlot> CreateAsync(
        IResoniteLinkClient client,
        string parentId,
        string slotName,
        ResoniteFloat3? position,
        ResoniteFloatQ? rotation,
        CancellationToken cancellationToken);
}

internal sealed class ResoniteSlotCreator : IResoniteSlotCreator
{
    public async Task<CreatedSlot> CreateAsync(
        IResoniteLinkClient client,
        string parentId,
        string slotName,
        ResoniteFloat3? position,
        ResoniteFloatQ? rotation,
        CancellationToken cancellationToken)
    {
        ResoniteBatchOperations.PendingBatchSlot pendingSlot = new(
            LocalId: $"single_slot_{Guid.NewGuid():N}",
            MessageId: $"single_slot_message_{Guid.NewGuid():N}",
            SlotName: slotName);
        BatchResponse response = await client.RunDataModelOperationBatchAsync(
            [ResoniteBatchOperations.CreateAddSlotOperation(parentId, slotName, position, rotation, requestedSlotId: pendingSlot.LocalId, messageId: pendingSlot.MessageId)],
            cancellationToken);
        return CanonicalBatchEntityMap.Create(response).ResolveSlot(pendingSlot);
    }
}
