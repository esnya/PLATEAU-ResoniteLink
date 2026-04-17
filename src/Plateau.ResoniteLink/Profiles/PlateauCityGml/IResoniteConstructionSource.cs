using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Application.Importing;

public interface IResoniteConstructionSource
{
    ResoniteConstructionMetadata Metadata { get; }

    [Obsolete(
        "ReadCommonMaterialsAsync is obsolete. Runtime common-material setup uses package catalog instead of source enumeration.")]
    IAsyncEnumerable<ResoniteMaterialBinding> ReadCommonMaterialsAsync(
        CancellationToken cancellationToken = default);

    IEnumerable<ResoniteConstructionCityObject> ReadCityObjects();

    IAsyncEnumerable<ResoniteConstructionCityObject> ReadCityObjectsAsync(
        CancellationToken cancellationToken = default);
}
