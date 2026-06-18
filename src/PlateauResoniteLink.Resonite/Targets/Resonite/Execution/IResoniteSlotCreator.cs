using System.Threading;
using System.Threading.Tasks;

using PlateauResoniteLink.Resonite.Transport.ResoniteLink;

using ResoniteLink;

namespace PlateauResoniteLink.Resonite.Targets.Resonite.Execution;

internal interface IResoniteSlotCreator
{
    Task<CreatedSlot> CreateAsync(
        IResoniteLinkClient client,
        ResoniteSlotLocator parent,
        string slotName,
        ResoniteFloat3? position,
        ResoniteFloatQ? rotation,
        long? orderOffset,
        CancellationToken cancellationToken);
}

internal sealed class ResoniteSlotCreator : IResoniteSlotCreator
{
    public async Task<CreatedSlot> CreateAsync(
        IResoniteLinkClient client,
        ResoniteSlotLocator parent,
        string slotName,
        ResoniteFloat3? position,
        ResoniteFloatQ? rotation,
        long? orderOffset,
        CancellationToken cancellationToken)
    {
        ResoniteBatchOperations.BatchActionBuilder batchBuilder = new();
        ResoniteBatchOperations.PendingBatchSlot pendingSlot = batchBuilder.AddSlot(
            parent.Value,
            slotName,
            position,
            rotation,
            orderOffset);
        BatchResponse response = await client.RunDataModelOperationBatchAsync(batchBuilder.Actions, cancellationToken);
        return CanonicalBatchEntityMap.Create(response).ResolveSlot(pendingSlot);
    }
}
