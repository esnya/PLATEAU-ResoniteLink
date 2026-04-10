using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Application.Importing;

public interface IResoniteConstructionSource
{
    ResoniteConstructionMetadata Metadata { get; }

    IAsyncEnumerable<ResoniteConstructionCityObject> ReadCityObjectsAsync(
        CancellationToken cancellationToken = default);
}
