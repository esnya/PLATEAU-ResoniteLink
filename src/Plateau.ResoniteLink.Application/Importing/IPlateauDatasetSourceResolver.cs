namespace Plateau.ResoniteLink.Application.Importing;

public interface IPlateauDatasetSourceResolver
{
    Task<ValidatedPlateauImportRequest> ResolveAsync(
        ValidatedPlateauImportRequest request,
        string workRoot,
        CancellationToken cancellationToken = default);
}
