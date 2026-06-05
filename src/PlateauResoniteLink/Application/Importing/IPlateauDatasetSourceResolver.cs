using System.Threading;
using System.Threading.Tasks;

namespace PlateauResoniteLink.Application.Importing;

internal interface IPlateauDatasetSourceResolver
{
    Task<ValidatedPlateauImportRequest> ResolveAsync(
        ValidatedPlateauImportRequest request,
        string workRoot,
        CancellationToken cancellationToken = default);
}
