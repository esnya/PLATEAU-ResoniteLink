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
            RootSlotId,
            1,
            cancellationToken);
        ResoniteSceneChildLookupResult lookup = snapshot.GetUniqueChildLookupResult(slotName, RootSlotId);
        return lookup.State == ResoniteSceneChildLookupState.FoundWithId
            ? new CreatedSlot(lookup.SlotId!, slotName)
            : null;
    }
}
