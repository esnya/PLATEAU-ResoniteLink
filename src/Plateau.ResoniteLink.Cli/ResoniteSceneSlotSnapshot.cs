using ResoniteLink;

namespace Plateau.ResoniteLink.Cli;

internal enum ResoniteSceneChildLookupState
{
    NotFound,
    FoundWithoutId,
    FoundWithId,
}

internal readonly record struct ResoniteSceneChildLookupResult(
    ResoniteSceneChildLookupState State,
    Slot? Slot)
{
    public string? SlotId => Slot?.ID;
}

internal readonly record struct ResoniteSceneSlotSnapshot(Slot? Root)
{
    public static async Task<ResoniteSceneSlotSnapshot> CreateAsync(
        IResoniteLinkClient client,
        string slotId,
        int depth,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentException.ThrowIfNullOrWhiteSpace(slotId);

        return new ResoniteSceneSlotSnapshot(await client.GetSlotAsync(slotId, depth, cancellationToken));
    }

    public Slot? TryGetUniqueChildByName(string slotName, string parentId)
    {
        return GetUniqueChildLookupResult(slotName, parentId).Slot;
    }

    public ResoniteSceneChildLookupResult GetUniqueChildLookupResult(string slotName, string parentId)
    {
        if (Root?.Children is null)
        {
            return new ResoniteSceneChildLookupResult(ResoniteSceneChildLookupState.NotFound, null);
        }

        Slot[] matches = Root.Children
            .Where(child => string.Equals(child.Name?.Value, slotName, StringComparison.Ordinal))
            .ToArray();
        if (matches.Length == 0)
        {
            return new ResoniteSceneChildLookupResult(ResoniteSceneChildLookupState.NotFound, null);
        }

        if (matches.Length > 1)
        {
            throw new InvalidOperationException(
                $"Parent slot '{parentId}' contains multiple child slots named '{slotName}'.");
        }

        Slot match = matches[0];
        return new ResoniteSceneChildLookupResult(
            string.IsNullOrWhiteSpace(match.ID)
                ? ResoniteSceneChildLookupState.FoundWithoutId
                : ResoniteSceneChildLookupState.FoundWithId,
            match);
    }
}
