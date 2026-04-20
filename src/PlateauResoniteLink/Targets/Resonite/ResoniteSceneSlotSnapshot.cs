using ResoniteLink;

namespace PlateauResoniteLink.Targets.Resonite.Execution;

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

internal sealed class ResoniteSceneSlotSnapshot
{
    private readonly Dictionary<string, Slot[]> uniqueChildrenByName;

    public ResoniteSceneSlotSnapshot(Slot? root)
    {
        Root = root;
        uniqueChildrenByName = BuildUniqueChildrenIndex(root);
    }

    public Slot? Root { get; }

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

    public ResoniteSceneChildLookupResult GetUniqueChildLookupResult(string slotName, string parentId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slotName);

        if (!uniqueChildrenByName.TryGetValue(slotName, out Slot[]? matches))
        {
            return new ResoniteSceneChildLookupResult(ResoniteSceneChildLookupState.NotFound, null);
        }

        if (matches.Length == 0)
        {
            return new ResoniteSceneChildLookupResult(ResoniteSceneChildLookupState.NotFound, null);
        }

        Slot match = SelectPreferredExistingSlot(matches);
        return new ResoniteSceneChildLookupResult(
            string.IsNullOrWhiteSpace(match.ID)
                ? ResoniteSceneChildLookupState.FoundWithoutId
                : ResoniteSceneChildLookupState.FoundWithId,
            match);
    }

    public ResoniteSceneChildLookupResult GetUniqueDescendantLookupResult(
        string parentId,
        params string[] pathSegments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parentId);
        ArgumentNullException.ThrowIfNull(pathSegments);
        if (pathSegments.Length == 0)
        {
            throw new ArgumentException("At least one path segment is required.", nameof(pathSegments));
        }

        ResoniteSceneSlotSnapshot currentSnapshot = this;
        ResoniteSceneChildLookupResult currentLookup = default;
        for (int index = 0; index < pathSegments.Length; index++)
        {
            string segment = pathSegments[index];
            currentLookup = currentSnapshot.GetUniqueChildLookupResult(segment, parentId);
            if (currentLookup.State == ResoniteSceneChildLookupState.NotFound)
            {
                return currentLookup;
            }

            if (index < pathSegments.Length - 1)
            {
                currentSnapshot = new ResoniteSceneSlotSnapshot(currentLookup.Slot);
            }
        }

        return currentLookup;
    }

    private static Dictionary<string, Slot[]> BuildUniqueChildrenIndex(Slot? root)
    {
        if (root?.Children is null || root.Children.Count == 0)
        {
            return new Dictionary<string, Slot[]>(StringComparer.Ordinal);
        }

        Dictionary<string, Slot[]> childrenByName = new(StringComparer.Ordinal);
        foreach (IGrouping<string, Slot> childGroup in root.Children
                     .GroupBy(static child => child.Name?.Value ?? string.Empty, StringComparer.Ordinal))
        {
            childrenByName[childGroup.Key] = childGroup.ToArray();
        }

        return childrenByName;
    }

    private static Slot SelectPreferredExistingSlot(IReadOnlyList<Slot> matches)
    {
        return matches
            .OrderByDescending(static slot => slot.Components?.Count ?? 0)
            .ThenBy(static slot => slot.ID, StringComparer.Ordinal)
            .First();
    }
}
