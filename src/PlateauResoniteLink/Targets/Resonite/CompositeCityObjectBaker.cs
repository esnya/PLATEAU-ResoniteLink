using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Targets.Resonite;

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
        return FlushAllSequentiallyAsync(onBakedCityObject, cancellationToken);
    }

    private async Task FlushAllSequentiallyAsync(
        Func<ResoniteConstructionCityObject, CancellationToken, Task> onBakedCityObject,
        CancellationToken cancellationToken)
    {
        foreach (IResoniteBufferedCityObjectBaker baker in bakers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await baker.FlushAllAsync(onBakedCityObject, cancellationToken);
        }
    }

    public IEnumerable<(string Name, int InputCount, int OutputCount)> GetBakeSummaries()
    {
        return bakers.Select(static baker => (baker.Name, baker.BakedInputCityObjectCount, baker.BakedOutputCityObjectCount));
    }
}
