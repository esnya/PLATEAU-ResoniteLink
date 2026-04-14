using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Cli;

internal interface IResoniteBufferedCityObjectBaker
{
    string Name { get; }

    int BakedInputCityObjectCount { get; }

    int BakedOutputCityObjectCount { get; }

    ValueTask<bool> TryBufferAsync(
        ResoniteConstructionCityObject cityObject,
        CancellationToken cancellationToken = default);

    Task FlushAllAsync(
        Func<ResoniteConstructionCityObject, CancellationToken, Task> onBakedCityObject,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ResoniteConstructionCityObject>> FlushAllAsync(
        CancellationToken cancellationToken = default);
}
