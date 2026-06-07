using System.Threading;
using System.Threading.Tasks;

using PlateauResoniteLink.Transport.ResoniteLink;

namespace PlateauResoniteLink.Targets.Resonite.Execution;

internal static class ResoniteSceneSlotLocator
{
    private const string RootSlotId = "Root";

    public static async Task<CreatedSlot?> TryGetDatasetRootAsync(
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
