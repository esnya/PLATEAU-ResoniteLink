using ResoniteLink;

namespace Plateau.ResoniteLink.Cli;

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
        if (Root?.Children is null)
        {
            return null;
        }

        Slot[] matches = Root.Children
            .Where(child => string.Equals(child.Name?.Value, slotName, StringComparison.Ordinal))
            .ToArray();
        if (matches.Length == 0)
        {
            return null;
        }

        if (matches.Length > 1)
        {
            throw new InvalidOperationException(
                $"Parent slot '{parentId}' contains multiple child slots named '{slotName}'.");
        }

        return matches[0];
    }
}
