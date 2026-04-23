using System.Threading;
using System.Threading.Tasks;

namespace PlateauResoniteLink.Targets.Resonite.Execution;

internal interface IResoniteSceneSlotLocator
{
    Task<CreatedSlot?> TryGetDatasetRootAsync(
        IResoniteLinkClient client,
        string slotName,
        CancellationToken cancellationToken);
}

internal sealed class ResoniteSceneSlotLocator : IResoniteSceneSlotLocator
{
    private const string RootSlotId = "Root";

    public async Task<CreatedSlot?> TryGetDatasetRootAsync(
        IResoniteLinkClient client,
        string slotName,
        CancellationToken cancellationToken)
    {
        ResoniteSceneSlotSnapshot snapshot = await ResoniteSceneSlotSnapshot.CreateAsync(
            client,
            ResoniteSlotLocator.Root,
            1,
            cancellationToken);
        ResoniteSceneChildLookupResult lookup = snapshot.GetUniqueChildLookupResult(slotName, RootSlotId);
        return lookup.State == ResoniteSceneChildLookupState.FoundWithId
            ? new CreatedSlot(new ResoniteSlotLocator(lookup.Slot!.ID!), slotName)
            : null;
    }
}
