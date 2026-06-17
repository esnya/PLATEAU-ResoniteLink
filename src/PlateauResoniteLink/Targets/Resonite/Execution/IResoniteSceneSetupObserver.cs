using System;
using System.Threading;
using System.Threading.Tasks;

using PlateauResoniteLink.Transport.ResoniteLink;

using ResoniteLink;

namespace PlateauResoniteLink.Targets.Resonite.Execution;

internal static class ResoniteSceneSetupConventions
{
    public const string AssetsRootName = "Assets";
    public const string SharedAssetsRootName = "PLATEAU Shared Assets";
    public const string SharedCommonMaterialsRootName = "Common Materials";
}

internal sealed record ObservedResoniteSceneSetup(
    Slot RootSnapshot,
    Slot? SharedAssetsSlot,
    Slot? SharedCommonMaterialsSlot,
    CreatedSlot? ExistingDatasetRoot,
    Slot? DatasetRootSnapshot,
    Slot? DatasetAssetsSlot,
    bool DatasetRootExisted);

internal interface IResoniteSceneSetupObserver
{
    Task<ObservedResoniteSceneSetup> ObserveAsync(
        IResoniteLinkClient client,
        string datasetRootName,
        CancellationToken cancellationToken);
}

internal sealed class ResoniteSceneSetupObserver : IResoniteSceneSetupObserver
{
    private readonly IResoniteSceneSlotLocator sceneSlotLocator;

    public ResoniteSceneSetupObserver(IResoniteSceneSlotLocator sceneSlotLocator)
    {
        this.sceneSlotLocator = sceneSlotLocator ?? throw new ArgumentNullException(nameof(sceneSlotLocator));
    }

    public async Task<ObservedResoniteSceneSetup> ObserveAsync(
        IResoniteLinkClient client,
        string datasetRootName,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentException.ThrowIfNullOrWhiteSpace(datasetRootName);

        Slot rootSnapshot = await client.GetSlotAsync(
                new ResoniteTransportSlotLocator(ResoniteSlotLocator.Root.Value),
                1,
                cancellationToken)
            ?? throw new InvalidOperationException("ResoniteLink did not surface the Root slot during setup.");
        ResoniteSceneSlotSnapshot rootSlotSnapshot = new(rootSnapshot);
        Slot? sharedAssetsSlot = GetReusableChildSlot(
            rootSlotSnapshot,
            ResoniteSceneSetupConventions.SharedAssetsRootName,
            ResoniteSlotLocator.Root.Value);
        Slot? sharedCommonMaterialsSlot = await TryGetExistingCommonMaterialsSlotAsync(
            client,
            sharedAssetsSlot,
            cancellationToken);
        CreatedSlot? existingDatasetRoot = await sceneSlotLocator.TryGetDatasetRootAsync(
            client,
            datasetRootName,
            cancellationToken);
        if (existingDatasetRoot is null)
        {
            return new ObservedResoniteSceneSetup(
                rootSnapshot,
                sharedAssetsSlot,
                sharedCommonMaterialsSlot,
                ExistingDatasetRoot: null,
                DatasetRootSnapshot: null,
                DatasetAssetsSlot: null,
                DatasetRootExisted: false);
        }

        Slot datasetRootSnapshot = await client.GetSlotAsync(
                new ResoniteTransportSlotLocator(existingDatasetRoot.Value.Locator.Value),
                3,
                cancellationToken)
            ?? throw new InvalidOperationException(
                $"ResoniteLink did not surface dataset root '{existingDatasetRoot.Value.Locator.Value}' after it was discovered.");
        ResoniteSceneChildLookupResult assetsLookup = new ResoniteSceneSlotSnapshot(datasetRootSnapshot)
            .GetUniqueChildLookupResult(
                ResoniteSceneSetupConventions.AssetsRootName,
                existingDatasetRoot.Value.Locator.Value);
        Slot? assetsSlot = assetsLookup.State == ResoniteSceneChildLookupState.FoundWithId
            ? assetsLookup.Slot
            : null;

        return new ObservedResoniteSceneSetup(
            rootSnapshot,
            sharedAssetsSlot,
            sharedCommonMaterialsSlot,
            existingDatasetRoot,
            datasetRootSnapshot,
            assetsSlot,
            DatasetRootExisted: true);
    }

    private static Slot? GetReusableChildSlot(
        ResoniteSceneSlotSnapshot snapshot,
        string slotName,
        string parentId)
    {
        ResoniteSceneChildLookupResult lookup = snapshot.GetUniqueChildLookupResult(slotName, parentId);
        return lookup.State == ResoniteSceneChildLookupState.FoundWithId
            ? lookup.Slot
            : null;
    }

    private static async Task<Slot?> TryGetExistingCommonMaterialsSlotAsync(
        IResoniteLinkClient client,
        Slot? sharedAssetsSlot,
        CancellationToken cancellationToken)
    {
        if (sharedAssetsSlot?.ID is null)
        {
            return null;
        }

        Slot? sharedAssetsSnapshot = await client.GetSlotAsync(
            new ResoniteTransportSlotLocator(sharedAssetsSlot.ID),
            1,
            cancellationToken);
        Slot? commonMaterialsSlot = sharedAssetsSnapshot is null
            ? null
            : GetReusableChildSlot(
                new ResoniteSceneSlotSnapshot(sharedAssetsSnapshot),
                ResoniteSceneSetupConventions.SharedCommonMaterialsRootName,
                sharedAssetsSlot.ID);
        if (commonMaterialsSlot?.ID is null)
        {
            return commonMaterialsSlot;
        }

        return await client.GetSlotAsync(
            new ResoniteTransportSlotLocator(commonMaterialsSlot.ID),
            2,
            cancellationToken);
    }
}
