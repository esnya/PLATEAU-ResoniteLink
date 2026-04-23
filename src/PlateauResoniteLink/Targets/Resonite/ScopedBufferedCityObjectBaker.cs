using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PlateauResoniteLink.Targets.Resonite;

internal sealed class ScopedBufferedCityObjectBaker(
    string name,
    Func<IResoniteBufferedCityObjectBaker> bakerFactory,
    Func<ResoniteConstructionCityObject, bool>? canBufferCityObject = null) : IResoniteBufferedCityObjectBaker
{
    private readonly Dictionary<ScopedBakeKey, IResoniteBufferedCityObjectBaker> bakersByScope = [];
    private readonly Func<ResoniteConstructionCityObject, bool> canBufferPredicate = canBufferCityObject ?? (_ => true);

    public string Name { get; } = name;

    public int BakedInputCityObjectCount => bakersByScope.Values.Sum(static baker => baker.BakedInputCityObjectCount);

    public int BakedOutputCityObjectCount => bakersByScope.Values.Sum(static baker => baker.BakedOutputCityObjectCount);

    public ValueTask<BufferedCityObjectBufferResult> TryBufferAsync(
        ResoniteConstructionCityObject cityObject,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cityObject);

        if (!canBufferPredicate(cityObject))
        {
            return ValueTask.FromResult(new BufferedCityObjectBufferResult(Buffered: false, []));
        }

        ScopedBakeKey scopeKey = CreateScopedBakeKey(cityObject);
        if (!bakersByScope.TryGetValue(scopeKey, out IResoniteBufferedCityObjectBaker? baker))
        {
            baker = bakerFactory();
            bakersByScope.Add(scopeKey, baker);
        }

        return baker.TryBufferAsync(cityObject, cancellationToken);
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
        }
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
