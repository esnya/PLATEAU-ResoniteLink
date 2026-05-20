using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
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
    private const double StrictPlacementEquivalenceTolerance = 0.001;
    private const double SiblingProjectionDriftTolerance = 0.25;

    private readonly AsyncCompletedResultCache<SharedSlotIndexKey, CreatedSlot> sharedSlotCache = new();
    private readonly AsyncCompletedResultCache<SharedSlotIndexKey, CreatedSlot> runScopedSourceFileRootCache = new();
    private readonly AsyncCompletedResultCache<CanonicalParentSourceFile, CanonicalParentScope> canonicalParentScopeCache = new();
    private readonly ConcurrentDictionary<string, byte> createdSlotIds = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<SharedSlotIndexKey, CreatedSlot> sharedSlotIndex = new();
    private readonly ConcurrentDictionary<string, Slot> observedSlotSnapshotsById = new(StringComparer.Ordinal);
    private Slot[]? observedDatasetSourceRoots;
    public SceneAnchor? SceneAnchor { get; private set; } = initialSceneAnchor;

    public void IndexSetupHierarchy(ResoniteSceneSetupState setupState)
    {
        observedDatasetSourceRoots = null;
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
        SourceRootPlacement sourceRootPlacement = ResolveSourceRootPlacement(sourceFileSlotName, rootMeshCode);
        CanonicalParentScope parentScope = await canonicalParentScopeCache.GetOrCreateAsync(
            new CanonicalParentSourceFile(sourceFileRelativePath, rootMeshCode, cityObject.LodLevel),
            ct => CreateCanonicalParentScopeAsync(client, sourceFileSlotName, rootMeshCode, cityObject.LodLevel, sourceRootPlacement.RootPosition, ct),
            cancellationToken);
        return new ObjectSlotHierarchy(
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

    private SourceRootPlacement ResolveSourceRootPlacement(
        string sourceFileSlotName,
        string rootMeshCode)
    {
        ObservedSourceRootPlacement? observedRootPlacement = TryResolveObservedSourceRootPosition(sourceFileSlotName, rootMeshCode);
        ResoniteFloat3 rootPosition = observedRootPlacement?.Position
            ?? ResolveDatasetRootAnchoredSourceRootPosition(rootMeshCode)
            ?? ResonitePlacementPolicy.ResolveMeshRootPosition(requestLocalOrigin, rootMeshCode);

        return new SourceRootPlacement(rootPosition, rootPosition);
    }

    private ResoniteFloat3? ResolveDatasetRootAnchoredSourceRootPosition(string rootMeshCode)
    {
        if (SceneAnchor is not { ReferenceSourceFileRoot: null } anchor)
        {
            return null;
        }

        return ResonitePlacementPolicy.ComputeMeshCodeOffset(anchor.MeshCode, rootMeshCode);
    }

    private ObservedSourceRootPlacement? TryResolveObservedSourceRootPosition(
        string sourceFileSlotName,
        string rootMeshCode)
    {
        Slot[] directSourceRoots = GetObservedDatasetSourceRoots();
        ObservedSourceRootPlacement[] exactSourceRoots = directSourceRoots
            .Where(slot => string.Equals(slot.Name?.Value, sourceFileSlotName, StringComparison.Ordinal))
            .Select(slot => new ObservedSourceRootPlacement(
                GetSlotPositionOrDefault(slot),
                ReferenceMeshCode: rootMeshCode,
                SlotId: slot.ID ?? string.Empty))
            .ToArray();
        if (exactSourceRoots.Length > 0)
        {
            return SelectDeterministicObservedPlacement(
                exactSourceRoots,
                $"Dataset root contains multiple observed source roots named '{sourceFileSlotName}' with different placements. Append placement is ambiguous.",
                StrictPlacementEquivalenceTolerance);
        }

        ObservedSourceRootPlacement[] ancestorCandidates = directSourceRoots
            .Select(static slot => ResoniteSourceMeshCodeAnchor.TryGetConcreteMeshCode(slot.Name?.Value ?? string.Empty, out string meshCode)
                ? (Slot: slot, MeshCode: meshCode)
                : default)
            .Where(candidate => candidate.Slot is not null
                && rootMeshCode.StartsWith(candidate.MeshCode, StringComparison.Ordinal))
            .Select(candidate => new ObservedSourceRootPlacement(
                ResonitePlacementPolicy.Add(
                    GetSlotPositionOrDefault(candidate.Slot),
                    ResonitePlacementPolicy.ComputeMeshCodeOffset(candidate.MeshCode, rootMeshCode)),
                ReferenceMeshCode: candidate.MeshCode,
                SlotId: candidate.Slot.ID ?? string.Empty))
            .OrderByDescending(static candidate => candidate.ReferenceMeshCode.Length)
            .ToArray();
        if (ancestorCandidates.Length == 0)
        {
            ObservedSourceRootPlacement[] observedMeshCodeRoots = directSourceRoots
                .Select(static slot => ResoniteSourceMeshCodeAnchor.TryGetConcreteMeshCode(slot.Name?.Value ?? string.Empty, out string meshCode)
                    ? (Slot: slot, MeshCode: meshCode)
                    : default)
                .Where(static candidate => candidate.Slot is not null)
                .Select(candidate => new ObservedSourceRootPlacement(
                    ResonitePlacementPolicy.Add(
                        GetSlotPositionOrDefault(candidate.Slot),
                        ResonitePlacementPolicy.ComputeMeshCodeOffset(candidate.MeshCode, rootMeshCode)),
                    ReferenceMeshCode: candidate.MeshCode,
                    SlotId: candidate.Slot.ID ?? string.Empty))
                .ToArray();
            return observedMeshCodeRoots.Length == 0
                ? null
                : SelectDeterministicObservedPlacement(
                    observedMeshCodeRoots,
                    $"Dataset root contains multiple observed source roots that resolve different placements for mesh-code '{rootMeshCode}'. Append placement is ambiguous.",
                    SiblingProjectionDriftTolerance);
        }

        int bestLength = ancestorCandidates[0].ReferenceMeshCode.Length;
        ObservedSourceRootPlacement[] bestCandidates = ancestorCandidates
            .Where(candidate => candidate.ReferenceMeshCode.Length == bestLength)
            .ToArray();
        return SelectDeterministicObservedPlacement(
            bestCandidates,
            $"Dataset root contains multiple observed source roots for mesh-code '{bestCandidates[0].ReferenceMeshCode}' with different placements. Append placement is ambiguous.",
            StrictPlacementEquivalenceTolerance);
    }

    private IEnumerable<Slot> EnumerateObservedDatasetSourceRoots()
    {
        return observedSlotSnapshotsById.Values
            .Where(slot => string.Equals(slot.Parent?.TargetID, datasetRootSlot.Locator.Value, StringComparison.Ordinal))
            .Where(slot => string.IsNullOrWhiteSpace(slot.ID) || !createdSlotIds.ContainsKey(slot.ID!))
            .Where(static slot => !string.Equals(slot.Name?.Value, "Assets", StringComparison.Ordinal));
    }

    private Slot[] GetObservedDatasetSourceRoots()
    {
        return observedDatasetSourceRoots ??= EnumerateObservedDatasetSourceRoots().ToArray();
    }

    private static ObservedSourceRootPlacement SelectDeterministicObservedPlacement(
        IReadOnlyCollection<ObservedSourceRootPlacement> candidates,
        string ambiguousMessage,
        double tolerance)
    {
        ObservedSourceRootPlacement selected = candidates
            .OrderBy(static candidate => candidate.ReferenceMeshCode, StringComparer.Ordinal)
            .ThenBy(static candidate => candidate.SlotId, StringComparer.Ordinal)
            .First();
        if (candidates.Any(candidate => !AreEquivalentPositions(candidate.Position, selected.Position, tolerance)))
        {
            throw new InvalidOperationException(ambiguousMessage);
        }

        return selected;
    }

    private static bool AreEquivalentPositions(ResoniteFloat3 left, ResoniteFloat3 right, double tolerance)
    {
        return Math.Abs(left.X - right.X) <= tolerance
            && Math.Abs(left.Y - right.Y) <= tolerance
            && Math.Abs(left.Z - right.Z) <= tolerance;
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

    private readonly record struct SourceRootPlacement(
        ResoniteFloat3 RootPosition,
        ResoniteFloat3 LocalPositionReferenceRoot);

    private readonly record struct ObservedSourceRootPlacement(
        ResoniteFloat3 Position,
        string ReferenceMeshCode,
        string SlotId);

    private readonly record struct CanonicalParentSourceFile(
        string SourceFileRelativePath,
        string RootMeshCode,
        int? LodLevel);

    private readonly record struct SharedSlotIndexKey(
        string ParentSlotId,
        string SlotName);
}
