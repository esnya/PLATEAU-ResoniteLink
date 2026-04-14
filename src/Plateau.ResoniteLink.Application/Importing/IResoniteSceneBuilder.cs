using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Application.Importing;

public interface IResoniteSceneBuilder : IAsyncDisposable
{
    /// <summary>
    /// Verifies transport/session availability before source bootstrap.
    /// Implementations must treat <paramref name="request"/> as informational only and must not
    /// depend on source-resolution side effects or previously created external object identifiers.
    /// </summary>
    Task EnsureConnectedAsync(
        PlateauImportRequest request,
        CancellationToken cancellationToken = default);

    Task BeginAsync(
        ResoniteConstructionMetadata metadata,
        string workRoot,
        CancellationToken cancellationToken = default);

    Task StartCommonMaterialWarmupAsync(
        IReadOnlyList<ResoniteMaterialBinding> materials,
        CancellationToken cancellationToken = default);

    Task ProcessCityObjectAsync(
        ResoniteConstructionCityObject cityObject,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> CompleteAsync(CancellationToken cancellationToken = default);
}
