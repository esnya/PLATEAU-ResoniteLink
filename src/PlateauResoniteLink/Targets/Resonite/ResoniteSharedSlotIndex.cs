using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using PlateauResoniteLink.Targets.Resonite.Execution;
using PlateauResoniteLink.Transport.ResoniteLink;

namespace PlateauResoniteLink.Targets.Resonite;

internal sealed class ResoniteSharedSlotIndex(
    CreatedSlot datasetRootSlot,
    CreatedSlot datasetAssetsRootSlot,
    ResoniteLocalOrigin requestLocalOrigin,
    IReadOnlyDictionary<string, string> sourceFileSlotNamesByRelativePath,
    SceneAnchor? initialSceneAnchor,
    Func<IResoniteLinkClient, ResoniteSlotLocator, string, ResoniteFloat3?, ResoniteFloatQ?, CancellationToken, Task<CreatedSlot>> createSlotAsync)
{
    private readonly AsyncCompletedResultCache<SharedSlotIndexKey, CreatedSlot> sharedSlotCache = new();
    private readonly AsyncCompletedResultCache<SharedSlotIndexKey, CreatedSlot> runScopedSourceFileRootCache = new();
    private readonly AsyncCompletedResultCache<CanonicalParentSourceFile, CanonicalParentScope> canonicalParentScopeCache = new();
    private readonly ResoniteSlotSnapshotIndex slotSnapshotIndex = new(datasetRootSlot);
    public SceneAnchor? SceneAnchor { get; private set; } = initialSceneAnchor;

    public void IndexSetupHierarchy(ResoniteSceneSetupState setupState)
    {
        slotSnapshotIndex.IndexSetupHierarchy(setupState);
    }

    public Task<ResoniteObjectSlotHierarchy> CreateObjectHierarchyTask(
        IResoniteLinkClient client,
        ResoniteConstructionCityObject cityObject,
        CancellationToken processingCancellationToken,
        CancellationToken cancellationToken)
    {
        return CreateObjectHierarchyWithLinkedCancellationAsync(
            client,
            cityObject,
            processingCancellationToken,
            cancellationToken);
    }

    public Task<CreatedSlot> GetOrCreateSharedChildSlotAsync(
        IResoniteLinkClient client,
        ResoniteSlotLocator parent,
        string slotName,
        CancellationToken cancellationToken)
    {
        return GetOrCreateSharedChildSlotByIdAsync(client, parent, slotName, null, null, cancellationToken);
    }

    private async Task<ResoniteObjectSlotHierarchy> CreateObjectHierarchyWithLinkedCancellationAsync(
        IResoniteLinkClient client,
        ResoniteConstructionCityObject cityObject,
        CancellationToken processingCancellationToken,
        CancellationToken cancellationToken)
    {
        using CancellationTokenSource linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            processingCancellationToken);
        return await CreateObjectSlotHierarchyAsync(client, cityObject, linkedCancellation.Token);
    }

    private async Task<ResoniteObjectSlotHierarchy> CreateObjectSlotHierarchyAsync(
        IResoniteLinkClient client,
        ResoniteConstructionCityObject cityObject,
        CancellationToken cancellationToken)
    {
        string sourceFileRelativePath = ResonitePlacementPolicy.ResolveSourceFileRelativePath(cityObject);
        string sourceFileSlotName = ResonitePlacementPolicy.ResolveSourceFileSlotName(cityObject, sourceFileRelativePath, sourceFileSlotNamesByRelativePath);
        string rootMeshCode = ResonitePlacementPolicy.ResolveRequiredSourceFileRootMeshCode(sourceFileSlotName, cityObject.ActualMeshCode);
        SourceRootPlacement sourceRootPlacement = ResolveSourceRootPlacement(sourceFileSlotName, rootMeshCode);
        CanonicalParentScope parentScope = await canonicalParentScopeCache.GetOrCreateAsync(
            new CanonicalParentSourceFile(sourceFileRelativePath, rootMeshCode, cityObject.LodLevel),
            ct => CreateCanonicalParentScopeAsync(client, sourceFileSlotName, rootMeshCode, cityObject.LodLevel, sourceRootPlacement.RootPosition, ct),
            cancellationToken);
        return new ResoniteObjectSlotHierarchy(
            parentScope.AssetLodSlot,
            parentScope.LodSlot,
            cityObject.DisplayName,
            ResonitePlacementPolicy.ResolveCityObjectLocalPosition(
                requestLocalOrigin,
                rootMeshCode,
                sourceRootPlacement.LocalPositionReferenceRoot,
                cityObject.Transform.Position),
            cityObject.Transform.Rotation);
    }

    private async Task<CanonicalParentScope> CreateCanonicalParentScopeAsync(
        IResoniteLinkClient client,
        string sourceFileSlotName,
        string rootMeshCode,
        int? lodLevel,
        ResoniteFloat3 rootPosition,
        CancellationToken cancellationToken)
    {
        string lodSlotName = ResonitePlacementPolicy.FormatLodSlotName(lodLevel);
        CreatedSlot sourceFileSlot = await GetOrCreateRunScopedSourceFileRootAsync(
            client,
            datasetRootSlot.Locator,
            sourceFileSlotName,
            rootPosition,
            cancellationToken);
        CreatedSlot assetSourceFileSlot = await GetOrCreateRunScopedSourceFileRootAsync(
            client,
            datasetAssetsRootSlot.Locator,
            sourceFileSlotName,
            null,
            cancellationToken);
        CreatedSlot lodSlot = await GetOrCreateSharedChildSlotByIdAsync(
            client,
            sourceFileSlot.Locator,
            lodSlotName,
            null,
            null,
            cancellationToken);
        CreatedSlot assetLodSlot = await GetOrCreateSharedChildSlotByIdAsync(
            client,
            assetSourceFileSlot.Locator,
            lodSlotName,
            null,
            null,
            cancellationToken);

        if (SceneAnchor is { } anchor
            && (string.Equals(anchor.MeshCode, rootMeshCode, StringComparison.Ordinal)
                || anchor.ReferenceSourceFileRoot is null))
        {
            SceneAnchor = anchor with
            {
                LocationSlot = sourceFileSlot.Locator,
                MeshCode = rootMeshCode,
                Position = rootPosition,
                ReferenceSourceFileRoot = sourceFileSlot.Locator,
            };
        }

        return new CanonicalParentScope(
            sourceFileSlot,
            assetSourceFileSlot,
            lodSlot,
            assetLodSlot);
    }

    private async Task<CreatedSlot> GetOrCreateSharedChildSlotByIdAsync(
        IResoniteLinkClient client,
        ResoniteSlotLocator parent,
        string slotName,
        ResoniteFloat3? position,
        ResoniteFloatQ? rotation,
        CancellationToken cancellationToken)
    {
        return await sharedSlotCache.GetOrCreateAsync(
            new SharedSlotIndexKey(parent.Value, slotName),
            ct => GetOrCreateSharedChildSlotCoreAsync(client, parent, slotName, position, rotation, ct),
            cancellationToken);
    }

    private async Task<CreatedSlot> GetOrCreateRunScopedSourceFileRootAsync(
        IResoniteLinkClient client,
        ResoniteSlotLocator parent,
        string slotName,
        ResoniteFloat3? position,
        CancellationToken cancellationToken)
    {
        return await runScopedSourceFileRootCache.GetOrCreateAsync(
            new SharedSlotIndexKey(parent.Value, slotName),
            async ct =>
            {
                CreatedSlot createdSlot = await createSlotAsync(client, parent, slotName, position, null, ct);
                slotSnapshotIndex.MarkCreated(createdSlot);
                return slotSnapshotIndex.IndexCreatedSharedSlot(parent, createdSlot, position);
            },
            cancellationToken);
    }

    private async Task<CreatedSlot> GetOrCreateSharedChildSlotCoreAsync(
        IResoniteLinkClient client,
        ResoniteSlotLocator parent,
        string slotName,
        ResoniteFloat3? position,
        ResoniteFloatQ? rotation,
        CancellationToken cancellationToken)
    {
        CreatedSlot? indexedSlot = TryGetIndexedSharedChildSlot(parent, slotName);
        if (indexedSlot is not null)
        {
            slotSnapshotIndex.MarkCreated(indexedSlot.Value);
            return indexedSlot.Value;
        }

        CreatedSlot createdSlot = await createSlotAsync(client, parent, slotName, position, rotation, cancellationToken);
        slotSnapshotIndex.MarkCreated(createdSlot);
        return slotSnapshotIndex.IndexCreatedSharedSlot(parent, createdSlot, position);
    }

    private CreatedSlot? TryGetIndexedSharedChildSlot(ResoniteSlotLocator parent, string slotName)
    {
        return slotSnapshotIndex.TryGetSharedChildSlot(parent, slotName);
    }

    private SourceRootPlacement ResolveSourceRootPlacement(
        string sourceFileSlotName,
        string rootMeshCode)
    {
        return SourceRootPlacementResolver.Resolve(
            sourceFileSlotName,
            rootMeshCode,
            requestLocalOrigin,
            slotSnapshotIndex.GetObservedDatasetSourceRoots());
    }

    private sealed record CanonicalParentScope(
        CreatedSlot SourceFileSlot,
        CreatedSlot AssetSourceFileSlot,
        CreatedSlot LodSlot,
        CreatedSlot AssetLodSlot);

    private readonly record struct CanonicalParentSourceFile(
        string SourceFileRelativePath,
        string RootMeshCode,
        int? LodLevel);

    private readonly record struct SharedSlotIndexKey(
        string ParentSlotId,
        string SlotName);
}
