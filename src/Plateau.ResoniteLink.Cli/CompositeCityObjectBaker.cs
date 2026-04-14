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
            BufferedCityObjectBufferResult result = await baker.TryBufferAsync(cityObject, cancellationToken);
            if (result.Buffered)
            {
                return result.ReadyCityObjects;
            }
        }

        return [cityObject];
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

    public Task FlushAllAsync(
        Func<ResoniteConstructionCityObject, CancellationToken, Task> onBakedCityObject,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(onBakedCityObject);
        return Task.WhenAll(bakers.Select(baker => baker.FlushAllAsync(onBakedCityObject, cancellationToken)));
    }

    public IEnumerable<(string Name, int InputCount, int OutputCount)> GetBakeSummaries()
    {
        return bakers.Select(static baker => (baker.Name, baker.BakedInputCityObjectCount, baker.BakedOutputCityObjectCount));
    }
}
