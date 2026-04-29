using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using PlateauResoniteLink.Targets.Resonite.Execution;
using PlateauResoniteLink.Transport.ResoniteLink;

using ResoniteLink;

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
    private readonly ConcurrentDictionary<string, byte> createdSlotIds = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<SharedSlotIndexKey, CreatedSlot> sharedSlotIndex = new();
    private readonly ConcurrentDictionary<string, Slot> observedSlotSnapshotsById = new(StringComparer.Ordinal);
    public SceneAnchor? SceneAnchor { get; private set; } = initialSceneAnchor;

    public void IndexSetupHierarchy(ResoniteSceneSetupState setupState)
    {
        if (setupState.DatasetRootSnapshot is not null)
        {
            observedSlotSnapshotsById.Clear();
            IndexObservedSlotSnapshot(setupState.DatasetRootSnapshot);
        }
        else
        {
            IndexCreatedSharedSlot(ResoniteSlotLocator.Root, setupState.DatasetRootSlot);
        }

        IndexCreatedSharedSlot(setupState.DatasetRootSlot.Locator, setupState.DatasetAssetsRootSlot);
    }

    public Task<ObjectSlotHierarchy> CreateObjectHierarchyTask(
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

    public ResoniteFloat3 ResolveMeshCodeRootPosition(string meshCode)
    {
        SceneAnchor? anchor = SceneAnchor;
        if (anchor is null)
        {
            return new ResoniteFloat3(0.0, 0.0, 0.0);
        }

        if (string.Equals(anchor.Value.MeshCode, meshCode, StringComparison.Ordinal))
        {
            return anchor.Value.Position;
        }

        return ResonitePlacementPolicy.Add(
            anchor.Value.Position,
            ResonitePlacementPolicy.ComputeMeshCodeOffset(anchor.Value.MeshCode, meshCode));
    }

    public Task<CreatedSlot> GetOrCreateSharedChildSlotAsync(
        IResoniteLinkClient client,
        ResoniteSlotLocator parent,
        string slotName,
        CancellationToken cancellationToken)
    {
        return GetOrCreateSharedChildSlotByIdAsync(client, parent, slotName, null, null, cancellationToken);
    }

    private async Task<ObjectSlotHierarchy> CreateObjectHierarchyWithLinkedCancellationAsync(
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

    private async Task<ObjectSlotHierarchy> CreateObjectSlotHierarchyAsync(
        IResoniteLinkClient client,
        ResoniteConstructionCityObject cityObject,
        CancellationToken cancellationToken)
    {
        string sourceFileRelativePath = ResonitePlacementPolicy.ResolveSourceFileRelativePath(cityObject);
        string sourceFileSlotName = ResonitePlacementPolicy.ResolveSourceFileSlotName(cityObject, sourceFileRelativePath, sourceFileSlotNamesByRelativePath);
        string rootMeshCode = ResonitePlacementPolicy.ResolveRequiredSourceFileRootMeshCode(sourceFileSlotName, cityObject.ActualMeshCode);
        CanonicalParentScope parentScope = await canonicalParentScopeCache.GetOrCreateAsync(
            new CanonicalParentSourceFile(sourceFileRelativePath, rootMeshCode, cityObject.LodLevel),
            ct => CreateCanonicalParentScopeAsync(client, sourceFileSlotName, rootMeshCode, cityObject.LodLevel, ct),
            cancellationToken);
        ResoniteFloat3 plannedRootPosition = ResolvePlannedRootPosition(rootMeshCode);
        return new ObjectSlotHierarchy(
            parentScope.AssetLodSlot,
            parentScope.LodSlot,
            cityObject.DisplayName,
            ResonitePlacementPolicy.ResolveCityObjectLocalPosition(
                requestLocalOrigin,
                rootMeshCode,
                TryGetObservedSlotPosition(parentScope.SourceFileSlot.Locator),
                cityObject.Transform.Position),
            cityObject.Transform.Rotation);
    }

    private async Task<CanonicalParentScope> CreateCanonicalParentScopeAsync(
        IResoniteLinkClient client,
        string sourceFileSlotName,
        string rootMeshCode,
        int? lodLevel,
        CancellationToken cancellationToken)
    {
        string lodSlotName = ResonitePlacementPolicy.FormatLodSlotName(lodLevel);
        ResoniteFloat3 rootPosition = ResolvePlannedRootPosition(rootMeshCode);
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
                createdSlotIds[createdSlot.Locator.Value] = 0;
                return IndexCreatedSharedSlot(parent, createdSlot, position);
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
            createdSlotIds[indexedSlot.Value.Locator.Value] = 0;
            return indexedSlot.Value;
        }

        CreatedSlot createdSlot = await createSlotAsync(client, parent, slotName, position, rotation, cancellationToken);
        createdSlotIds[createdSlot.Locator.Value] = 0;
        return IndexCreatedSharedSlot(parent, createdSlot, position);
    }

    private void IndexObservedSlotSnapshot(Slot slot)
    {
        if (string.IsNullOrWhiteSpace(slot.ID))
        {
            return;
        }

        observedSlotSnapshotsById[slot.ID] = slot;
        if (slot.Children is null || slot.Children.Count == 0)
        {
            return;
        }

        foreach (Slot child in slot.Children)
        {
            if (!string.IsNullOrWhiteSpace(child.ID) && !string.IsNullOrWhiteSpace(child.Name?.Value))
            {
                sharedSlotIndex[new SharedSlotIndexKey(slot.ID!, child.Name!.Value)] = new CreatedSlot(new ResoniteSlotLocator(child.ID!), child.Name.Value);
            }

            IndexObservedSlotSnapshot(child);
        }
    }

    private CreatedSlot? TryGetIndexedSharedChildSlot(ResoniteSlotLocator parent, string slotName)
    {
        return sharedSlotIndex.TryGetValue(new SharedSlotIndexKey(parent.Value, slotName), out CreatedSlot createdSlot)
            ? createdSlot
            : null;
    }

    private CreatedSlot IndexCreatedSharedSlot(ResoniteSlotLocator parent, CreatedSlot createdSlot, ResoniteFloat3? position = null)
    {
        sharedSlotIndex[new SharedSlotIndexKey(parent.Value, createdSlot.SlotName)] = createdSlot;
        observedSlotSnapshotsById[createdSlot.Locator.Value] = new Slot
        {
            ID = createdSlot.Locator.Value,
            Name = new Field_string { Value = createdSlot.SlotName },
            Parent = new Reference { TargetID = parent.Value },
            Position = position is null ? null : CreateFloat3(position),
        };
        return createdSlot;
    }

    private ResoniteFloat3? TryGetObservedSlotPosition(ResoniteSlotLocator slot)
    {
        if (!observedSlotSnapshotsById.TryGetValue(slot.Value, out Slot? observedSlot)
            || observedSlot.Position is not Field_float3 position)
        {
            return null;
        }

        return new ResoniteFloat3(position.Value.x, position.Value.y, position.Value.z);
    }

    private ResoniteFloat3 ResolvePlannedRootPosition(string rootMeshCode)
    {
        SceneAnchor? anchor = SceneAnchor;
        if (anchor is null)
        {
            return ResonitePlacementPolicy.ResolveMeshRootPosition(requestLocalOrigin, rootMeshCode);
        }

        if (TryResolveReferenceSourceFileRootPosition(anchor.Value, rootMeshCode, out ResoniteFloat3 referenceRootPosition))
        {
            return referenceRootPosition;
        }

        return ResolveMeshCodeRootPosition(rootMeshCode);
    }

    private bool TryResolveReferenceSourceFileRootPosition(
        SceneAnchor anchor,
        string rootMeshCode,
        out ResoniteFloat3 position)
    {
        position = new ResoniteFloat3(0.0, 0.0, 0.0);
        if (anchor.ReferenceSourceFileRoot is not ResoniteSlotLocator referenceSourceFileRoot
            || !observedSlotSnapshotsById.TryGetValue(referenceSourceFileRoot.Value, out Slot? referenceSlot)
            || !ResoniteSourceMeshCodeAnchor.TryGetConcreteMeshCode(referenceSlot.Name?.Value ?? string.Empty, out string referenceMeshCode))
        {
            return false;
        }

        ResoniteFloat3 referencePosition = GetSlotPositionOrDefault(referenceSlot);
        position = string.Equals(referenceMeshCode, rootMeshCode, StringComparison.Ordinal)
            ? referencePosition
            : ResonitePlacementPolicy.Add(
                referencePosition,
                ResonitePlacementPolicy.ComputeMeshCodeOffset(referenceMeshCode, rootMeshCode));
        return true;
    }

    private static Field_float3 CreateFloat3(ResoniteFloat3 value)
    {
        return new Field_float3
        {
            Value = new float3
            {
                x = (float)value.X,
                y = (float)value.Y,
                z = (float)value.Z,
            },
        };
    }

    private static ResoniteFloat3 GetSlotPositionOrDefault(Slot slot)
    {
        if (slot.Position is not Field_float3 position)
        {
            return new ResoniteFloat3(0.0, 0.0, 0.0);
        }

        return new ResoniteFloat3(position.Value.x, position.Value.y, position.Value.z);
    }

    internal sealed record ObjectSlotHierarchy(
        CreatedSlot AssetLodSlot,
        CreatedSlot LodSlot,
        string CityObjectSlotName,
        ResoniteFloat3 CityObjectLocalPosition,
        ResoniteFloatQ? CityObjectRotation);

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
