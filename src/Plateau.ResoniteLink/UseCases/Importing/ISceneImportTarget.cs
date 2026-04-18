using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Application.Importing;

public interface ISceneImportTarget : IAsyncDisposable
{
    Task EnsureConnectedAsync(
        PlateauImportRequest request,
        CancellationToken cancellationToken = default);

    Task BeginAsync(
        SceneBuildRequest request,
        CancellationToken cancellationToken = default);

    Task ProcessCityObjectAsync(
        ImportedCityObject cityObject,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> CompleteAsync(CancellationToken cancellationToken = default);
}
