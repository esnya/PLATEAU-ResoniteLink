using System.Threading;
using System.Threading.Tasks;

using PlateauResoniteLink.Core.Application.Importing;

namespace PlateauResoniteLink.Plateau.Application.Importing;

internal interface IResolvedPlateauSceneSourceReader
{
    Task<ImportedSceneSourceSnapshot> ReadAsync(
        ResolvedLocalPlateauImportRequest request,
        CancellationToken cancellationToken = default);
}
