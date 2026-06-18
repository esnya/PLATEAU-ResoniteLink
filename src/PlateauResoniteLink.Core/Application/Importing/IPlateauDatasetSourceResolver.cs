using System.Threading;
using System.Threading.Tasks;

namespace PlateauResoniteLink.Core.Application.Importing;

public interface IPlateauDatasetSourceResolver
{
    Task<ResolvedLocalPlateauImportRequest> ResolveAsync(
        ValidatedPlateauImportRequest request,
        string workRoot,
        CancellationToken cancellationToken = default);
}
