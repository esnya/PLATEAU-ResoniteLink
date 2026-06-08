using System.Threading;
using System.Threading.Tasks;

namespace PlateauResoniteLink.Application.Importing;

internal delegate Task<ResolvedLocalPlateauImportRequest> ResolvePlateauDatasetSource(
    ValidatedPlateauImportRequest request,
    string workRoot,
    CancellationToken cancellationToken = default);
