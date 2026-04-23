using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PlateauResoniteLink.Targets.Resonite;

internal sealed class ScopedBufferedCityObjectBaker(
    string name,
    Func<IResoniteBufferedCityObjectBaker> bakerFactory,
    Func<ResoniteConstructionCityObject, bool>? canBufferCityObject = null,
    int? maxBufferedScopes = null) : IResoniteBufferedCityObjectBaker
{
    private readonly Dictionary<ScopedBakeKey, IResoniteBufferedCityObjectBaker> bakersByScope = [];
    private readonly LinkedList<ScopedBakeKey> bufferedScopeOrder = [];
    private readonly Dictionary<ScopedBakeKey, LinkedListNode<ScopedBakeKey>> bufferedScopeNodes = [];
    private readonly Func<ResoniteConstructionCityObject, bool> canBufferPredicate = canBufferCityObject ?? (_ => true);

    public string Name { get; } = name;

    public int BakedInputCityObjectCount => bakersByScope.Values.Sum(static baker => baker.BakedInputCityObjectCount);

    public int BakedOutputCityObjectCount => bakersByScope.Values.Sum(static baker => baker.BakedOutputCityObjectCount);

    public async ValueTask<BufferedCityObjectBufferResult> TryBufferAsync(
        ResoniteConstructionCityObject cityObject,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cityObject);

        if (!canBufferPredicate(cityObject))
        {
            return new BufferedCityObjectBufferResult(Buffered: false, []);
        }

        ScopedBakeKey scopeKey = CreateScopedBakeKey(cityObject);
        if (!bakersByScope.TryGetValue(scopeKey, out IResoniteBufferedCityObjectBaker? baker))
        {
            baker = bakerFactory();
            bakersByScope.Add(scopeKey, baker);
            AttachBufferedScope(scopeKey);
        }
        else
        {
            TrackBufferedScopeAccess(scopeKey);
        }

        BufferedCityObjectBufferResult result = await baker.TryBufferAsync(cityObject, cancellationToken);
        if (!result.Buffered || !maxBufferedScopes.HasValue || bakersByScope.Count <= maxBufferedScopes.Value)
        {
            return result;
        }

        List<ResoniteConstructionCityObject> readyCityObjects = [.. result.ReadyCityObjects];
        while (maxBufferedScopes.HasValue && bakersByScope.Count > maxBufferedScopes.Value)
        {
            ScopedBakeKey overflowScopeKey = GetOldestBufferedScopeKey(excluding: scopeKey);
            readyCityObjects.AddRange(await FlushScopeAsync(overflowScopeKey, cancellationToken));
        }

        return new BufferedCityObjectBufferResult(Buffered: true, readyCityObjects);
    }

    public async Task<IReadOnlyList<ResoniteConstructionCityObject>> FlushAllAsync(
        CancellationToken cancellationToken = default)
    {
        List<ResoniteConstructionCityObject> bakedCityObjects = [];
        await FlushAllAsync(
            (bakedCityObject, _) =>
            {
                bakedCityObjects.Add(bakedCityObject);
                return Task.CompletedTask;
            },
            cancellationToken);
        return bakedCityObjects;
    }

    public async Task FlushAllAsync(
        Func<ResoniteConstructionCityObject, CancellationToken, Task> onBakedCityObject,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(onBakedCityObject);

        foreach ((ScopedBakeKey scopeKey, IResoniteBufferedCityObjectBaker baker) in bakersByScope
                     .OrderBy(static pair => pair.Key, ScopedBakeKeyComparer.Instance)
                     .ToArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            await baker.FlushAllAsync(onBakedCityObject, cancellationToken);
            _ = bakersByScope.Remove(scopeKey);
            DetachBufferedScope(scopeKey);
        }
    }

    private async Task<IReadOnlyList<ResoniteConstructionCityObject>> FlushScopeAsync(
        ScopedBakeKey scopeKey,
        CancellationToken cancellationToken)
    {
        if (!bakersByScope.Remove(scopeKey, out IResoniteBufferedCityObjectBaker? baker))
        {
            return [];
        }

        DetachBufferedScope(scopeKey);
        return await baker.FlushAllAsync(cancellationToken);
    }

    private static ScopedBakeKey CreateScopedBakeKey(ResoniteConstructionCityObject cityObject)
    {
        if (string.IsNullOrWhiteSpace(cityObject.SourceFileRelativePath)
            && string.IsNullOrWhiteSpace(cityObject.SourceUnitKey))
        {
            throw new InvalidOperationException(
                $"Buffered city object '{cityObject.PackageName}/{cityObject.SlotKey}' must provide SourceFileRelativePath or SourceUnitKey before non-DEM bake.");
        }

        return new ScopedBakeKey(
            ResonitePlacementPolicy.ResolveCityGmlScopeKey(cityObject),
            cityObject.LodLevel);
    }

    private readonly record struct ScopedBakeKey(string CityGmlScopeKey, int? LodLevel);

    private void AttachBufferedScope(ScopedBakeKey scopeKey)
    {
        LinkedListNode<ScopedBakeKey> node = bufferedScopeOrder.AddLast(scopeKey);
        bufferedScopeNodes[scopeKey] = node;
    }

    private void TrackBufferedScopeAccess(ScopedBakeKey scopeKey)
    {
        if (!bufferedScopeNodes.TryGetValue(scopeKey, out LinkedListNode<ScopedBakeKey>? node))
        {
            throw new InvalidOperationException($"Buffered scope order tracking is missing '{scopeKey}'.");
        }

        if (node != bufferedScopeOrder.Last)
        {
            bufferedScopeOrder.Remove(node);
            bufferedScopeOrder.AddLast(node);
        }
    }

    private void DetachBufferedScope(ScopedBakeKey scopeKey)
    {
        if (!bufferedScopeNodes.Remove(scopeKey, out LinkedListNode<ScopedBakeKey>? node))
        {
            return;
        }

        bufferedScopeOrder.Remove(node);
    }

    private ScopedBakeKey GetOldestBufferedScopeKey(ScopedBakeKey excluding)
    {
        LinkedListNode<ScopedBakeKey>? node = bufferedScopeOrder.First;
        while (node is not null)
        {
            if (!node.Value.Equals(excluding))
            {
                return node.Value;
            }

            node = node.Next;
        }

        throw new InvalidOperationException("Buffered scope overflow had no eligible scope to flush.");
    }

    private sealed class ScopedBakeKeyComparer : IComparer<ScopedBakeKey>
    {
        internal static readonly ScopedBakeKeyComparer Instance = new();

        public int Compare(ScopedBakeKey x, ScopedBakeKey y)
        {
            int compare = string.CompareOrdinal(x.CityGmlScopeKey, y.CityGmlScopeKey);
            if (compare != 0)
            {
                return compare;
            }

            return Nullable.Compare(x.LodLevel, y.LodLevel);
        }
    }
}
