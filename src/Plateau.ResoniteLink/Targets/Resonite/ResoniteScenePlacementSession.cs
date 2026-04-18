using System.Collections.Concurrent;
using System.Globalization;

using GeographicLib;

using Plateau.ResoniteLink.Domain.Importing;

using ResoniteLink;

namespace Plateau.ResoniteLink.Targets.Resonite;

internal sealed class ResoniteScenePlacementSession(
    CreatedSlot datasetRootSlot,
    CreatedSlot datasetAssetsRootSlot,
    ResoniteLocalOrigin requestLocalOrigin,
    IReadOnlyDictionary<string, string> cityGmlSlotNamesByRelativePath,
    SceneAnchor? initialSceneAnchor,
    Func<IResoniteLinkClient, string, string, ResoniteFloat3?, ResoniteFloatQ?, CancellationToken, Task<CreatedSlot>> createSlotAsync)
{
    private readonly AsyncCompletedResultCache<(string ParentSlotId, string SlotName), CreatedSlot> sharedSlotCache = new();
    private readonly AsyncCompletedResultCache<CanonicalParentScopeKey, CanonicalParentScope> canonicalParentScopeCache = new();
    private readonly ConcurrentDictionary<string, byte> createdSlotIds = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, CreatedSlot> sharedSlotIndex = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Slot> observedSlotSnapshotsById = new(StringComparer.Ordinal);
    public SceneAnchor? SceneAnchor { get; private set; } = initialSceneAnchor;

    public string? DatasetLicenseComponentId { get; set; }

    public void IndexBootstrapHierarchy(ResoniteSceneBootstrapState bootstrapState)
    {
        if (bootstrapState.DatasetRootSnapshot is not null)
        {
            observedSlotSnapshotsById.Clear();
            IndexObservedSlotSnapshot(bootstrapState.DatasetRootSnapshot);
        }
        else
        {
            IndexCreatedSharedSlot("Root", bootstrapState.DatasetRootSlot);
        }

        IndexCreatedSharedSlot(bootstrapState.DatasetRootSlot.SlotId, bootstrapState.DatasetAssetsRootSlot);
        IndexCreatedSharedSlot(bootstrapState.DatasetAssetsRootSlot.SlotId, bootstrapState.CommonAssetsRootSlot);
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

        return Add(anchor.Value.Position, ComputeMeshCodeOffset(anchor.Value.MeshCode, meshCode));
    }

    public Task<CreatedSlot> GetOrCreateSharedChildSlotAsync(
        IResoniteLinkClient client,
        string parentId,
        string slotName,
        CancellationToken cancellationToken)
    {
        return GetOrCreateSharedChildSlotByIdAsync(client, parentId, slotName, null, null, cancellationToken);
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
        string cityGmlScopeKey = ResolveCityGmlScopeKey(cityObject);
        string cityGmlSlotName = ResolveCityGmlSlotName(cityObject, cityGmlScopeKey);
        string rootMeshCode = ResolveRequiredSourceFileRootMeshCode(cityGmlSlotName, cityObject.ActualMeshCode);
        CanonicalParentScope parentScope = await canonicalParentScopeCache.GetOrCreateAsync(
            new CanonicalParentScopeKey(cityGmlScopeKey, rootMeshCode, cityObject.LodLevel),
            ct => CreateCanonicalParentScopeAsync(client, cityGmlSlotName, rootMeshCode, cityObject.LodLevel, ct),
            cancellationToken);

        return new ObjectSlotHierarchy(
            parentScope.AssetLodSlot,
            parentScope.LodSlot,
            cityObject.DisplayName,
            ResolveCityObjectLocalPosition(requestLocalOrigin, rootMeshCode, cityObject.Transform.Position),
            cityObject.Transform.Rotation);
    }

    private async Task<CanonicalParentScope> CreateCanonicalParentScopeAsync(
        IResoniteLinkClient client,
        string cityGmlSlotName,
        string rootMeshCode,
        int? lodLevel,
        CancellationToken cancellationToken)
    {
        string lodSlotName = FormatLodSlotName(lodLevel);
        ResoniteFloat3 rootPosition = NormalizeMeshRootPosition(ResolveMeshCodeRootPosition(rootMeshCode));
        CreatedSlot? cityGmlSlot = TryGetIndexedSharedChildSlot(datasetRootSlot.SlotId, cityGmlSlotName);
        CreatedSlot? assetCityGmlSlot = TryGetIndexedSharedChildSlot(datasetAssetsRootSlot.SlotId, cityGmlSlotName);
        CreatedSlot? lodSlot = cityGmlSlot is null ? null : TryGetIndexedSharedChildSlot(cityGmlSlot.Value.SlotId, lodSlotName);
        CreatedSlot? assetLodSlot = assetCityGmlSlot is null ? null : TryGetIndexedSharedChildSlot(assetCityGmlSlot.Value.SlotId, lodSlotName);

        if (cityGmlSlot is null)
        {
            cityGmlSlot = await TryGetUniqueChildSlotByNameWithRetryAsync(
                client,
                datasetRootSlot.SlotId,
                cityGmlSlotName,
                3,
                TimeSpan.FromMilliseconds(50),
                cancellationToken);
            if (cityGmlSlot is not null)
            {
                cityGmlSlot = IndexCreatedSharedSlot(datasetRootSlot.SlotId, cityGmlSlot.Value, rootPosition);
            }
        }

        if (assetCityGmlSlot is null)
        {
            assetCityGmlSlot = await TryGetUniqueChildSlotByNameWithRetryAsync(
                client,
                datasetAssetsRootSlot.SlotId,
                cityGmlSlotName,
                3,
                TimeSpan.FromMilliseconds(50),
                cancellationToken);
            if (assetCityGmlSlot is not null)
            {
                assetCityGmlSlot = IndexCreatedSharedSlot(datasetAssetsRootSlot.SlotId, assetCityGmlSlot.Value);
            }
        }

        cityGmlSlot ??= await GetOrCreateSharedChildSlotByIdAsync(
            client,
            datasetRootSlot.SlotId,
            cityGmlSlotName,
            rootPosition,
            null,
            cancellationToken);
        assetCityGmlSlot ??= await GetOrCreateSharedChildSlotByIdAsync(
            client,
            datasetAssetsRootSlot.SlotId,
            cityGmlSlotName,
            null,
            null,
            cancellationToken);
        lodSlot ??= await GetOrCreateSharedChildSlotByIdAsync(
            client,
            cityGmlSlot.Value.SlotId,
            lodSlotName,
            null,
            null,
            cancellationToken);
        assetLodSlot ??= await GetOrCreateSharedChildSlotByIdAsync(
            client,
            assetCityGmlSlot.Value.SlotId,
            lodSlotName,
            null,
            null,
            cancellationToken);

        if (SceneAnchor is { } anchor
            && (string.Equals(anchor.MeshCode, rootMeshCode, StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(anchor.ReferenceSourceFileRootId)))
        {
            SceneAnchor = anchor with
            {
                LocationSlotId = cityGmlSlot.Value.SlotId,
                MeshCode = rootMeshCode,
                Position = rootPosition,
                ReferenceSourceFileRootId = cityGmlSlot.Value.SlotId,
            };
        }

        return new CanonicalParentScope(
            cityGmlSlot ?? throw new InvalidOperationException($"CityGML slot '{cityGmlSlotName}' was not resolved."),
            assetCityGmlSlot ?? throw new InvalidOperationException($"Asset CityGML slot '{cityGmlSlotName}' was not resolved."),
            lodSlot ?? throw new InvalidOperationException($"LOD slot '{lodSlotName}' was not resolved."),
            assetLodSlot ?? throw new InvalidOperationException($"Asset LOD slot '{lodSlotName}' was not resolved."));
    }

    private async Task<CreatedSlot> GetOrCreateSharedChildSlotByIdAsync(
        IResoniteLinkClient client,
        string parentId,
        string slotName,
        ResoniteFloat3? position,
        ResoniteFloatQ? rotation,
        CancellationToken cancellationToken)
    {
        return await sharedSlotCache.GetOrCreateAsync(
            (parentId, slotName),
            ct => GetOrCreateSharedChildSlotCoreAsync(client, parentId, slotName, position, rotation, ct),
            cancellationToken);
    }

    private async Task<CreatedSlot> GetOrCreateSharedChildSlotCoreAsync(
        IResoniteLinkClient client,
        string parentId,
        string slotName,
        ResoniteFloat3? position,
        ResoniteFloatQ? rotation,
        CancellationToken cancellationToken)
    {
        CreatedSlot? indexedSlot = TryGetIndexedSharedChildSlot(parentId, slotName);
        if (indexedSlot is not null)
        {
            createdSlotIds[indexedSlot.Value.SlotId] = 0;
            return indexedSlot.Value;
        }

        CreatedSlot createdSlot = await createSlotAsync(client, parentId, slotName, position, rotation, cancellationToken);
        createdSlotIds[createdSlot.SlotId] = 0;
        return IndexCreatedSharedSlot(parentId, createdSlot, position);
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
                sharedSlotIndex[CreateSharedSlotIndexKey(slot.ID!, child.Name!.Value)] = new CreatedSlot(child.ID!, child.Name.Value);
            }

            IndexObservedSlotSnapshot(child);
        }
    }

    private CreatedSlot? TryGetIndexedSharedChildSlot(string parentId, string slotName)
    {
        return sharedSlotIndex.TryGetValue(CreateSharedSlotIndexKey(parentId, slotName), out CreatedSlot createdSlot)
            ? createdSlot
            : null;
    }

    private CreatedSlot IndexCreatedSharedSlot(string parentId, CreatedSlot createdSlot, ResoniteFloat3? position = null)
    {
        sharedSlotIndex[CreateSharedSlotIndexKey(parentId, createdSlot.SlotName)] = createdSlot;
        observedSlotSnapshotsById[createdSlot.SlotId] = new Slot
        {
            ID = createdSlot.SlotId,
            Name = new Field_string { Value = createdSlot.SlotName },
            Parent = new Reference { TargetID = parentId },
            Position = position is null ? null : CreateFloat3(position),
        };
        return createdSlot;
    }

    private static string CreateSharedSlotIndexKey(string parentId, string slotName)
    {
        return string.Create(CultureInfo.InvariantCulture, $"{parentId}\n{slotName}");
    }

    private static async Task<CreatedSlot?> TryGetUniqueChildSlotByNameWithRetryAsync(
        IResoniteLinkClient client,
        string parentId,
        string slotName,
        int attemptLimit,
        TimeSpan retryDelay,
        CancellationToken cancellationToken)
    {
        for (int attempt = 1; attempt <= attemptLimit; attempt++)
        {
            ResoniteSceneSlotSnapshot snapshot = await ResoniteSceneSlotSnapshot.CreateAsync(client, parentId, 1, cancellationToken);
            CreatedSlot? existingSlot = TryFindUniqueChildSlotByName(snapshot.Root, slotName, parentId);
            if (existingSlot is not null)
            {
                return existingSlot;
            }

            if (attempt < attemptLimit && retryDelay > TimeSpan.Zero)
            {
                await Task.Delay(retryDelay, cancellationToken);
            }
        }

        return null;
    }

    private static CreatedSlot? TryFindUniqueChildSlotByName(Slot? parentSlot, string slotName, string? parentId = null)
    {
        if (parentSlot?.Children is null)
        {
            return null;
        }

        Slot[] matches = parentSlot.Children
            .Where(child => string.Equals(child.Name?.Value, slotName, StringComparison.Ordinal))
            .ToArray();
        if (matches.Length == 0)
        {
            return null;
        }

        Slot preferredMatch = matches
            .OrderByDescending(static slot => slot.Components?.Count ?? 0)
            .ThenBy(static slot => slot.ID, StringComparer.Ordinal)
            .First();
        string existingSlotId = preferredMatch.ID
            ?? throw new InvalidOperationException(
                $"Child slot '{slotName}' under parent '{parentId ?? parentSlot.ID ?? "<unknown>"}' did not surface an ID.");
        return new CreatedSlot(existingSlotId, slotName);
    }

    private string ResolveCityGmlSlotName(ResoniteConstructionCityObject cityObject, string cityGmlScopeKey)
    {
        if (cityGmlSlotNamesByRelativePath.TryGetValue(cityGmlScopeKey, out string? slotName)
            && !string.IsNullOrWhiteSpace(slotName))
        {
            return slotName;
        }

        if (!string.IsNullOrWhiteSpace(cityObject.SourceFileRelativePath))
        {
            string fileStem = Path.GetFileNameWithoutExtension(cityObject.SourceFileRelativePath);
            if (!string.IsNullOrWhiteSpace(fileStem))
            {
                return fileStem;
            }
        }

        if (!string.IsNullOrWhiteSpace(cityObject.SourceUnitKey))
        {
            return cityObject.SourceUnitKey!;
        }

        return cityObject.SlotKey;
    }

    private static string ResolveCityGmlScopeKey(ResoniteConstructionCityObject cityObject)
    {
        if (!string.IsNullOrWhiteSpace(cityObject.SourceFileRelativePath))
        {
            return cityObject.SourceFileRelativePath!;
        }

        if (!string.IsNullOrWhiteSpace(cityObject.SourceUnitKey))
        {
            return cityObject.SourceUnitKey!;
        }

        return cityObject.SlotKey;
    }

    private static string ResolveRequiredSourceFileRootMeshCode(string cityGmlSlotName, string actualMeshCode)
    {
        if (ResoniteSourceMeshCodeAnchor.TryGetConcreteMeshCode(cityGmlSlotName, out string meshCode))
        {
            return meshCode;
        }

        if (PlateauMeshCode.TryGetCenter(actualMeshCode, out _))
        {
            return actualMeshCode;
        }

        throw new InvalidOperationException(
            $"Source-file root '{cityGmlSlotName}' did not contain a concrete meshcode and actual mesh '{actualMeshCode}' was not concrete.");
    }

    private static string FormatLodSlotName(int? lodLevel)
    {
        return lodLevel.HasValue
            ? string.Create(CultureInfo.InvariantCulture, $"LOD{lodLevel.Value}")
            : "LOD";
    }

    private static ResoniteFloat3 ResolveCityObjectLocalPosition(
        ResoniteLocalOrigin requestOrigin,
        string rootMeshCode,
        ResoniteFloat3 cityObjectPosition)
    {
        if (!PlateauMeshCode.TryGetCenter(rootMeshCode, out ResoniteLocalOrigin rootMeshCenter))
        {
            return cityObjectPosition;
        }

        ResoniteFloat3 rootOffsetFromRequest = ComputeOriginOffset(requestOrigin, rootMeshCenter);
        return Subtract(cityObjectPosition, rootOffsetFromRequest);
    }

    private static ResoniteFloat3 NormalizeMeshRootPosition(ResoniteFloat3 position)
    {
        return new ResoniteFloat3(position.X, 0.0, position.Z);
    }

    private static ResoniteFloat3 Add(ResoniteFloat3 left, ResoniteFloat3 right)
    {
        return new ResoniteFloat3(left.X + right.X, left.Y + right.Y, left.Z + right.Z);
    }

    private static ResoniteFloat3 Subtract(ResoniteFloat3 left, ResoniteFloat3 right)
    {
        return new ResoniteFloat3(left.X - right.X, left.Y - right.Y, left.Z - right.Z);
    }

    private static ResoniteFloat3 ComputeMeshCodeOffset(string referenceMeshCode, string meshCode)
    {
        if (!PlateauMeshCode.TryGetCenter(referenceMeshCode, out ResoniteLocalOrigin referenceCenter)
            || !PlateauMeshCode.TryGetCenter(meshCode, out ResoniteLocalOrigin currentCenter))
        {
            return new ResoniteFloat3(0.0, 0.0, 0.0);
        }

        return ComputeOriginOffset(referenceCenter, currentCenter);
    }

    private static ResoniteFloat3 ComputeOriginOffset(ResoniteLocalOrigin referenceCenter, ResoniteLocalOrigin currentCenter)
    {
        LocalCartesian cartesian = new(
            referenceCenter.Latitude,
            referenceCenter.Longitude,
            referenceCenter.Altitude,
            Geocentric.WGS84);
        (double x, double y, double z) eun = cartesian.Forward(
            currentCenter.Latitude,
            currentCenter.Longitude,
            currentCenter.Altitude);
        return new ResoniteFloat3(X: eun.x, Y: 0.0, Z: eun.y);
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

    internal sealed record ObjectSlotHierarchy(
        CreatedSlot AssetLodSlot,
        CreatedSlot LodSlot,
        string CityObjectSlotName,
        ResoniteFloat3 CityObjectLocalPosition,
        ResoniteFloatQ? CityObjectRotation);

    private sealed record CanonicalParentScope(
        CreatedSlot CityGmlSlot,
        CreatedSlot AssetCityGmlSlot,
        CreatedSlot LodSlot,
        CreatedSlot AssetLodSlot);

    private readonly record struct CanonicalParentScopeKey(
        string CityGmlScopeKey,
        string RootMeshCode,
        int? LodLevel);
}
