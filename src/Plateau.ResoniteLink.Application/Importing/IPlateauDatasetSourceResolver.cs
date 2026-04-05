using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Application.Importing;

public interface IPlateauDatasetSourceResolver
{
    Task<PlateauImportRequest> ResolveAsync(
        PlateauImportRequest request,
        string outputRoot,
        CancellationToken cancellationToken = default);
}
