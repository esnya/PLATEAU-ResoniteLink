namespace PlateauResoniteLink.Application.Importing;

public interface IPlateauDatasetSourceResolver
{
    Task<ValidatedPlateauImportRequest> ResolveAsync(
        ValidatedPlateauImportRequest request,
        string workRoot,
        CancellationToken cancellationToken = default);
}
