using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Cli;

internal sealed class CompositeCityObjectBaker(params IResoniteBufferedCityObjectBaker[] bakers)
{
    private readonly IResoniteBufferedCityObjectBaker[] bakers = bakers;

    public async ValueTask<IReadOnlyList<ResoniteConstructionCityObject>> BufferAsync(
        ResoniteConstructionCityObject cityObject,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cityObject);

        foreach (IResoniteBufferedCityObjectBaker baker in bakers)
        {
            if (await baker.TryBufferAsync(cityObject, cancellationToken))
            {
                return [];
            }
        }

        return [cityObject];
    }

    public async Task<IReadOnlyList<ResoniteConstructionCityObject>> FlushAllAsync(
        CancellationToken cancellationToken = default)
    {
        List<ResoniteConstructionCityObject> bakedCityObjects = [];
        foreach (IResoniteBufferedCityObjectBaker baker in bakers)
        {
            IReadOnlyList<ResoniteConstructionCityObject> baked = await baker.FlushAllAsync(cancellationToken);
            if (baked.Count > 0)
            {
                bakedCityObjects.AddRange(baked);
            }
        }

        return bakedCityObjects;
    }

    public IEnumerable<(string Name, int InputCount, int OutputCount)> GetBakeSummaries()
    {
        return bakers.Select(static baker => (baker.Name, baker.BakedInputCityObjectCount, baker.BakedOutputCityObjectCount));
    }
}
