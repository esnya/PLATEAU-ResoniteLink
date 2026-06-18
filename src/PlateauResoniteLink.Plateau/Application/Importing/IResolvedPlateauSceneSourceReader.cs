using System.Threading;
using System.Threading.Tasks;

namespace PlateauResoniteLink.Application.Importing;

internal interface IResolvedPlateauSceneSourceReader
{
    Task<ImportedSceneSourceSnapshot> ReadAsync(
        ResolvedLocalPlateauImportRequest request,
        CancellationToken cancellationToken = default);
}
