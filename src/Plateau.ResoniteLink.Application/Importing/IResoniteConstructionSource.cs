using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Application.Importing;

public interface IResoniteConstructionSource
{
    ResoniteConstructionMetadata Metadata { get; }

    IAsyncEnumerable<ResoniteMaterialBinding> ReadCommonMaterialsAsync(
        CancellationToken cancellationToken = default);

    IEnumerable<ResoniteConstructionCityObject> ReadCityObjects();

    IAsyncEnumerable<ResoniteConstructionCityObject> ReadCityObjectsAsync(
        CancellationToken cancellationToken = default);
}
