using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using PlateauResoniteLink.Resonite.Transport.ResoniteLink;

using ResoniteLink;

namespace PlateauResoniteLink.Resonite.Targets.Resonite.Execution;

internal interface IResoniteSceneAnchorResolver
{
    Task<SceneAnchor> ResolveAsync(
        IResoniteLinkClient client,
        ResoniteSlotLocator datasetRootSlot,
        string completionMeshCode,
        CancellationToken cancellationToken);
}

internal readonly record struct SceneAnchor(
    ResoniteSlotLocator LocationSlot,
    string MeshCode,
    ResoniteFloat3 Position,
    ResoniteSlotLocator? ReferenceSourceFileRoot);

internal sealed class ResoniteSceneAnchorResolver : IResoniteSceneAnchorResolver
{
    private const string RootSlotId = "Root";

    public async Task<SceneAnchor> ResolveAsync(
        IResoniteLinkClient client,
        ResoniteSlotLocator datasetRootSlot,
        string completionMeshCode,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentException.ThrowIfNullOrWhiteSpace(datasetRootSlot.Value);
        ArgumentException.ThrowIfNullOrWhiteSpace(completionMeshCode);

        await WaitForSlotAvailableAsync(client, datasetRootSlot, cancellationToken);
        ResoniteSceneSlotSnapshot datasetRootSnapshot = await ResoniteSceneSlotSnapshot.CreateAsync(
            client,
            datasetRootSlot,
            1,
            cancellationToken);
        ObservedDatasetSourceRoot[] sourceFileRoots = datasetRootSnapshot.Root?.Children is null
            ? []
            : ObservedDatasetSourceRootSelector.SelectDirectChildren(
                datasetRootSnapshot.Root.Children,
                []);
        ObservedDatasetSourceRoot? completionSourceFileRoot = sourceFileRoots.FirstOrDefault(
            root => string.Equals(root.ConcreteMeshCode, completionMeshCode, StringComparison.Ordinal));
        if (completionSourceFileRoot is not null)
        {
            ResoniteSlotLocator sourceRootLocator = CreateLocatorOrFallback(completionSourceFileRoot, datasetRootSlot);
            return new SceneAnchor(
                sourceRootLocator,
                completionMeshCode,
                completionSourceFileRoot.Position,
                sourceRootLocator);
        }

        (ObservedDatasetSourceRoot Root, string MeshCode) referenceSourceFileRoot = sourceFileRoots
            .Select(static root => root.ConcreteMeshCode is { } meshCode
                ? (Root: root, MeshCode: meshCode)
                : default)
            .Where(static candidate => candidate.Root is not null)
            .OrderBy(static candidate => candidate.MeshCode, StringComparer.Ordinal)
            .ThenBy(static candidate => candidate.Root.SlotId, StringComparer.Ordinal)
            .FirstOrDefault();
        if (referenceSourceFileRoot.Root is not null)
        {
            ResoniteSlotLocator referenceRootLocator = CreateLocatorOrFallback(referenceSourceFileRoot.Root, datasetRootSlot);
            return new SceneAnchor(
                referenceRootLocator,
                completionMeshCode,
                ResonitePlacementPolicy.Add(
                    referenceSourceFileRoot.Root.Position,
                    ResonitePlacementPolicy.ComputeMeshCodeOffset(referenceSourceFileRoot.MeshCode, completionMeshCode)),
                referenceRootLocator);
        }

        return new SceneAnchor(
            datasetRootSlot,
            completionMeshCode,
            GetSlotPositionOrDefault(datasetRootSnapshot.Root ?? throw new InvalidOperationException("Dataset root snapshot did not include a root slot.")),
            ReferenceSourceFileRoot: null);
    }

    private static async Task WaitForSlotAvailableAsync(
        IResoniteLinkClient client,
        ResoniteSlotLocator slot,
        CancellationToken cancellationToken)
    {
        if (string.Equals(slot.Value, RootSlotId, StringComparison.Ordinal))
        {
            return;
        }

        Slot? existingSlot = await client.GetSlotAsync(new ResoniteTransportSlotLocator(slot.Value), 0, cancellationToken);
        if (existingSlot is not null)
        {
            return;
        }

        throw new InvalidOperationException($"ResoniteLink did not surface slot '{slot.Value}' on the initial probe.");
    }

    private static ResoniteSlotLocator CreateLocatorOrFallback(
        ObservedDatasetSourceRoot sourceRoot,
        ResoniteSlotLocator fallback)
    {
        return string.IsNullOrWhiteSpace(sourceRoot.SlotId)
            ? fallback
            : new ResoniteSlotLocator(sourceRoot.SlotId);
    }

    private static ResoniteFloat3 GetSlotPositionOrDefault(Slot slot)
    {
        if (slot.Position is Field_float3 position)
        {
            return new ResoniteFloat3(position.Value.x, position.Value.y, position.Value.z);
        }

        return new ResoniteFloat3(0.0, 0.0, 0.0);
    }

}
